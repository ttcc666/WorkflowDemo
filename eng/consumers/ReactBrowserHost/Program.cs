using System.Net;
using System.Text.Json;
using Approval.AspNetCore;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Certificate.Api;
using Microsoft.Data.Sqlite;

var sampleIndex = Array.IndexOf(args, "--Tests:SampleRoot");
if (sampleIndex < 0 || sampleIndex + 1 >= args.Length) throw new InvalidOperationException("Pass the sample root explicitly");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = Path.GetFullPath(args[sampleIndex + 1]) });
if (!builder.Environment.IsDevelopment() || !builder.Configuration.GetValue<bool>("Certificate:DemoMode"))
    throw new InvalidOperationException("React browser host only runs in explicit development DemoMode");
var output = Path.GetFullPath(builder.Configuration["Tests:Output"] ?? throw new InvalidOperationException("Pass owned output directory"));
var database = Path.GetFullPath(new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default")).DataSource);
if (!database.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(database))
    throw new InvalidOperationException("Browser tests require a new database inside the owned output directory");

builder.AddCertificateApplication(approval => approval.AddActionHandler<HostEchoAction>()
    .AddHttpActions(new ApprovalHttpActionEndpoint("read-model", new Uri("https://erp.example.invalid/read"), new HashSet<string> { "GET" }, AllowSynchronousRead: true)));
builder.Services.AddSingleton<ScriptedRemote>();
builder.Services.AddHttpClient("Approval.WorkflowAction.Http")
    .ConfigurePrimaryHttpMessageHandler(sp => new ScriptedRemoteHandler(sp.GetRequiredService<ScriptedRemote>()));
await using var app = builder.Build();
await app.UseCertificateApplicationAsync();
app.MapGet("/api/test/http/received", (HttpContext http, IApprovalCurrentUser user, ScriptedRemote remote) =>
    user.GetUserId(http) == "admin" ? Results.Json(remote.Snapshot()) : Results.StatusCode(403));
app.MapPost("/api/test/http/fail-next", (HttpContext http, IApprovalCurrentUser user, ScriptedRemote remote, int count) =>
{
    if (user.GetUserId(http) != "admin") return Results.StatusCode(403);
    if (count is < 0 or > 10) return Results.BadRequest();
    remote.FailNext(count);
    return Results.NoContent();
});
app.Run();

internal sealed class HostEchoAction : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    public ApprovalActionDescriptor Descriptor { get; } = new("HostEcho", "宿主回显", "浏览器验收使用的宿主自定义动作",
        [ActionExecutionMode.Sync], ConfigVersion: 3, ConfigSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object", required = new[] { "label" },
            properties = new { label = new { type = "string", @default = "host-value" }, enabled = new { type = "boolean", @default = true } }
        }), AllowsContinue: true);

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) =>
        version == 3 && config.ValueKind == JsonValueKind.Object && config.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String
            ? [] : ["HostEcho requires configVersion 3 and a label"];
    private static ApprovalActionResult Calculate(ApprovalActionContext context, JsonElement config)
    {
        if (config.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return new(false);
        var bound = context.BindParameters(config);
        return new(true, JsonSerializer.SerializeToElement(new { label = bound.GetProperty("label").GetString() }));
    }
    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Calculate(context, config)); }
    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Calculate(context, config)); }
}

internal sealed record RemoteExchange(string Method, string Address, string? IdempotencyKey, string Body, int Status);
internal sealed class ScriptedRemote
{
    private readonly object _gate = new();
    private readonly List<RemoteExchange> _received = [];
    private int _failNext;
    public void FailNext(int count) { lock (_gate) _failNext = count; }
    public RemoteExchange[] Snapshot() { lock (_gate) return _received.ToArray(); }
    public int Record(string method, string address, string? key, string body)
    {
        lock (_gate)
        {
            var status = _failNext > 0 ? 503 : 200;
            if (_failNext > 0) _failNext--;
            _received.Add(new(method, address, key, body, status));
            return status;
        }
    }
}
internal sealed class ScriptedRemoteHandler(ScriptedRemote remote) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "erp.example.invalid") throw new InvalidOperationException("Only the registered test target is handled");
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues("Idempotency-Key", out var keys);
        var status = remote.Record(request.Method.Method, request.RequestUri.AbsoluteUri, keys?.SingleOrDefault(), body);
        return new((HttpStatusCode)status) { Content = new StringContent("{\"value\":42}", System.Text.Encoding.UTF8, "application/json") };
    }
}
