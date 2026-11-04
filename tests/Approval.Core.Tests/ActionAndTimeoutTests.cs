using System.Net;
using System.Text.Json.Nodes;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class DiscountLifecycle : IApprovalLifecycleHandler
{
    public Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct)
    {
        var form = JsonNode.Parse(instance.FormDataJson)!.AsObject();
        form["discounted"] = form["amount"]!.GetValue<decimal>() * 0.9m;
        return Task.FromResult(new ApprovalStartPreparation(instance.Title, form.ToJsonString()));
    }
}

public sealed class LifecyclePreparationTests
{
    [Theory]
    [InlineData(200, "big", 180)]
    [InlineData(100, "small", 90)]
    public async Task Lifecycle_prepares_snapshot_before_condition_evaluation(int amount, string expected, int discounted)
    {
        await using var host = new TestHost(build: b => b.AddLifecycleHandler<DiscountLifecycle>());
        const string flow = """
        {"schemaVersion":1,"startNodeId":"s","nodes":{"s":{"type":"Start","next":"c"},
        "c":{"type":"Condition","branches":[{"priority":1,"expression":"discounted > 100","next":"big"},{"priority":2,"next":"small"}]},
        "big":{"type":"Approval","approvers":[{"type":"User","value":"boss"}],"next":"e"},
        "small":{"type":"Approval","approvers":[{"type":"User","value":"manager"}],"next":"e"},"e":{"type":"End"}}}
        """;
        await host.PublishAsync("f", flow); var instance = await host.StartAsync("f", "B1", "alice", new { amount });
        Assert.Equal(expected, (await host.GetInstanceAsync(instance.Id)).CurrentNodeId);
        Assert.Equal(discounted, JsonNode.Parse((await host.GetInstanceAsync(instance.Id)).FormDataJson)!["discounted"]!.GetValue<decimal>());
    }
}

/// <summary>节点时限：超时提醒 / 重复催办、自动同意、自动驳回。</summary>
public sealed class TimeoutTests : IAsyncLifetime
{
    private readonly TestHost _host = new(o => o.EnableAutomaticTimeoutDecisions = true);

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string TimeoutFlow(string timeoutJson, string mode = "Any") => $$"""
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "{{mode}}", "timeout": {{timeoutJson}},
                    "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    [Fact]
    public async Task Reminds_after_deadline_and_repeats_up_to_the_limit()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 60, "repeatMinutes": 30, "maxReminders": 2 }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");
        var task = Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks);
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(60), task.DueAt);

        _host.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await _host.ScanTimeoutsAsync());

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());
        // 未配置 Timeout 通知规则时默认给角色成员发站内信
        var reminders = await _host.MessagesAsync();
        Assert.Equal(["r1", "r2"], reminders.Select(m => m.UserId).Order());
        Assert.All(reminders, m => Assert.Equal(NotifyEvent.Timeout, m.Event));
        Assert.Contains("已超过处理时限", reminders[0].Content);

        Assert.Equal(0, await _host.ScanTimeoutsAsync()); // 同一时刻不重复提醒
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());
        _host.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, await _host.ScanTimeoutsAsync()); // 已达最多提醒次数

        task = Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks);
        Assert.Equal(2, task.RemindCount);
        Assert.Null(task.NextRemindAt);
        Assert.Equal(2, (await _host.LogsAsync(instance.Id)).Count(l => l.Action == ApprovalAction.Remind));
    }

    [Fact]
    public async Task Handled_task_is_not_reminded()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10 }"""));
        await _host.StartAsync("t", "B1", "alice");
        await _host.ApproveMyTaskAsync("r1");

        _host.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await _host.ScanTimeoutsAsync());
    }

    [Fact]
    public async Task Auto_approve_on_timeout_advances_the_flow()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10, "action": "AutoApprove" }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");

        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());

        var result = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, result.Status);
        Assert.Contains(result.Logs, l => l.Action == ApprovalAction.TimeoutApprove && l.OperatorId == ApprovalEngine.SystemOperator);
        Assert.Contains(_host.Handler.Calls, c => c.Event == "Approved");
    }

    [Fact]
    public async Task Auto_reject_on_timeout_finishes_as_rejected()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10, "action": "AutoReject" }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");

        _host.Clock.Advance(TimeSpan.FromMinutes(15));
        await _host.ScanTimeoutsAsync();

        Assert.Equal(InstanceStatus.Rejected, (await _host.GetInstanceAsync(instance.Id)).Status);
        Assert.Contains(_host.Handler.Calls, c => c.Event == "Rejected");
    }

    [Fact]
    public async Task Sequential_deadline_starts_when_it_is_your_turn()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 60 }""", "Sequential"));
        var instance = await _host.StartAsync("t", "B1", "alice");
        Assert.Null((await _host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "r2").DueAt);

        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await _host.ApproveMyTaskAsync("r1");

        var second = (await _host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "r2");
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(60), second.DueAt);
    }

    [Fact]
    public async Task Validator_rejects_invalid_timeout()
    {
        const string flow = """
        {
          "schemaVersion":1,
          "startNodeId": "start",
          "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "cc", "timeout": { "minutes": 5 } },
            "cc":    { "name": "抄送", "type": "Cc", "approvers": [{ "type": "Initiator", "value": "" }], "next": "review" },
            "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "User", "value": "u" }], "next": "end",
                        "timeout": { "minutes": 0, "repeatMinutes": -1 } },
            "end":   { "name": "结束", "type": "End" }
          }
        }
        """;
        var errors = await _host.RunAsync(sp =>
            sp.GetRequiredService<ApprovalDefinitionService>().ValidateAsync(FlowDefinition.Parse(flow)));

        Assert.Contains(errors, e => e.Contains("'start' 不是审批节点"));
        Assert.Contains(errors, e => e.Contains("时限必须大于 0"));
        Assert.Contains(errors, e => e.Contains("催办间隔必须大于 0"));
    }
}
