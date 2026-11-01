using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Approval.AspNetCore;
using Approval.Certificate;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Forms;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length != 2 || args[0] != "--output")
    throw new ArgumentException("Usage: Approval.ContractTool --output <directory>");
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = "ContractGeneration"
});
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"));
builder.Services.AddScoped<IApprovalIdentityProvider, ContractIdentity>();
builder.Services.AddApprovalWorkflow<AppDbContext>(options => options.EnableBackgroundServices = false)
    .AddFormApprovals().AddCertificateModule();
builder.Services.AddApprovalAspNetCore();
await using var app = builder.Build();
app.MapApprovalEndpoints();
app.MapCertificateEndpoints();
app.MapOpenApi();
await app.StartAsync();
try
{
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    var document = JsonNode.Parse(await http.GetStringAsync(app.Urls.Single() + "/openapi/v1.json"))!;
    document["servers"] = new JsonArray(new JsonObject { ["url"] = "/" });
    document["info"]!["title"] = "Approval Workflow";
    document["info"]!["version"] = typeof(ApprovalOptions).Assembly.GetName().Version!.ToString(3);
    await WriteJsonAsync("approval.openapi.json", document);
}
finally
{
    await app.StopAsync();
}

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    Converters = { new JsonStringEnumConverter() }
};
var flowSchema = json.GetJsonSchemaAsNode(typeof(FlowDefinition));
flowSchema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
flowSchema["$id"] = "urn:approval:flow:1";
flowSchema["properties"]!["schemaVersion"] = new JsonObject { ["type"] = "integer", ["const"] = 1, ["default"] = 1 };
await WriteJsonAsync("flow.schema.json", flowSchema);
var formSchema = json.GetJsonSchemaAsNode(typeof(FormSchema));
formSchema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
formSchema["$id"] = "urn:approval:form:1";
await WriteJsonAsync("form.schema.json", formSchema);
var nullability = new NullabilityInfoContext();
foreach (var assembly in new[] { typeof(ApprovalOptions).Assembly, typeof(ApprovalAspNetCoreOptions).Assembly, typeof(CertificateService).Assembly })
{
    var lines = new List<string>();
    foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
    {
        lines.Add(type.FullName!);
        if (type.IsEnum)
            foreach (var name in Enum.GetNames(type)) lines.Add($"  {name} = {Convert.ToInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture)}");
        else
            lines.AddRange(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member is not MethodInfo { IsSpecialName: true })
                .Select(member => $"  {member.MemberType} {member}{NullableContract(member)}").Order(StringComparer.Ordinal));
    }
    await File.WriteAllTextAsync(Path.Combine(output, assembly.GetName().Name + ".api.txt"), string.Join('\n', lines) + "\n");
}
var legacy = typeof(global::Certificate.Api.CertificateApplication).Assembly;
await File.WriteAllTextAsync(Path.Combine(output, "Certificate.Api.forwards.txt"), string.Join('\n', legacy.GetForwardedTypes()
    .Select(type => $"{type.FullName} -> {type.Assembly.GetName().Name}").Order(StringComparer.Ordinal)) + "\n");
Console.WriteLine($"Generated contracts in {output}");

string NullableContract(MemberInfo member) => member switch
{
    MethodInfo method => $" [return={Describe(nullability.Create(method.ReturnParameter))}; args={string.Join(',', method.GetParameters().Select(ParameterContract))}]",
    ConstructorInfo constructor => $" [args={string.Join(',', constructor.GetParameters().Select(ParameterContract))}]",
    PropertyInfo property => $" [{Describe(nullability.Create(property))}]",
    FieldInfo field => $" [{Describe(nullability.Create(field))}]" + (field.IsLiteral ? $" value={JsonSerializer.Serialize(field.GetRawConstantValue())}" : ""),
    _ => ""
};

string ParameterContract(ParameterInfo parameter) => Describe(nullability.Create(parameter))
    + (parameter.HasDefaultValue ? $" default={JsonSerializer.Serialize(parameter.DefaultValue)}" : "");

static string Describe(NullabilityInfo info) => $"{info.ReadState}/{info.WriteState}"
    + (info.ElementType is { } element ? $"[{Describe(element)}]" : "")
    + (info.GenericTypeArguments.Length > 0 ? $"<{string.Join(',', info.GenericTypeArguments.Select(Describe))}>" : "");

async Task WriteJsonAsync(string name, JsonNode value) =>
    await File.WriteAllTextAsync(Path.Combine(output, name), Normalize(value)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

static JsonNode? Normalize(JsonNode? node) => node switch
{
    JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => KeyValuePair.Create(pair.Key, Normalize(pair.Value)))),
    JsonArray array => new JsonArray(array.Select(Normalize).ToArray()),
    _ => node?.DeepClone()
};

sealed class ContractIdentity : IApprovalIdentityProvider
{
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
