using System.Net;
using Approval.Core.Abstractions;
using Approval.ReferenceHost;
using Approval.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using System.Security.Claims;
using Approval.Core;
using Microsoft.AspNetCore.Http;

namespace Approval.AspNetCore.Tests;

public sealed class ReferenceHostTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "approval-reference-" + Guid.NewGuid().ToString("N") + ".db");
    private Dictionary<string, string?> Config => ReferenceHostScenario.Configuration("Sqlite", "Data Source=" + _path);
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
        return Task.CompletedTask;
    }
    [Fact]
    public async Task ReferenceHost_SingleHostApprovalRevocationAndOriginalVersionReplay()
    {
        await using var host = await ReferenceHostScenario.StartAsync(Config);
        await host.SingleHostWorkflow();
    }
    [Fact]
    public async Task ReferenceHost_TwentyIdenticalHttpRequestsAcrossTwoHostsCommitOnce()
    {
        var config = Config;
        await using var first = await ReferenceHostScenario.StartAsync(config);
        await using var second = await ReferenceHostScenario.StartAsync(config);
        await first.TwentyRequestsAcross(second);
    }
    [Fact]
    public async Task ReferenceHost_BackgroundScopeRecoversAfterFailedBatch()
    {
        await using var host = await ReferenceHostScenario.StartAsync(Config);
        await host.BackgroundIsolation();
    }
    [Fact]
    public async Task ReferenceHost_AuthenticationMembershipAndPermissionConsoleFailClosed()
    {
        await using var host = await ReferenceHostScenario.StartAsync(Config);
        using var anonymous = await host.Client.GetAsync("/api/approval/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var guessed = await host.Send(HttpMethod.Get, "/api/approval/me", "unknown");
        Assert.Equal(HttpStatusCode.Unauthorized, guessed.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/approval/me");
        request.Headers.Authorization = new("Bearer", "reference-test-alpha-alice-" + new string('x', 32));
        request.Headers.Add("X-Tenant-Id", "beta");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("alice", (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(response.Content)).GetProperty("userId").GetString());
        using var nonAdmin = await host.Send(HttpMethod.Put, "/demo/grants/bob/approval.task.approve", body: new { allowed = false });
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
        using var foreignUser = await host.Send(HttpMethod.Put, "/demo/grants/unknown/approval.task.approve", user: "admin", body: new { allowed = false });
        Assert.Equal(HttpStatusCode.NotFound, foreignUser.StatusCode);
        using var systemGrant = await host.Send(HttpMethod.Put, "/demo/grants/bob/approval.system.outbox-dispatch", user: "admin", body: new { allowed = true });
        Assert.Equal(HttpStatusCode.BadRequest, systemGrant.StatusCode);
        await host.Publish("alpha");
        using var missingKey = await host.Send(HttpMethod.Post, "/api/approval/forms/shared/start", body: new { formData = new { amount = 12 } });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
    }
    [Fact]
    public void ReferenceHost_ProductionRejectsDemoAndInvalidJwtConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => ReferenceApplication.Build(["--environment", "Production"], b => ReferenceHostScenario.ConfigureBuilder(b, Config)));
        var config = Config; config["Authentication:Mode"] = "Jwt"; config["Authentication:Authority"] = "http://issuer.invalid"; config["Authentication:Audience"] = "approval";
        Assert.Throws<InvalidOperationException>(() => ReferenceApplication.Build(["--environment", "Production"], b => ReferenceHostScenario.ConfigureBuilder(b, config)));
        config["Authentication:Mode"] = "Invalid";
        Assert.Throws<InvalidOperationException>(() => ReferenceApplication.Build(["--environment", "Production"], b => ReferenceHostScenario.ConfigureBuilder(b, config)));
    }
    [Fact]
    public async Task ReferenceHost_SystemPermissionsRequireKnownWorkerAndAllowedCapability()
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ReferenceHostConfiguration.json")).AddInMemoryCollection(Config).Build();
        var provider = new DirectoryPermissionProvider(new ReferenceDirectory(config));
        Assert.True(await provider.AuthorizeAsync(new(new("approval-background", true), ApprovalPermissions.OutboxDispatch, new()), default));
        Assert.False(await provider.AuthorizeAsync(new(new("approval-background", true), ApprovalPermissions.DefinitionPublish, new()), default));
        Assert.False(await provider.AuthorizeAsync(new(new("unknown-worker", true), ApprovalPermissions.OutboxDispatch, new()), default));
        Assert.False(await provider.AuthorizeAsync(new(new("admin", false), ApprovalPermissions.OutboxDispatch, new()), default));
    }
    [Fact]
    public void ReferenceHost_DirectoryRejectsAmbiguousMissingAndNonmemberClaims()
    {
        var settings = Config;
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ReferenceHostConfiguration.json")).AddInMemoryCollection(settings).Build();
        var directory = new ReferenceDirectory(config);
        var http = new DefaultHttpContext();
        var resolver = new DirectoryContextResolver(new HttpContextAccessor { HttpContext = http }, directory);
        foreach (var users in new[] { new[] { "alice", "bob" }, Array.Empty<string>(), new[] { "unknown" } })
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(users.Select(u => new Claim("sub", u)), "verified"));
            var expected = users.Length == 1 ? ApprovalErrorCode.Forbidden : ApprovalErrorCode.Unauthorized;
            Assert.Equal(expected, Assert.Throws<ApprovalException>(() => resolver.Resolve()).Code);
        }
    }
    [Fact]
    public void ReferenceHost_DemoTokensRejectShortDuplicateAndNonmemberCredentials()
    {
        foreach (var invalid in new[] { "short", "duplicate", "nonmember" })
        {
            var settings = Config;
            if (invalid == "short") settings["Authentication:DemoIdentities:0:Token"] = new string('x', 31);
            if (invalid == "duplicate") settings["Authentication:DemoIdentities:1:Token"] = settings["Authentication:DemoIdentities:0:Token"];
            if (invalid == "nonmember") settings["Authentication:DemoIdentities:0:UserId"] = "unknown";
            var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ReferenceHostConfiguration.json")).AddInMemoryCollection(settings).Build();
            Assert.Throws<InvalidOperationException>(() => new ReferenceDirectory(config).ValidateDemoIdentities());
        }
    }
}
