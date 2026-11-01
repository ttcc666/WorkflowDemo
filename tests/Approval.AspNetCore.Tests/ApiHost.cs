using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.AspNetCore.Tests;

public sealed class ApiDbContext(DbContextOptions<ApiDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureApproval();
}

/// <summary>内存用户表，同时作为身份提供者与用户目录。</summary>
public sealed class FakeUsers : IApprovalIdentityProvider, IApprovalUserDirectory
{
    public Dictionary<string, string[]> Roles { get; } = new()
    {
        ["alice"] = [],
        ["bob"] = ["Reviewer"],
        ["admin"] = ["Admin"],
        ["hana"] = ["HR"]
    };

    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) =>
        Task.FromResult(Roles.TryGetValue(userId, out var r) && r.Contains(role));

    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Roles.Where(u => u.Value.Contains(role)).Select(u => u.Key).ToList());

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Roles.TryGetValue(userId, out var r) ? r : []);

    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApprovalUser>>(Roles.Keys
            .Where(id => string.IsNullOrEmpty(keyword) || id.Contains(keyword)).Take(take).Select(ToUser).ToList());

    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApprovalUser>>(Roles.Keys.Where(ids.Contains).Select(ToUser).ToList());

    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Roles.Values.SelectMany(r => r).Distinct().Order().ToList());

    private ApprovalUser ToUser(string id) => new(id, id.ToUpperInvariant(), Roles[id]);
}

/// <summary>测试用当前用户：请求头 X-Test-User。</summary>
public sealed class HeaderCurrentUser : IApprovalCurrentUser
{
    public const string Header = "X-Test-User";

    public string? GetUserId(HttpContext http) => http.Request.Headers[Header].ToString() is { Length: > 0 } id ? id : null;
}

public sealed class DocHandler : IApprovalBusinessHandler
{
    public const string Type = "Doc";
    public string BusinessType => Type;
    public Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct) => Task.CompletedTask;
    public Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>最小宿主：SQLite 内存库 + TestServer，不依赖示例项目。</summary>
public sealed class ApiHost : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    private ApiHost(WebApplication app, SqliteConnection connection, FakeUsers users)
    {
        _app = app;
        _connection = connection;
        Users = users;
        Client = app.GetTestClient();
    }

    public FakeUsers Users { get; }
    public HttpClient Client { get; }

    public static async Task<ApiHost> StartAsync(bool withDirectory = true, Action<ApprovalBuilder>? build = null,
        Action<ApprovalAspNetCoreOptions>? api = null, Action<ApprovalOptions>? workflow = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var users = new FakeUsers();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        builder.Services.AddDbContext<ApiDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddSingleton<IApprovalIdentityProvider>(users);
        if (withDirectory) builder.Services.AddSingleton<IApprovalUserDirectory>(users);
        builder.Services.AddSingleton<IApprovalCurrentUser, HeaderCurrentUser>();
        builder.Services.AddApprovalAspNetCore(api);
        var approval = builder.Services
            .AddApprovalWorkflow<ApiDbContext>(o =>
            {
                o.EnableBackgroundServices = false;
                workflow?.Invoke(o);
            })
            .AddBusinessHandler<DocHandler>();
        build?.Invoke(approval);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapApprovalEndpoints();
        await app.StartAsync();

        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.EnsureCreatedAsync();

        return new ApiHost(app, connection, users);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? user, object? body = null, string? rawJson = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (user is not null) request.Headers.Add(HeaderCurrentUser.Header, user);
        if (rawJson is not null) request.Content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return Client.SendAsync(request);
    }

    public async Task<JsonElement> GetJsonAsync(string url, string user)
    {
        var response = await SendAsync(HttpMethod.Get, url, user);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> PostJsonAsync(string url, string user, object? body)
    {
        var response = await SendAsync(HttpMethod.Post, url, user, body ?? new { });
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response.Content.Headers.ContentLength == 0 ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task PublishAsync(string key, string flowJson, string name = "测试流程")
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/approval/definitions/{key}?name={Uri.EscapeDataString(name)}", "admin",
            rawJson: flowJson);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>读取 ProblemDetails。</summary>
public static class ProblemExtensions
{
    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, int expectedStatus)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"期望 {expectedStatus}，实际 {(int)response.StatusCode}：{text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(text).RootElement;
    }
}
