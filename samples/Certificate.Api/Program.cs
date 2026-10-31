using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.AspNetCore;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Notifications;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// 审批接口（/api/approval/*）、ProblemDetails 异常映射、字符串枚举
builder.Services.AddApprovalAspNetCore();

// 接入审批框架：宿主只需提供 DbContext、用户角色体系、当前用户和业务回调
builder.Services.AddSingleton<DemoIdentityProvider>();
builder.Services.AddSingleton<IApprovalIdentityProvider>(sp => sp.GetRequiredService<DemoIdentityProvider>());
builder.Services.AddSingleton<IApprovalContactProvider>(sp => sp.GetRequiredService<DemoIdentityProvider>());
builder.Services.AddSingleton<IApprovalUserDirectory>(sp => sp.GetRequiredService<DemoIdentityProvider>());
// 演示用请求头识别用户；接入真实认证后删除这一行，默认从 Claims 读取
builder.Services.AddSingleton<IApprovalCurrentUser, DemoCurrentUser>();
builder.Services.AddSingleton<DemoWebhookReceiver>();
builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() =>
    new SocketsHttpHandler { Proxy = new LoopbackBypassProxy(HttpClient.DefaultProxy) }));

var approvalConfig = builder.Configuration.GetSection("Approval");
var baseUrl = approvalConfig["BaseUrl"] ?? "http://localhost:5199";
var approval = builder.Services
    .AddApprovalWorkflow<AppDbContext>(o =>
    {
        o.AllowInitiatorApprove = false;
        o.RequireBusinessHandler = true;
        // 模板变量 {appBaseUrl}、{链接}
        o.GlobalVariables["appBaseUrl"] = baseUrl;
        o.InstanceUrlFormat = baseUrl + "/approvals/{0}";
        // HTTP 动作只允许访问白名单主机
        o.Http.AllowedHosts.AddRange(approvalConfig.GetSection("HttpAllowedHosts").Get<string[]>() ?? []);
        approvalConfig.GetSection("Outbox").Bind(o.Outbox);
        o.TimeoutScanInterval = approvalConfig.GetValue("TimeoutScanInterval", o.TimeoutScanInterval);
    })
    .AddBusinessHandler<CertificateApprovalHandler>()
    // 表单审批：设置了“可发起”的流程（如 Flows/leave.json、expense.json）可在发起中心直接填写表单发起
    .AddFormApprovals()
    .AddActionHandler<CertificateExpiryCheckAction>()
    // 演示：Webhook 发到本服务的模拟接收端（/api/demo/webhook），可在“投递记录”页模拟失败观察重试
    .AddWebhookChannel(o =>
    {
        approvalConfig.GetSection("Webhook").Bind(o);
        if (string.IsNullOrEmpty(o.Url)) o.Url = baseUrl + "/api/demo/webhook/notify";
    });

// 企业微信 / 钉钉 / 邮件：在配置（建议用 user-secrets 或环境变量，勿提交密钥）中填写后启用
if (approvalConfig["WeCom:WebhookUrl"] is { Length: > 0 })
    approval.AddWeComRobotChannel(o => approvalConfig.GetSection("WeCom").Bind(o));
if (approvalConfig["DingTalk:WebhookUrl"] is { Length: > 0 })
    approval.AddDingTalkRobotChannel(o => approvalConfig.GetSection("DingTalk").Bind(o));
if (approvalConfig["Email:Host"] is { Length: > 0 })
    approval.AddEmailChannel(o => approvalConfig.GetSection("Email").Bind(o));

builder.Services.AddScoped<CertificateService>();

var app = builder.Build();

app.UseExceptionHandler();

await InitializeAsync(app);

// 前端构建产物（web/approval-ui → wwwroot），开发时也可用 Vite 开发服务器
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapApprovalEndpoints();
app.MapCertificateEndpoints();
app.MapGet("/api/demo/users", (DemoIdentityProvider identity) => identity.ListUsers()).WithTags("Demo");

// 演示用外部系统：接收 Webhook 通知与 HTTP 动作请求
var webhook = app.MapGroup("/api/demo/webhook").WithTags("Demo");
webhook.MapGet("/", (DemoWebhookReceiver r) => new { failNext = r.FailNext, received = r.List() });
webhook.MapPost("/fail-next", (int count, DemoWebhookReceiver r) =>
{
    r.SetFailNext(count);
    return Results.NoContent();
});
webhook.MapDelete("/", (DemoWebhookReceiver r) =>
{
    r.Clear();
    return Results.NoContent();
});
webhook.MapPost("/{**path}", async (string? path, HttpRequest request, DemoWebhookReceiver r) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return r.TryReceive(path ?? "", request.Headers["Idempotency-Key"].ToString(), body)
        ? Results.Ok(new { received = true })
        : Results.Problem("模拟的外部系统故障", statusCode: StatusCodes.Status503ServiceUnavailable);
});

// 前端路由回退到 index.html。默认回退会跳过“像文件名”的路径，
// 而流程标识含点（如 /designer/certificate.update、/start/hr.leave），需单独声明；未知 API 返回 404 而不是页面
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("/designer/{**key}", "index.html");
app.MapFallbackToFile("/start/{**key}", "index.html");
app.MapFallbackToFile("index.html");

app.Run();

// 演示用：建库并发布 Flows 目录下尚未发布的流程（文件名为流程标识，顶层 name 为流程名称）。
// 生产环境请使用 EF Core 迁移与流程管理界面
static async Task InitializeAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureSchemaUpToDateAsync(db);

    var definitions = scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>();
    foreach (var file in Directory.GetFiles(Path.Combine(app.Environment.ContentRootPath, "Flows"), "*.json"))
    {
        var key = Path.GetFileNameWithoutExtension(file);
        if (await db.Set<ApprovalDefinition>().AnyAsync(d => d.Key == key)) continue;

        var json = await File.ReadAllTextAsync(file);
        using var document = JsonDocument.Parse(json);
        var name = document.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } text ? text : key;
        var definition = await definitions.PublishAsync(key, name, json);
        app.Logger.LogInformation("已发布流程 {Key} v{Version}", definition.Key, definition.Version);
    }
}

// EnsureCreated 不会给已存在的库补新表；旧版本创建的演示库缺少新表 / 新列时给出明确提示
static async Task EnsureSchemaUpToDateAsync(AppDbContext db)
{
    try
    {
        await db.Set<ApprovalOutboxMessage>().AnyAsync();
        await db.Set<ApprovalCarbonCopy>().AnyAsync();
        await db.Set<ApprovalTask>().Select(t => new { t.NextRemindAt, t.Round }).FirstOrDefaultAsync();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex)
    {
        throw new InvalidOperationException(
            $"演示数据库结构已过期（{ex.Message}）。请删除连接字符串指向的 .db 文件后重新启动；正式项目请使用 EF Core 迁移。", ex);
    }
}
