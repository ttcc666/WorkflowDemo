using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
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

public sealed class TenantHttpDbContext(DbContextOptions<TenantHttpDbContext> options, IApprovalTenantContext tenant) : ApprovalDbContext(options, tenant)
{ protected override void OnModelCreating(ModelBuilder builder) => builder.ConfigureApproval(this); }

public sealed class TenantEndpointTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private readonly Dictionary<string, Guid> _instances = [];
    private Guid _file;
    private readonly Permissions _permissions = new();
    private sealed class Permissions : IApprovalPermissionProvider
    {
        public bool DenyTenantB { get; set; }
        public List<ApprovalPermissionRequest> Calls { get; } = [];
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        { Calls.Add(request); return Task.FromResult(!(DenyTenantB && request.Subject.TenantId == "b")); }
    }
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"name":"Start","type":"Start","next":"review"},
        "review":{"name":"Review","type":"Approval","mode":"Any","approvers":[{"type":"User","value":"admin"}],"next":"end"},
        "end":{"name":"End","type":"End"}}}
        """;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDbContext<TenantHttpDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddScoped<IApprovalTenantCatalog, Catalog>();
        builder.Services.AddApprovalWorkflow<TenantHttpDbContext>(o => { o.MultiTenant = true; o.EnableBackgroundServices = false; o.AllowInitiatorApprove = true; })
            .AddIdentityProvider<FakeUsers>().AddFormApprovals();
        builder.Services.AddApprovalAspNetCore();
        builder.Services.AddSingleton<IApprovalPermissionProvider>(_permissions);
        _app = builder.Build(); _app.UseExceptionHandler();
        // Test authentication middleware. Production tenant resolution reads only the authenticated claims.
        _app.Use(async (http, next) =>
        {
            if (http.Request.Headers.Authorization.ToString() is { Length: > 0 } token)
            {
                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "admin") };
                claims.AddRange(token.Split(',').Where(t => t != "missing").Select(t => new Claim("tenant_id", t)));
                http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-authentication"));
            }
            await next(http);
        });
        _app.MapApprovalEndpoints(); await _app.StartAsync(); _http = _app.GetTestClient();
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await _app.Services.GetRequiredService<IApprovalTenantScopeFactory>().CreateAsync(tenant);
            scope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Authorization = Approval.Tests.Infrastructure.FlowFixtureAuthorization.Instance;
            var db = scope.ServiceProvider.GetRequiredService<TenantHttpDbContext>(); await db.Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>().PublishAsync("shared", "Shared", Flow);
            var instance = await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().StartAsync(new("shared", "Doc", "same", "admin"));
            _instances[tenant] = instance.Id;
            if (tenant == "b")
            {
                _file = Guid.NewGuid();
                db.Add(new ApprovalFile { Id = _file, InstanceId = instance.Id, FileName = "secret.txt", ContentType = "text/plain", UploadedBy = "admin", Sha256 = "00", StorageKey = "not-read" });
                await db.SaveChangesAsync();
            }
        }
    }
    public async Task DisposeAsync() { _http.Dispose(); await _app.DisposeAsync(); await _connection.DisposeAsync(); }
    private async Task<HttpResponseMessage> Get(string url, string? tenant, string? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (tenant is not null) request.Headers.TryAddWithoutValidation("Authorization", tenant);
        if (header is not null) request.Headers.Add("X-Tenant-Id", header);
        return await _http.SendAsync(request);
    }
    [Theory]
    [InlineData(null)] [InlineData("missing")] [InlineData("a,b")]
    public async Task MissingOrAmbiguousTrustedTenant_ReturnsUnauthorized(string? tenant)
    { Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/approval/me", tenant, "a")).StatusCode); }
    [Fact]
    public async Task DisabledTenant_ReturnsForbidden()
    { Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/approval/me", "disabled")).StatusCode); }
    [Fact]
    public async Task HeaderCannotOverrideClaimsAndAdministratorCannotReadOtherTenant()
    {
        using var me = await Get("/api/approval/me", "a", "b");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("a", json.RootElement.GetProperty("tenantId").GetString());
        Assert.True(json.RootElement.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/approval/instances/{_instances["b"]}", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/approval/instances/{_instances["b"]}/definition", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/approval/files/{_file}", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get($"/api/approval/instances/{_instances["a"]}", "a")).StatusCode);
    }
    [Fact]
    public async Task ThirdPartyPermissions_ReevaluateTenantSwitch_AndNeverReceiveForeignResources()
    {
        _permissions.DenyTenantB = true;
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/approval/todo/count", "a")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/approval/todo/count", "b")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/approval/todo/count", "a")).StatusCode);
        Assert.Contains(_permissions.Calls, r => r.Subject.TenantId == "a" && r.Subject.ActorId == "admin");
        Assert.Contains(_permissions.Calls, r => r.Subject.TenantId == "b" && r.Subject.ActorId == "admin");
        _permissions.Calls.Clear();
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/approval/instances/{_instances["b"]}", "a")).StatusCode);
        Assert.Empty(_permissions.Calls);
    }
    [Fact]
    public async Task TenantSwitchWithSameUser_HasIndependentTodoAndDefinitions()
    {
        foreach (var tenant in new[] { "a", "b", "a" })
        {
            using var response = await Get("/api/approval/todo", tenant);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(_instances[tenant].ToString(), Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("instanceId").GetString());
            using var count = await Get("/api/approval/todo/count", tenant);
            using var value = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
            Assert.Equal(1, value.RootElement.GetProperty("count").GetInt32());
        }
    }
    private sealed class Catalog : IApprovalTenantCatalog
    { public Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalTenant>>([new("a"), new("b"), new("disabled", false)]); }
}
