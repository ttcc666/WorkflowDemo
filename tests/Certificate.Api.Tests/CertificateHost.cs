using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Persistence;
using Approval.Forms;
using Microsoft.EntityFrameworkCore;
using Certificate.Api;
using Certificate.Api.Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Certificate.Api.Tests;

public sealed class CertificateHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly bool _ownsDirectory;
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "pilot.db");
    public HttpClient Client { get; }
    public FakeTimeProvider Clock { get; }
    public RecordingHttp Http { get; }

    private CertificateHost(WebApplication app, string directory, bool ownsDirectory, FakeTimeProvider clock, RecordingHttp http)
    {
        _app = app;
        DirectoryPath = directory;
        _ownsDirectory = ownsDirectory;
        Clock = clock;
        Http = http;
        Client = app.GetTestClient();
    }

    public static async Task<CertificateHost> StartAsync(string? directory = null,
        Action<IServiceCollection>? configure = null, Dictionary<string, string?>? configuration = null)
    {
        var ownsDirectory = directory is null;
        directory ??= Path.Combine(Path.GetTempPath(), "workflow-certificate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));
        var http = new RecordingHttp();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(directory, "pilot.db")};Pooling=False;Default Timeout=10",
            ["Certificate:DemoMode"] = "true",
            ["Approval:EnableBackgroundServices"] = "false",
            ["Approval:Files:RootPath"] = Path.Combine(directory, "attachments"),
            ["Approval:BaseUrl"] = "http://localhost",
            ["Approval:Webhook:Url"] = "http://localhost/api/demo/webhook/notify",
            ["Approval:WeCom:WebhookUrl"] = "",
            ["Approval:DingTalk:WebhookUrl"] = "",
            ["Approval:Email:Host"] = ""
        });
        if (configuration is not null) builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddCertificateApplication();
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => http));
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue("X-Test-Remote-IP", out var ip)
                ? IPAddress.Parse(ip.ToString()) : IPAddress.Loopback;
            return next(context);
        });
        try
        {
            await app.UseCertificateApplicationAsync();
            await app.StartAsync();
            return new CertificateHost(app, directory, ownsDirectory, clock, http);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? user = "alice", object? body = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (user is not null) request.Headers.Add("X-User-Id", user);
        if (method != HttpMethod.Get) request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }

    public async Task<JsonElement> GetAsync(string path, string user = "alice")
    {
        using var response = await SendAsync(HttpMethod.Get, path, user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<Guid> CreateAsync(CertificateData? data = null, string user = "alice", string? key = null)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/certificates", user, data ?? ValidData(), key);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"{response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("requestId").GetGuid();
    }

    public async Task<Guid> InstanceIdAsync(Guid requestId) =>
        (await GetAsync($"/api/certificate-requests/{requestId}")).GetProperty("approval").GetProperty("id").GetGuid();

    public async Task<Guid> TaskAsync(Guid instanceId, string user)
    {
        var result = await GetAsync($"/api/approval/instances/{instanceId}/my-task", user);
        return result.GetProperty("task").GetProperty("id").GetGuid();
    }

    public async Task ApproveAsync(Guid instanceId, string user)
    {
        var task = await TaskAsync(instanceId, user);
        using var response = await SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", user, new { comment = "审核通过" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task<int> ProcessDueAsync()
    {
        await using var scope = await _app.Services.GetRequiredService<Approval.Core.Abstractions.IApprovalScopeFactory>().CreateAsync();
        return await scope.ServiceProvider.GetRequiredService<Approval.Core.Engine.ApprovalOutboxService>().ProcessDueAsync();
    }

    public Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action) => RunAsync(sp => action(sp.GetRequiredService<AppDbContext>()));

    public async Task<T> SeedStorageAsync<T>(Func<DbContext, Task<T>> seed)
    {
        await using var db = new CertificateStorageContext(DatabasePath);
        return await seed(db);
    }

    public async Task<Guid> RegisterAsync(CertificateData? data = null)
    {
        var request = await CreateAsync(data);
        var instance = await InstanceIdAsync(request);
        await CompleteAsync(instance);
        return (await GetAsync($"/api/certificate-requests/{request}")).GetProperty("request").GetProperty("certificateId").GetGuid();
    }

    public async Task CompleteAsync(Guid instance)
    {
        await ApproveAsync(instance, "dave");
        await ApproveAsync(instance, "quinn");
        await ApproveAsync(instance, "leo");
    }

    public Task ConfigureAsync(string key, string? value) => RunAsync(sp =>
    {
        sp.GetRequiredService<IConfiguration>()[key] = value;
        return Task.FromResult(true);
    });

    public Task SetRolesAsync(string userId, params string[] roles) => SetArrayAsync("DemoUsers", userId, "Roles", roles);
    public Task SetLeadersAsync(string department, params string[] users) => SetArrayAsync("Departments", department, "Leaders", users);
    public Task<int> PublishFilesAsync(bool replace) => CertificateApplication.PublishFlowsAsync(_app, replace);

    private Task SetArrayAsync(string section, string id, string property, string[] values) => RunAsync(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var item = config.GetSection(section).GetChildren().Single(s => s["Id"] == id);
        var count = Math.Max(values.Length, item.GetSection(property).GetChildren().Count());
        for (var i = 0; i < count; i++) config[$"{item.Path}:{property}:{i}"] = i < values.Length ? values[i] : "__removed__";
        return Task.FromResult(true);
    });

    public static CertificateData ValidData(string number = "REG-2026-001") =>
        new(number, "注册证测试资料", "示例企业", "Normal", new DateOnly(2026, 1, 1), new DateOnly(2029, 1, 1));

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (_ownsDirectory)
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "workflow-certificate-tests")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(DirectoryPath);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("非法测试清理路径");
            Directory.Delete(target, recursive: true);
        }
    }
}

// Only corrupt/legacy storage setup uses this context. All application requests use AppDbContext and UseApproval.
internal sealed class CertificateStorageContext(string path) : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite($"Data Source={path};Pooling=False");
    protected override void OnModelCreating(ModelBuilder model) => model.ConfigureApproval().ConfigureApprovalForms();
}

public sealed class RecordingHttp : HttpMessageHandler
{
    private int _failures;
    public int Failures { set => _failures = value; }
    public ConcurrentQueue<(string? Key, string? Body)> Calls { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Enqueue((request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        return new HttpResponseMessage(Interlocked.Decrement(ref _failures) >= 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        };
    }
}
