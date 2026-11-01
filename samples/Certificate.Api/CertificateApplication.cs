using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.AspNetCore;
using Approval.Certificate;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Notifications;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using Microsoft.Data.Sqlite;

namespace Certificate.Api;

/// <summary>真实宿主与集成测试共用的服务注册、初始化和 HTTP 管线。</summary>
public static class CertificateApplication
{
    public static void AddCertificateApplication(this WebApplicationBuilder builder, Action<ApprovalBuilder>? configureApproval = null)
    {
        var certificateOptions = new CertificateOptions();
        builder.Configuration.GetSection("Certificate").Bind(certificateOptions);
        if (certificateOptions.MonitorInterval <= TimeSpan.Zero) throw new InvalidOperationException("运行检查间隔必须大于零");
        if (certificateOptions.DemoMode && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("演示身份仅允许在 Development 本地试点中启用");
        if (!certificateOptions.DemoMode && (!builder.Services.Any(d => d.ServiceType == typeof(IApprovalIdentityProvider))
            || !builder.Services.Any(d => d.ServiceType == typeof(IAuthenticationSchemeProvider))))
            throw new InvalidOperationException("非演示模式必须接入真实认证和 IApprovalIdentityProvider；本地试点请使用 Development 配置");
        builder.Services.AddSingleton(certificateOptions);
        var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
        if (connection.Mode != SqliteOpenMode.Memory && connection.DataSource != ":memory:")
            connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
        connection.ForeignKeys = true;
        if (!connection.ContainsKey("Default Timeout")) connection.DefaultTimeout = 10;
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection.ToString()));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

        // 审批接口（/api/approval/*）、ProblemDetails 异常映射、字符串枚举
        builder.Services.AddApprovalAspNetCore();
        builder.Services.AddExceptionHandler<CertificateDatabaseExceptionHandler>();
        builder.Services.AddAuthorization();

