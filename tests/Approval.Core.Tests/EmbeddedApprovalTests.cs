using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class EmbeddedApprovalTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{
          "start":{"type":"Start","name":"提交","next":"cc"},
          "cc":{"type":"Cc","name":"抄送","approvers":[{"type":"Role","value":"Readers"}],"next":"review"},
          "review":{"type":"Approval","name":"审批","approvers":[{"type":"User","value":"bob"}],"next":"end","requireComment":true},
          "end":{"type":"End","name":"完成"}}}
        """;

    private static async Task<TestHost> Create(Action<ApprovalBuilder>? build = null)
    {
        var host = new TestHost(build: build);
        host.Identity.Add("alice"); host.Identity.Add("bob");
        host.Identity.Add("reader1", "Readers"); host.Identity.Add("reader2", "Readers");
        await host.PublishAsync("embedded", Flow);
        return host;
    }

    [Fact]
    public async Task Returned_application_retains_business_slot_version_and_history_then_resubmits_once()
    {
        await using var host = await Create();
        var instance = await host.StartAsync("embedded", "one", "alice", new { amount = 1 });
        var original = Assert.Single(await host.TodoAsync("bob"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(original.TaskId, "bob", "请修改", "start"));
        var returned = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Returned, returned.Status); Assert.Null(returned.CompletedAt);
        Assert.Empty(await host.TodoAsync("bob"));
        var resubmit = Assert.Single(await host.TodoAsync("alice"));
        Assert.Equal(ApprovalTaskKind.Resubmit, resubmit.Kind);
        var conflict = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("embedded", "one", "alice"));
        Assert.Equal(ApprovalErrorCode.Conflict, conflict.Code);
        await host.PublishAsync("embedded", Flow.Replace("bob", "other"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.TaskId, "alice", "已修改", new { amount = 2 }));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.TaskId, "alice")));
        var current = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Running, current.Status); Assert.Equal(instance.DefinitionId, current.DefinitionId);
        Assert.Equal(1, current.DefinitionVersion); Assert.Contains("\"amount\":2", current.FormDataJson);
        Assert.Single(current.Tasks, t => t.Status == ApprovalTaskStatus.Pending && t.AssigneeValue == "bob");
        Assert.Contains(current.Logs, l => l.Action == ApprovalAction.Return && l.Comment!.Contains("请修改") && l.TaskId == original.TaskId);
        Assert.Single(current.Logs, l => l.Action == ApprovalAction.Resubmit);
        Assert.Single(await host.MessagesAsync("alice"), m => m.Event == NotifyEvent.Returned);
    }

    [Fact]
    public async Task Carbon_copy_reads_are_per_user_idempotent_authorized_and_independent_of_messages()
    {
        await using var host = await Create();
        var instance = await host.StartAsync("embedded", "one", "alice");
        var one = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().ListAsync("reader1", true));
        var item = Assert.Single(one.Items); Assert.Null(item.ReadAt);
        Assert.Single((await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().ListAsync("reader1",
            query: new(Keyword: instance.Id.ToString())))).Items);
        Assert.Empty(await host.TodoAsync("reader1"));
        await host.RunAsync(async sp =>
        {
            var copies = sp.GetRequiredService<ApprovalCarbonCopyService>();
            await copies.MarkReadAsync("reader1", item.Id); await copies.MarkReadAsync("reader1", item.Id);
        });
        Assert.Empty((await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().ListAsync("reader1", true))).Items);
        Assert.Single((await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().ListAsync("reader2", true))).Items);
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().MarkReadAsync("alice", item.Id)));
        Assert.Equal(ApprovalErrorCode.NotFound, denied.Code);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalCarbonCopyRead>().CountAsync()));
        Assert.False(Assert.Single(await host.MessagesAsync("reader1")).IsRead);
        Assert.Empty((await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().ListAsync("reader2", query: new(ApproverId: "nobody")))).Items);
        var history = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().HistoryAsync("alice", instance.Id));
        Assert.Equal("Readers", Assert.Single(history).AssigneeValue);
    }

    private sealed class Events : IApprovalEventHandler
    {
        public readonly List<ApprovalEvent> Attempts = [];
        public bool FailOnce = true;
        public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
        {
            Attempts.Add(approvalEvent);
            if (FailOnce) { FailOnce = false; throw new InvalidOperationException("宿主临时失败"); }
            return Task.CompletedTask;
        }
    }

    private sealed class Status : IApprovalUserStatusProvider
    {
        public readonly HashSet<string> Disabled = [];
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(
            userId is "alice" or "bob" or "reader1" or "reader2" && !Disabled.Contains(userId));
    }

    [Fact]
    public async Task Host_user_state_blocks_unknown_start_transfer_addsign_and_disabled_approver()
    {
        var status = new Status();
        await using var host = await Create(b => b.Services.AddSingleton<IApprovalUserStatusProvider>(status));
        var unknown = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("embedded", "unknown", "missing"));
        Assert.Equal(ApprovalErrorCode.Forbidden, unknown.Code);
        var instance = await host.StartAsync("embedded", "one", "alice"); var task = Assert.Single(await host.TodoAsync("bob"));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().TransferAsync(task.TaskId, "bob", "missing")));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().AddSignAsync(task.TaskId, "bob", ["missing"], AddSignMode.Before)));
        status.Disabled.Add("bob");
        var disabled = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob", "同意")));
        Assert.Equal(ApprovalErrorCode.Forbidden, disabled.Code);
        var saved = await host.GetInstanceAsync(instance.Id); Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status);
    }

    [Fact]
    public async Task Copy_creates_only_a_draft_and_cannot_overwrite_existing_flow()
    {
        await using var host = await Create();
        var draft = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().CopyAsync("embedded", "copy", "副本"));
        Assert.Equal(0, draft.LastVersion); Assert.True(draft.HasDraftChanges); Assert.Null(draft.ActiveDefinitionId);
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("copy", "one", "alice"));
        var conflict = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().CopyAsync("embedded", "copy", "重复")));
        Assert.Equal(ApprovalErrorCode.Conflict, conflict.Code);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("copy", draft.Revision));
        var instance = await host.StartAsync("copy", "copy-business", "alice"); Assert.Equal("copy", instance.DefinitionKey);
    }

    [Fact]
    public async Task Automatic_timeout_decisions_are_reminders_unless_host_explicitly_enables_them()
    {
        await using var host = new TestHost();
        await host.PublishAsync("timeout", Flow.Replace("\"requireComment\":true", "\"requireComment\":true,\"timeout\":{\"minutes\":1,\"action\":\"AutoApprove\"}"));
        var instance = await host.StartAsync("timeout", "one", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await host.ScanTimeoutsAsync());
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status);
        Assert.Contains(saved.Logs, l => l.Action == ApprovalAction.Remind);
        Assert.DoesNotContain(saved.Logs, l => l.Action == ApprovalAction.TimeoutApprove);
    }

    [Fact]
    public async Task Host_events_are_transactional_retry_with_stable_ids_and_distinguish_return_from_rejection()
    {
        var events = new Events();
        await using var host = await Create(b => b.Services.AddSingleton<IApprovalEventHandler>(events));
        var instance = await host.StartAsync("embedded", "one", "alice");
        Assert.Empty(events.Attempts);
        var pending = await host.OutboxAsync(); Assert.Contains(pending, m => m.Description == "Submit");
        await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync(), m => m.Status == OutboxStatus.Pending);
        host.Clock.Advance(TimeSpan.FromMinutes(1)); await host.DispatchOutboxAsync();
        Assert.Equal(2, events.Attempts.Count(e => e.Id == failed.Id));
        var task = Assert.Single(await host.TodoAsync("bob"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(task.TaskId, "bob", "修改", "start"));
        var resubmit = Assert.Single(await host.TodoAsync("alice"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.TaskId, "alice"));
        await host.DispatchOutboxAsync();
        Assert.Contains(events.Attempts, e => e.EventType == "Return" && e.Status == InstanceStatus.Returned && e.TaskId == task.TaskId);
        Assert.Contains(events.Attempts, e => e.EventType == "Resubmit" && e.InstanceId == instance.Id);
        Assert.DoesNotContain(events.Attempts, e => e.EventType == "Reject");
        Assert.Contains(events.Attempts, e => e.EventType == "TaskCreated");
        Assert.Contains(events.Attempts, e => e.EventType == "NodeEntered");
    }

    [Fact]
    public async Task Required_comments_and_append_only_logs_are_enforced_by_server()
    {
        await using var host = await Create(); var instance = await host.StartAsync("embedded", "one", "alice");
        var task = Assert.Single(await host.TodoAsync("bob"));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob")));
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks).Status);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob", "同意"));
        Assert.Equal(InstanceStatus.Approved, (await host.GetInstanceAsync(instance.Id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            (await db.Set<ApprovalLog>().FirstAsync()).Comment = "覆盖历史";
            await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Remove(await db.Set<ApprovalLog>().FirstAsync()); await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Query_filters_and_sorting_are_applied_before_paging()
    {
        await using var host = await Create();
        await host.StartAsync("embedded", "C-1", "alice"); host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.StartAsync("embedded", "C-2", "alice");
        var page = await host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("bob",
            new(BusinessType: RecordingBusinessHandler.Type, BusinessKey: "C-", InitiatorId: "alice", ApproverId: "bob", Sort: ApprovalSort.Newest), new(1, 1)));
        Assert.Equal(2, page.Total); Assert.Equal("C-2", Assert.Single(page.Items).BusinessKey);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetStartedByAsync("alice", new(Keyword: "C-1", BusinessType: RecordingBusinessHandler.Type), new(1, 1)));
        Assert.Equal(1, result.Total); Assert.Equal("C-1", Assert.Single(result.Items).BusinessKey);
    }

    [Theory]
    [InlineData("amount >")]
    [InlineData("(amount > 1")]
    [InlineData("amount @ 1")]
    public void Invalid_condition_syntax_is_rejected_before_publication(string expression) =>
        Assert.NotNull(Conditions.DynamicExpressoConditionEvaluator.ValidateSyntax(expression));

    [Theory]
    [InlineData("approved == true")]
    [InlineData("false != approved")]
    [InlineData("\"Special\" == certType")]
    [InlineData("amount > 10 && approved")]
    [InlineData("risk.body.score > 80")]
    public void Valid_condition_types_and_dynamic_host_fields_are_not_rejected(string expression) =>
        Assert.Null(Conditions.DynamicExpressoConditionEvaluator.ValidateSyntax(expression));

    [Fact]
    public void Duplicate_node_ids_are_rejected_without_silently_overwriting() =>
        Assert.Throws<ApprovalException>(() => FlowDefinition.Parse("""{"nodes":{"same":{"type":"Start"},"same":{"type":"End"}}}"""));
}
