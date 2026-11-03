using Approval.Forms;
using System.Net;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class PermissionEndpointTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","mode":"Any","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;
    private sealed class Provider : IApprovalPermissionProvider
    {
        public Func<ApprovalPermissionRequest, CancellationToken, Task<bool>> Rule { get; set; } = (_, _) => Task.FromResult(true);
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Rule(request, ct);
    }

    [Fact]
    public async Task ExplicitPolicy_CanDesignAndValidateButCannotPublish()
    {
        await using var host = await ApiHost.StartAsync(api: o =>
        {
            foreach (var permission in new[] { ApprovalPermissions.DefinitionDesign, ApprovalPermissions.DefinitionValidate, ApprovalPermissions.DefinitionReadSensitive })
                o.PermissionPolicies[permission] = "designer";
        }, build: b => b.Services.AddAuthorization(o => o.AddPolicy("designer", p => p.RequireAssertion(c =>
            c.Resource is ApprovalPermissionRequest { Subject.ActorId: "bob" }))));
        var me = await host.GetJsonAsync("/api/approval/me", "bob");
        Assert.True(me.GetProperty("capabilities").GetProperty(ApprovalPermissions.DefinitionDesign).GetBoolean());
        Assert.False(me.GetProperty("capabilities").GetProperty(ApprovalPermissions.DefinitionPublish).GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/validate", "bob", rawJson: Flow)).StatusCode);
        await (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/doc", "bob", rawJson: Flow)).ProblemAsync(403);
        Assert.Empty((await host.GetJsonAsync("/api/approval/definitions", "bob")).EnumerateArray());
    }

    [Fact]
    public async Task DefinitionList_ExcludesDefinitionsDeniedByResourcePermission_AndReflectsRevocation()
    {
        var provider = new Provider();
        await using var host = await ApiHost.StartAsync(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(provider));
        await host.PublishAsync("visible", Flow);
        await host.PublishAsync("hidden", Flow.Replace("Review", "Confidential review"));

        provider.Rule = (request, _) => Task.FromResult(
            request.Permission != ApprovalPermissions.DefinitionReadSensitive || request.Resource.DefinitionKey != "hidden");
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/definitions/hidden", "bob")).ProblemAsync(403);
        var definitions = await host.GetJsonAsync("/api/approval/definitions", "bob");
        var visible = Assert.Single(definitions.EnumerateArray());
        Assert.Equal("visible", visible.GetProperty("key").GetString());
        Assert.Contains("Review", visible.GetProperty("definitionJson").GetString());

        provider.Rule = (_, _) => Task.FromResult(true);
        Assert.Equal(2, (await host.GetJsonAsync("/api/approval/definitions", "bob")).GetArrayLength());
        provider.Rule = (request, _) => Task.FromResult(
            request.Permission != ApprovalPermissions.DefinitionReadSensitive || string.IsNullOrEmpty(request.Resource.DefinitionKey));
        Assert.Empty((await host.GetJsonAsync("/api/approval/definitions", "bob")).EnumerateArray());
    }

    [Fact]
    public async Task DefinitionVersions_ExcludeDeniedVersionIds_AndPermissionFailureReturns503()
    {
        var provider = new Provider();
        await using var host = await ApiHost.StartAsync(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(provider));
        await host.PublishAsync("doc", Flow);
        await host.PublishAsync("doc", Flow.Replace("Review", "Updated review"));
        var original = await host.GetJsonAsync("/api/approval/definitions/doc/versions", "bob");
        var deniedId = original.EnumerateArray().Single(d => d.GetProperty("version").GetInt32() == 1).GetProperty("id").GetGuid();

        provider.Rule = (request, _) => Task.FromResult(
            request.Permission != ApprovalPermissions.DefinitionReadSensitive || request.Resource.DefinitionId != deniedId);
        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/definitions/by-id/{deniedId}", "bob")).ProblemAsync(403);
        var versions = await host.GetJsonAsync("/api/approval/definitions/doc/versions", "bob");
        Assert.Equal(2, Assert.Single(versions.EnumerateArray()).GetProperty("version").GetInt32());

        provider.Rule = (request, _) => request.Permission == ApprovalPermissions.DefinitionReadSensitive && request.Resource.DefinitionId == deniedId
            ? Task.FromException<bool>(new HttpRequestException("permissions offline")) : Task.FromResult(true);
        using var failed = await host.SendAsync(HttpMethod.Get, "/api/approval/definitions/doc/versions", "bob");
        Assert.Equal("AuthorizationUnavailable", (await failed.ProblemAsync(503)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ResourceCapabilitiesAndDirectHttpCalls_ReflectRevocation()
    {
        var provider = new Provider();
        await using var host = await ApiHost.StartAsync(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(provider));
        await host.PublishAsync("doc", Flow);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", DocHandler.Type, "one", "alice")));
        var task = Assert.Single(instance.Tasks);
        var current = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}/my-task", "bob");
        Assert.True(current.GetProperty("capabilities").GetProperty(ApprovalPermissions.TaskApprove).GetBoolean());
        provider.Rule = (r, _) => Task.FromResult(r.Permission != ApprovalPermissions.TaskApprove && r.Permission != ApprovalPermissions.OutboxRetry);
        current = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}/my-task", "bob");
        Assert.False(current.GetProperty("capabilities").GetProperty(ApprovalPermissions.TaskApprove).GetBoolean());
        await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task.Id}/approve", "bob", new { })).ProblemAsync(403);
        var me = await host.GetJsonAsync("/api/approval/me", "bob");
        Assert.True(me.GetProperty("capabilities").GetProperty(ApprovalPermissions.OutboxRead).GetBoolean());
        Assert.False(me.GetProperty("capabilities").GetProperty(ApprovalPermissions.OutboxRetry).GetBoolean());
        var detail = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}", "bob");
        Assert.Equal("Pending", Assert.Single(detail.GetProperty("tasks").EnumerateArray()).GetProperty("status").GetString());
        Assert.True(detail.GetProperty("capabilities").GetProperty(ApprovalPermissions.InstanceTerminate).GetBoolean());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnavailablePermissionService_Returns503WithoutPublishing(bool timeout)
    {
        var provider = new Provider { Rule = (_, ct) => timeout ? new TaskCompletionSource<bool>().Task.WaitAsync(ct) : Task.FromException<bool>(new HttpRequestException("offline")) };
        await using var host = await ApiHost.StartAsync(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(provider), workflow: o => o.PermissionTimeout = TimeSpan.FromMilliseconds(150));
        var problem = await (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/doc", "admin", rawJson: Flow)).ProblemAsync(503);
        Assert.Equal("AuthorizationUnavailable", problem.GetProperty("code").GetString());
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalDefinition>().CountAsync()));
    }

    [Fact]
    public async Task EveryMappedEndpointHasKnownPermission_AndNewUnclassifiedRouteFailsStartup()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddApprovalForms());
        Assert.True(host.Endpoints.Count >= 40);
        foreach (var endpoint in host.Endpoints)
        {
            var metadata = Assert.IsType<ApprovalPermissionMetadata>(endpoint.Metadata.GetMetadata<ApprovalPermissionMetadata>());
            Assert.NotEmpty(metadata.Permissions);
            Assert.All(metadata.Permissions, permission => Assert.Contains(permission, ApprovalPermissions.All));
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiHost.StartAsync(routes: g => g.MapGet("/forgotten", () => "oops")));
        Assert.Contains("GET /forgotten", failure.Message);
    }
}
