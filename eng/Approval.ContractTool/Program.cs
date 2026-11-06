using Approval.Forms.AspNetCore;
using Approval.Forms;
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
using Approval.Forms.Schema;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length != 2 || args[0] != "--output")
    throw new ArgumentException("Usage: Approval.ContractTool --output <directory>");
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var sampleContracts = Path.Combine("samples", "Approval.Certificate", "contracts");
var sampleOutput = Path.Combine(output, sampleContracts);
Directory.CreateDirectory(sampleOutput);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = "ContractGeneration"
});
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddOpenApi("approval");
builder.Services.AddOpenApi("certificate");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"));
builder.Services.AddScoped<IApprovalIdentityProvider, ContractIdentity>();
builder.Services.AddApprovalWorkflow<AppDbContext>(options => { options.EnableBackgroundServices = false; options.Outbox.EnableDispatcher = false; })
    .AddApprovalForms().AddCertificateModule();
builder.Services.AddApprovalAspNetCore();
builder.Services.AddApprovalFormsAspNetCore();
await using var app = builder.Build();
app.MapGroup("").WithGroupName("approval").MapApprovalEndpoints();
app.MapGroup("").WithGroupName("certificate").MapCertificateEndpoints();
app.MapOpenApi();
await app.StartAsync();
try
{
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    foreach (var (name, title, file) in new[]
    {
        ("approval", "Approval Workflow", "approval.openapi.json"),
        ("certificate", "Certificate Approval Sample", Path.Combine(sampleContracts, "certificate.openapi.json"))
    })
    {
        var document = JsonNode.Parse(await http.GetStringAsync(app.Urls.Single() + $"/openapi/{name}.json"))!;
        document["servers"] = new JsonArray(new JsonObject { ["url"] = "/" });
        document["info"]!["title"] = title;
        document["info"]!["version"] = typeof(ApprovalOptions).Assembly.GetName().Version!.ToString(3);
        foreach (var endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var permissions = endpoint.Metadata.GetMetadata<ApprovalPermissionMetadata>();
            if (permissions is null || permissions.Operation == "POST /definitions/{key}/import/validate"
                || permissions.Permissions.Any(p => p is ApprovalPermissions.DefinitionValidate or ApprovalPermissions.DefinitionSimulate or ApprovalPermissions.FileUpload)) continue;
            var path = System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, @"\{([^}:]+):[^}]+\}", "{$1}");
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Where(m => m is "POST" or "PUT" or "DELETE"))
                if (document["paths"]?[path]?[method.ToLowerInvariant()] is JsonObject operation)
                    AddCommandHeader(operation);
        }
        if (name == "certificate")
            foreach (var (path, method) in new[] { ("/api/certificates", "post"), ("/api/certificates/{id}", "put"), ("/api/certificates/{id}/revoke", "post") })
                if (document["paths"]?[path]?[method] is JsonObject operation) AddCommandHeader(operation, uuid: true);
        await WriteJsonAsync(file, document);
    }
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
flowSchema["properties"]!["schemaVersion"] = new JsonObject { ["type"] = "integer", ["const"] = 1 };
flowSchema["properties"]!["form"] = new JsonObject { ["anyOf"] = new JsonArray(json.GetJsonSchemaAsNode(typeof(FormSchema)), new JsonObject { ["type"] = "null" }) };
var actionSchema = flowSchema["properties"]!["nodes"]!["additionalProperties"]!["properties"]!["actions"]!;
actionSchema["minItems"] = 1;
actionSchema["maxItems"] = 20;
actionSchema["items"]!["type"] = "object";
actionSchema["items"]!["properties"]!["config"] = new JsonObject { ["type"] = "object" };
await WriteJsonAsync("flow.schema.json", flowSchema);
var packageSchema = new JsonSerializerOptions(json) { RespectRequiredConstructorParameters = true, NumberHandling = JsonNumberHandling.Strict }
    .GetJsonSchemaAsNode(typeof(DefinitionTransferPackage));
packageSchema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
packageSchema["$id"] = "urn:approval:definition-package:1";
packageSchema["type"] = "object";
packageSchema["additionalProperties"] = false;
var packageProperties = packageSchema["properties"]!;
packageProperties["packageVersion"] = new JsonObject { ["type"] = "integer", ["const"] = DefinitionTransferPackage.CurrentPackageVersion };
packageProperties["kind"] = new JsonObject { ["type"] = "string", ["const"] = DefinitionTransferPackage.DefinitionKind };
packageProperties["definition"] = new JsonObject { ["$ref"] = "urn:approval:flow:1" };
var sourceSchema = packageProperties["source"]!;
sourceSchema["additionalProperties"] = false;
sourceSchema["properties"]!["key"]!["minLength"] = 1;
sourceSchema["properties"]!["key"]!["maxLength"] = 100;
sourceSchema["properties"]!["name"]!["minLength"] = 1;
sourceSchema["properties"]!["name"]!["maxLength"] = 200;
sourceSchema["properties"]!["version"]!["minimum"] = 1;
await WriteJsonAsync("definition-package.schema.json", packageSchema);
var formSchema = json.GetJsonSchemaAsNode(typeof(FormSchema));
formSchema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
formSchema["$id"] = "urn:approval:form:1";
await WriteJsonAsync("form.schema.json", formSchema);
var nullability = new NullabilityInfoContext();
foreach (var (assembly, directory) in new[]
{
    (typeof(ApprovalOptions).Assembly, output),
    (typeof(ApprovalAspNetCoreOptions).Assembly, output),
    (typeof(Approval.Forms.Runtime.ApprovalFormOptions).Assembly, output),
    (typeof(ApprovalFormsAspNetCoreExtensions).Assembly, output),
    (typeof(Approval.Testing.ApprovalTestingExtensions).Assembly, output),
    (typeof(CertificateService).Assembly, sampleOutput)
})
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
    await File.WriteAllTextAsync(Path.Combine(directory, assembly.GetName().Name + ".api.txt"), string.Join('\n', lines) + "\n");
}
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

static void AddCommandHeader(JsonObject operation, bool uuid = false)
{
    var parameters = operation["parameters"] as JsonArray ?? new JsonArray();
    if (operation["parameters"] is null) operation["parameters"] = parameters;
    parameters.Add(new JsonObject
    {
        ["name"] = "Idempotency-Key", ["in"] = "header", ["required"] = true,
        ["description"] = uuid ? "Stable non-empty UUID for one logical submission; retain it and the original body after network errors or CommitOutcomeUnknown."
            : "Required stable key for one logical command. Same content replays the committed result; changed content returns 409. Retain the key and original content after unknown outcomes.",
        ["schema"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 }
    });
}

sealed class ContractIdentity : IApprovalIdentityProvider
{
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
