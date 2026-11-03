using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>管理功能：终止流程、改派、离职交接、统计。</summary>
public sealed class AdminTests : IAsyncLifetime
{
    private const string ParallelFlow = """
    {
      "startNodeId": "start",
      "nodes": {
        "start": { "name": "提交", "type": "Start", "next": "fork" },
        "fork":  { "name": "会审", "type": "Parallel", "branches": [ { "priority": 1, "next": "a" }, { "priority": 2, "next": "b" } ] },
        "a":     { "name": "A 审核", "type": "Approval", "approvers": [{ "type": "User", "value": "u1" }], "next": "join" },
        "b":     { "name": "B 审核", "type": "Approval", "approvers": [{ "type": "User", "value": "u2" }], "next": "join" },
        "join":  { "name": "合并", "type": "Join", "next": "end" },
        "end":   { "name": "结束", "type": "End" }
      }
    }
    """;

    /// <summary>单个审批节点，60 分钟时限。</summary>
    private static string TimedFlow(string approverType, string value, string mode = "Any") => $$"""
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "{{mode}}", "approvers": [{ "type": "{{approverType}}", "value": "{{value}}" }],
                    "next": "end", "timeout": { "minutes": 60 } },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private readonly TestHost _host = new();

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task RunEngineAsync(Func<ApprovalEngine, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApprovalEngine>()));

    private static bool IsOpen(ApprovalTask t) =>
        t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended;

    // ───────────── 终止 ─────────────

    [Fact]
    public async Task Terminate_closes_parallel_branches_and_calls_rejected_handler_by_default()
    {
        await _host.PublishAsync("p", ParallelFlow);
        var instance = await _host.StartAsync("p", "T1", "alice");
        await _host.ApproveMyTaskAsync("u1");

        await RunEngineAsync(e => e.TerminateAsync(instance.Id, "admin", "业务取消"));

        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Terminated, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        Assert.DoesNotContain(saved.Tasks, IsOpen);
        Assert.Null(saved.StateJson);
        Assert.Contains(("Rejected", "T1"), _host.Handler.Calls);   // 默认 OnTerminatedAsync → OnRejectedAsync
        var log = Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Terminate);
        Assert.Equal(("admin", "业务取消"), (log.OperatorId, log.Comment));
        Assert.Contains(_host.Notifier.Notifications, n => n.Kind == Abstractions.NotificationKind.Completed && n.Status == InstanceStatus.Terminated);

        var again = await Assert.ThrowsAsync<ApprovalException>(() => RunEngineAsync(e => e.TerminateAsync(instance.Id, "admin", "再次")));
        Assert.Contains("已结束", again.Message);
    }

    [Fact]
    public async Task Terminate_requires_reason_and_cancels_add_sign_tasks()
    {
        await _host.PublishAsync("t", TimedFlow("User", "r1"));
        var instance = await _host.StartAsync("t", "T2", "alice");
        var task = Assert.Single(await _host.TodoAsync("r1"));
        await RunEngineAsync(e => e.AddSignAsync(task.TaskId, "r1", ["sam"], AddSignMode.Before));

        await Assert.ThrowsAsync<ApprovalException>(() => RunEngineAsync(e => e.TerminateAsync(instance.Id, "admin", " ")));
        await RunEngineAsync(e => e.TerminateAsync(instance.Id, "admin", "重复提交"));

        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Terminated, saved.Status);
        Assert.All(saved.Tasks, t => Assert.Equal(ApprovalTaskStatus.Canceled, t.Status));
        Assert.Empty(await _host.TodoAsync("sam"));
    }

    // ───────────── 改派 ─────────────

    [Fact]
    public async Task Reassign_role_task_to_user_keeps_due_time_and_blocks_previous_handler()
    {
        await _host.PublishAsync("t", TimedFlow("Role", "Reviewer"));
        var instance = await _host.StartAsync("t", "R1", "alice");
        var original = Assert.Single(await _host.TodoAsync("r1"));
        _host.Clock.Advance(TimeSpan.FromMinutes(30));

        await RunEngineAsync(e => e.ReassignAsync(original.TaskId, "admin", "zoe", "r1 休假"));

        Assert.Empty(await _host.TodoAsync("r1"));
        var reassigned = Assert.Single(await _host.TodoAsync("zoe"));
        Assert.Equal(original.DueAt, reassigned.DueAt);
        var saved = await _host.GetInstanceAsync(instance.Id);
        var newTask = saved.Tasks.Single(t => t.Id == reassigned.TaskId);
        Assert.Equal((AssigneeKind.User, original.DueAt), (newTask.AssigneeKind, newTask.NextRemindAt));
        Assert.Equal(ApprovalTaskStatus.Transferred, saved.Tasks.Single(t => t.Id == original.TaskId).Status);
        Assert.Contains(saved.Logs, l => l.Action == ApprovalAction.Reassign && l.OperatorId == "admin" && l.Comment!.Contains("zoe"));

        await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(original.TaskId, "r1"));
        await _host.ApproveMyTaskAsync("zoe");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
    }

    [Fact]
    public async Task Reassign_rejects_initiator_and_existing_node_approver()
    {
        await _host.PublishAsync("all", TimedFlow("User", "r1,r2", mode: "All"));
        await _host.StartAsync("all", "R2", "alice");
        var task = Assert.Single(await _host.TodoAsync("r1"));

        var toInitiator = await Assert.ThrowsAsync<ApprovalException>(() => RunEngineAsync(e => e.ReassignAsync(task.TaskId, "admin", "alice")));
        var duplicated = await Assert.ThrowsAsync<ApprovalException>(() => RunEngineAsync(e => e.ReassignAsync(task.TaskId, "admin", "r2")));

        Assert.Contains("发起人", toInitiator.Message);
        Assert.Contains("已是该节点的审批人", duplicated.Message);
    }

    [Fact]
    public async Task Reassign_user_moves_pending_and_waiting_tasks_and_skips_self_approval()
    {
        await _host.PublishAsync("u", TimedFlow("User", "dave"));
        await _host.PublishAsync("seq", TimedFlow("User", "frank,dave", mode: "Sequential"));
        await _host.StartAsync("u", "B1", "alice");
        var erinsOwn = await _host.StartAsync("u", "B2", "erin");
        var sequential = await _host.StartAsync("seq", "B3", "alice");

        var preview = await _host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetOpenUserTasksAsync("dave"));
        _host.Identity.Add("admin", "Admin");
        var result = await _host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("dave", "erin", "admin", "离职交接"));

        Assert.Equal(3, preview.Total);
        Assert.Contains(preview.Items, t => t.Status == ApprovalTaskStatus.Waiting);
        Assert.Equal(2, result.Count);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(erinsOwn.Id, skipped.InstanceId);
        Assert.Contains("发起人", skipped.Reason);
        Assert.Single(await _host.TodoAsync("erin"));                          // 依次审批中尚未轮到的不在待办中
        var waiting = (await _host.GetInstanceAsync(sequential.Id)).Tasks.Single(t => t.AssigneeValue == "erin");
        Assert.Equal(ApprovalTaskStatus.Waiting, waiting.Status);

        await _host.ApproveMyTaskAsync("frank");
        Assert.Equal(2, (await _host.TodoAsync("erin")).Count);
        Assert.Single(await _host.TodoAsync("dave"));                           // 只剩发起人为 erin 的那条
    }

    // ───────────── 统计 ─────────────

    [Fact]
    public async Task Statistics_summarize_status_duration_nodes_overdue_and_backlog()
    {
        await _host.PublishAsync("s", TimedFlow("User", "r1"));
        var start = _host.Clock.GetUtcNow().UtcDateTime;

        await _host.StartAsync("s", "A", "alice");                                   // 00:00 发起，00:10 通过
        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        await _host.ApproveMyTaskAsync("r1");
        await _host.StartAsync("s", "B", "alice");                                   // 00:10 发起，00:40 驳回
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        var b = Assert.Single(await _host.TodoAsync("r1"));
        await RunEngineAsync(e => e.RejectAsync(b.TaskId, "r1", "不同意"));
        var c = await _host.StartAsync("s", "C", "alice");                           // 00:40 发起，00:50 撤回
        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        await RunEngineAsync(e => e.WithdrawAsync(c.Id, "alice"));
        await _host.StartAsync("s", "D", "alice");                                   // 00:50 发起，截止 01:50，现在 03:20 已超时
        _host.Clock.Advance(TimeSpan.FromMinutes(150));

        var stats = await _host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new StatisticsQuery(start.AddHours(-1), start.AddDays(1))));

        Assert.Equal((4, 1, 1, 1, 1, 0), (stats.Summary.Total, stats.Summary.Running, stats.Summary.Approved, stats.Summary.Rejected,
            stats.Summary.Withdrawn, stats.Summary.Terminated));
        Assert.Equal(50.0, stats.ApprovalRate);
        Assert.Equal(16.7, stats.Summary.AverageMinutes);                              // (10 + 30 + 10) / 3
        Assert.Equal(30.0, stats.Summary.P90Minutes);
        Assert.Equal(1, stats.OverdueTasks);
        var node = Assert.Single(stats.Nodes);
        Assert.Equal(("review", 2, 20.0, 0), (node.NodeId, node.Handled, node.AverageMinutes, node.Overdue));
        var backlog = Assert.Single(stats.Backlog);
        Assert.Equal(("r1", 1, 1), (backlog.AssigneeValue, backlog.Count, backlog.Overdue));
        Assert.Equal("s", Assert.Single(stats.Flows).DefinitionKey);

        await Assert.ThrowsAsync<ApprovalException>(() => _host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new StatisticsQuery(start, start.AddDays(400)))));
    }
}