        // 接入审批框架：宿主只需提供 DbContext、用户角色体系、当前用户和业务回调
        if (certificateOptions.DemoMode)
        {
            builder.Services.AddSingleton<DemoIdentityProvider>();
            builder.Services.AddSingleton<IApprovalIdentityProvider>(sp => sp.GetRequiredService<DemoIdentityProvider>());
            builder.Services.AddSingleton<IApprovalContactProvider>(sp => sp.GetRequiredService<DemoIdentityProvider>());
            builder.Services.AddSingleton<IApprovalUserDirectory>(sp => sp.GetRequiredService<DemoIdentityProvider>());
            // 演示用请求头识别用户；接入真实认证后删除这一行，默认从 Claims 读取
            builder.Services.AddSingleton<IApprovalCurrentUser, DemoCurrentUser>();
        }
        builder.Services.AddSingleton<DemoWebhookReceiver>();
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() =>
            new SocketsHttpHandler { Proxy = new LoopbackBypassProxy(HttpClient.DefaultProxy) }));

        var approvalConfig = builder.Configuration.GetSection("Approval");
        var baseUrl = approvalConfig["BaseUrl"] ?? "http://localhost:5199";
        var approval = builder.Services
            .AddApprovalWorkflow<AppDbContext>(o =>
            {
                o.AllowInitiatorApprove = false;
                o.AllowSameUserMultipleNodes = false;
                o.DisplayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(certificateOptions.BusinessTimeZone);
                o.RequireBusinessHandler = true;
                o.EnableBackgroundServices = approvalConfig.GetValue("EnableBackgroundServices", true);
                // 模板变量 {appBaseUrl}、{链接}
                o.GlobalVariables["appBaseUrl"] = baseUrl;
                o.InstanceUrlFormat = baseUrl + "/approvals/{0}";
                // HTTP 动作只允许访问白名单主机
                o.Http.AllowedHosts.AddRange(approvalConfig.GetSection("HttpAllowedHosts").Get<string[]>() ?? []);
                approvalConfig.GetSection("Outbox").Bind(o.Outbox);
                // 附件：本地目录（相对于内容根目录，不在 wwwroot 下）、大小限制
                approvalConfig.GetSection("Files").Bind(o.Files);
                o.TimeoutScanInterval = approvalConfig.GetValue("TimeoutScanInterval", o.TimeoutScanInterval);
            })
            .AddCertificateModule()
            // 组织架构：审批人可选“部门负责人”“上级”“逐级上级”（演示数据见 appsettings.json 的 Departments 与 DemoUsers）
            // 表单审批：设置了“可发起”的流程（如 Flows/leave.json、expense.json、purchase.json）可在发起中心直接填写表单发起
            .AddFormApprovals()
            // 演示：Webhook 发到本服务的模拟接收端（/api/demo/webhook），可在“投递记录”页模拟失败观察重试
            .AddWebhookChannel(o =>
            {
                approvalConfig.GetSection("Webhook").Bind(o);
                if (string.IsNullOrEmpty(o.Url)) o.Url = baseUrl + "/api/demo/webhook/notify";
            });

        if (certificateOptions.DemoMode) approval.AddOrganizationProvider<DemoIdentityProvider>();
        configureApproval?.Invoke(approval);

        // 企业微信 / 钉钉 / 邮件：在配置（建议用 user-secrets 或环境变量，勿提交密钥）中填写后启用
        if (approvalConfig["WeCom:WebhookUrl"] is { Length: > 0 })
            approval.AddWeComRobotChannel(o => approvalConfig.GetSection("WeCom").Bind(o));
        if (approvalConfig["DingTalk:WebhookUrl"] is { Length: > 0 })
            approval.AddDingTalkRobotChannel(o => approvalConfig.GetSection("DingTalk").Bind(o));
        if (approvalConfig["Email:Host"] is { Length: > 0 })
            approval.AddEmailChannel(o => approvalConfig.GetSection("Email").Bind(o));

        if (approvalConfig.GetValue("EnableBackgroundServices", true))
            builder.Services.AddHostedService(sp => sp.GetRequiredService<CertificateMonitor>());
        builder.Services.Replace(ServiceDescriptor.Scoped<IApprovalAccessPolicy>(sp => sp.GetRequiredService<CertificateAccessPolicy>()));
    }

    public static async Task UseCertificateApplicationAsync(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
            {
                ["TraceId"] = context.TraceIdentifier,
                ["SubmissionId"] = Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) ? key : null
            });
            context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
            await next(context);
        });
        var demo = app.Services.GetRequiredService<CertificateOptions>().DemoMode;
        if (demo) app.Use(async (context, next) =>
        {
            if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { title = "本地试点仅允许回环地址访问" });
                return;
            }
            await next(context);
        });
        else
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        await InitializeAsync(app);

        // 前端构建产物（web/approval-ui → wwwroot），开发时也可用 Vite 开发服务器
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapApprovalEndpoints();
        app.MapCertificateEndpoints();
        app.MapCertificateOperations();
        if (demo)
        {
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
        }

        // 前端路由回退到 index.html。默认回退会跳过“像文件名”的路径，
        // 而流程标识含点（如 /designer/certificate.update、/start/hr.leave），需单独声明；未知 API 返回 404 而不是页面
        app.Map("/api/{**rest}", () => Results.NotFound());
        app.MapFallbackToFile("/designer/{**key}", "index.html");
        app.MapFallbackToFile("/start/{**key}", "index.html");
        app.MapFallbackToFile("index.html");
    }

    private static async Task InitializeAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CertificateDatabase>().InitializeAsync();
        await PublishFlowsAsync(app, replaceExisting: false);
    }

    /// <summary>启动仅补缺失流程；发布更新必须显式调用，进行中实例继续使用原版本。</summary>
    public static async Task<int> PublishFlowsAsync(WebApplication app, bool replaceExisting, CancellationToken ct = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var definitions = scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>();
        var count = 0;
        var defaults = CertificateFlowTemplates.ReadDefaults();
        var files = Directory.GetFiles(Path.Combine(app.Environment.ContentRootPath, "Flows"), "*.json")
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file), StringComparer.Ordinal);
        foreach (var key in defaults.Keys.Concat(files.Keys).Distinct(StringComparer.Ordinal).Order())
        {
            var latest = await definitions.GetLatestAsync(key, ct);
            if (latest is not null && !replaceExisting) continue;
            var json = files.TryGetValue(key, out var file) ? await File.ReadAllTextAsync(file, ct) : defaults[key];
            using var document = JsonDocument.Parse(json);
            var name = document.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } text ? text : key;
            var flow = Approval.Core.Definitions.FlowDefinition.Parse(json);
            if (latest is not null && latest.Name == name
                && Approval.Core.Definitions.FlowDefinition.Parse(latest.DefinitionJson).ToJson() == flow.ToJson()) continue;
            try
            {
                var published = await definitions.PublishAsync(key, name, flow, ct);
                count++;
                app.Logger.LogInformation("已发布流程 {Key} v{Version}", published.Key, published.Version);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
            {
                db.ChangeTracker.Clear();
                var winner = await definitions.GetLatestAsync(key, ct);
                if (winner is null || winner.Name != name
                    || Approval.Core.Definitions.FlowDefinition.Parse(winner.DefinitionJson).ToJson() != flow.ToJson()) throw;
            }
        }
        return count;
    }
}

