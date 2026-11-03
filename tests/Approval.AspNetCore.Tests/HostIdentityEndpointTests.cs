using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class HostIdentityEndpointTests
{
    [Fact]
    public async Task Request_body_cannot_impersonate_another_tasks_assignee()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("identity", """
            {"startNodeId":"s","nodes":{"s":{"type":"Start","next":"a"},
            "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"e"},"e":{"type":"End"}}}
            """);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("identity", DocHandler.Type, "one", "alice")));
        var task = instance.Tasks.Single();
        var denied = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task.Id}/approve", "alice", new { userId = "bob", operatorId = "bob", comment = "spoofed" })).ProblemAsync(403);
        Assert.Equal("Forbidden", denied.GetProperty("code").GetString());
        var pending = await host.GetJsonAsync("/api/approval/todo", "bob");
        Assert.Equal(1, pending.GetProperty("total").GetInt32());
        var anonymous = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task.Id}/approve", null, new { userId = "bob" })).ProblemAsync(401);
        Assert.Equal("Unauthorized", anonymous.GetProperty("code").GetString());
    }
}
