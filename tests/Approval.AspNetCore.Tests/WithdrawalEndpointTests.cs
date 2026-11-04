using System.Net;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class WithdrawalEndpointTests
{
    private const string Flow = """
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "nodes": {
        "start": { "name": "提交", "type": "Start", "next": "first" },
        "first": { "name": "一审", "type": "Approval", "next": "second",
          "approvers": [{ "type": "Role", "value": "Reviewer" }] },
        "second": { "name": "二审", "type": "Approval", "next": "end",
          "approvers": [{ "type": "Role", "value": "Reviewer" }] },
        "end": { "name": "结束", "type": "End" }
      }
    }
    """;

    [Theory]
    [InlineData(false, HttpStatusCode.BadRequest, "Running")]
    [InlineData(true, HttpStatusCode.NoContent, "Withdrawn")]
    public async Task Withdrawal_capability_matches_option_and_command_after_approval(bool enabled, HttpStatusCode responseStatus,
        string instanceStatus)
    {
        await using var host = await ApiHost.StartAsync(workflow: o => o.AllowWithdrawAfterApproval = enabled);
        var id = await StartAsync(host);
        Assert.True((await host.GetJsonAsync($"/api/approval/instances/{id}", "alice")).GetProperty("canWithdraw").GetBoolean());
        await ApproveNextAsync(host);

        var detail = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");
        Assert.Equal(enabled, detail.GetProperty("canWithdraw").GetBoolean());
        var response = await host.SendAsync(HttpMethod.Post, $"/api/approval/instances/{id}/withdraw", "alice", new { });

        Assert.Equal(responseStatus, response.StatusCode);
        var result = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");
        Assert.Equal(instanceStatus, result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("canWithdraw").GetBoolean());
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task Only_initiator_receives_withdrawal_capability(string userId)
    {
        await using var host = await ApiHost.StartAsync(workflow: o => o.AllowWithdrawAfterApproval = true);
        var id = await StartAsync(host);

        var detail = await host.GetJsonAsync($"/api/approval/instances/{id}", userId);
        var problem = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/instances/{id}/withdraw", userId, new { }))
            .ProblemAsync(403);

        Assert.False(detail.GetProperty("canWithdraw").GetBoolean());
        Assert.Equal("Forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Completed_instance_cannot_be_withdrawn()
    {
        await using var host = await ApiHost.StartAsync(workflow: o => o.AllowWithdrawAfterApproval = true);
        var id = await StartAsync(host);
        await ApproveNextAsync(host);
        await ApproveNextAsync(host);

        var detail = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canWithdraw").GetBoolean());
        var problem = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/instances/{id}/withdraw", "alice", new { }))
            .ProblemAsync(400);
        Assert.Equal("流程已结束，无法撤回", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Returned_request_can_be_withdrawn_even_after_approval()
    {
        await using var host = await ApiHost.StartAsync();
        var id = await StartAsync(host);
        await ApproveNextAsync(host);
        var taskId = await PendingTaskAsync(host);
        await host.RunAsync(async sp =>
        {
            await sp.GetRequiredService<ApprovalEngine>().ReturnAsync(taskId, "bob", "补充材料", "start");
            return true;
        });

        var detail = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");
        Assert.True(detail.GetProperty("canWithdraw").GetBoolean());
        await host.PostJsonAsync($"/api/approval/instances/{id}/withdraw", "alice", new { });
        Assert.Equal("Withdrawn", (await host.GetJsonAsync($"/api/approval/instances/{id}", "alice")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Withdrawal_rechecks_permission_after_a_stale_capability_response()
    {
        await using var host = await ApiHost.StartAsync();
        var id = await StartAsync(host);
        Assert.True((await host.GetJsonAsync($"/api/approval/instances/{id}", "alice")).GetProperty("canWithdraw").GetBoolean());
        await ApproveNextAsync(host);

        var problem = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/instances/{id}/withdraw", "alice", new { }))
            .ProblemAsync(400);

        Assert.Equal("已有人审批通过，无法撤回", problem.GetProperty("title").GetString());
        Assert.Equal("Running", (await host.GetJsonAsync($"/api/approval/instances/{id}", "alice")).GetProperty("status").GetString());
    }

    private static async Task<Guid> StartAsync(ApiHost host)
    {
        await host.PublishAsync("withdraw", Flow);
        return (await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("withdraw", DocHandler.Type, "D1", "alice")))).Id;
    }

    private static async Task<Guid> PendingTaskAsync(ApiHost host) =>
        Assert.Single((await host.GetJsonAsync("/api/approval/todo", "bob")).GetProperty("items").EnumerateArray())
            .GetProperty("taskId").GetGuid();

    private static async Task ApproveNextAsync(ApiHost host) =>
        await host.PostJsonAsync($"/api/approval/tasks/{await PendingTaskAsync(host)}/approve", "bob", new { });
}
