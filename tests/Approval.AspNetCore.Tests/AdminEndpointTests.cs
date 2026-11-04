using System.Text.Json;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

/// <summary>管理接口：仅管理员；终止、改派、离职交接、监控列表、统计。</summary>
public sealed class AdminEndpointTests
{
    private const string Flow = """
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any", "approvers": [{ "type": "User", "value": "bob" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static async Task<(ApiHost Host, Guid InstanceId)> StartAsync(string businessKey = "D-1")
    {
        var host = await ApiHost.StartAsync();
        await host.PublishAsync("doc", Flow, name: "文档审批");
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, businessKey, "alice") { Title = $"文档 {businessKey}" }));
        return (host, instance.Id);
    }

    private static async Task<Guid> BobTaskAsync(ApiHost host) =>
        (await host.GetJsonAsync("/api/approval/todo", "bob")).GetProperty("items")[0].GetProperty("taskId").GetGuid();

    [Fact]
    public async Task 非管理员调用管理接口返回403()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;
        var taskId = await BobTaskAsync(host);

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, "/api/approval/admin/instances", null),
                     (HttpMethod.Get, "/api/approval/admin/statistics", null),
                     (HttpMethod.Get, "/api/approval/admin/users/bob/tasks", null),
                     (HttpMethod.Post, $"/api/approval/admin/instances/{id}/terminate", new { reason = "x" }),
                     (HttpMethod.Post, $"/api/approval/admin/tasks/{taskId}/reassign", new { toUserId = "hana" }),
                     (HttpMethod.Post, "/api/approval/admin/users/bob/reassign", new { toUserId = "hana" })
                 })
        {
            var problem = await (await host.SendAsync(method, url, "bob", body)).ProblemAsync(403);
            Assert.Equal("Forbidden", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task 管理员终止流程()
    {
        var (host, id) = await StartAsync();
        await using var _ = host;

        var missingReason = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/instances/{id}/terminate", "admin", new { }))
            .ProblemAsync(400);
        await host.PostJsonAsync($"/api/approval/admin/instances/{id}/terminate", "admin", new { reason = "业务取消" });

        Assert.Contains("原因", missingReason.GetProperty("title").GetString());
        var detail = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");
        Assert.Equal("Terminated", detail.GetProperty("status").GetString());
        Assert.Contains(detail.GetProperty("logs").EnumerateArray(), l => l.GetProperty("action").GetString() == "Terminate");
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/todo/count", "bob")).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task 管理员改派与离职交接()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, "D-2", "alice") { Title = "文档 D-2" }));

        var first = await BobTaskAsync(host);
        await host.PostJsonAsync($"/api/approval/admin/tasks/{first}/reassign", "admin", new { toUserId = "hana", reason = "休假" });
        var preview = await host.GetJsonAsync("/api/approval/admin/users/bob/tasks", "admin");
        var result = await host.PostJsonAsync("/api/approval/admin/users/bob/reassign", "admin", new { toUserId = "hana" });

        Assert.Equal(1, preview.GetProperty("total").GetInt32());
        Assert.Equal(1, result.GetProperty("count").GetInt32());
        Assert.Empty(result.GetProperty("skipped").EnumerateArray());
        Assert.Equal(2, (await host.GetJsonAsync("/api/approval/todo/count", "hana")).GetProperty("count").GetInt32());
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/todo/count", "bob")).GetProperty("count").GetInt32());
        // 改派给发起人：不允许自审
        var hanaTask = (await host.GetJsonAsync("/api/approval/todo", "hana")).GetProperty("items")[0].GetProperty("taskId").GetGuid();
        await (await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/tasks/{hanaTask}/reassign", "admin", new { toUserId = "alice" }))
            .ProblemAsync(400);
    }

    [Fact]
    public async Task 监控列表与统计()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var instances = await host.GetJsonAsync("/api/approval/admin/instances?initiatorId=alice&status=Running", "admin");
        var stats = await host.GetJsonAsync("/api/approval/admin/statistics?definitionKey=doc", "admin");

        Assert.Equal(1, instances.GetProperty("total").GetInt32());
        Assert.Equal(1, stats.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("approvalRate").ValueKind);
        Assert.Equal("bob", stats.GetProperty("backlog")[0].GetProperty("assigneeValue").GetString());
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/admin/statistics?from=2026-01-01T00:00:00Z&to=2027-06-01T00:00:00Z", "admin"))
            .ProblemAsync(400);
    }
}
