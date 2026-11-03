using Approval.Forms.Schema;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

/// <summary>审批方式、条件分支、撤回、转交、并发与事务等引擎规则。</summary>
public sealed class EngineRuleTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        _host.Identity.Add("r3", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Countersign_expands_role_and_requires_everyone()
    {
        await _host.PublishAsync("all", Flows.SingleNode("All"));
        var instance = await _host.StartAsync("all", "B1", "alice");

        var tasks = (await _host.GetInstanceAsync(instance.Id)).Tasks;
        Assert.Equal(["r1", "r2", "r3"], tasks.Select(t => t.AssigneeValue).Order());
        Assert.All(tasks, t => Assert.Equal(AssigneeKind.User, t.AssigneeKind));

        await _host.ApproveMyTaskAsync("r1");
        await _host.ApproveMyTaskAsync("r2");
        Assert.Equal(InstanceStatus.Running, (await _host.GetInstanceAsync(instance.Id)).Status);

        await _host.ApproveMyTaskAsync("r3");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
    }

    [Fact]
    public async Task Sequential_activates_approvers_one_by_one()
    {
        await _host.PublishAsync("seq", Flows.SingleNode("Sequential"));
        var instance = await _host.StartAsync("seq", "B2", "alice");

        Assert.Single(await _host.TodoAsync("r1"));
        Assert.Empty(await _host.TodoAsync("r2"));

        var waiting = (await _host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "r2");
        await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(waiting.Id, "r2"));

        await _host.ApproveMyTaskAsync("r1");
        await _host.ApproveMyTaskAsync("r2");
        await _host.ApproveMyTaskAsync("r3");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
    }

    [Theory]
    [InlineData(8000, "boss")]
    [InlineData(5000, "manager")]
    public async Task Condition_branch_routes_by_form_data(decimal amount, string expectedApprover)
    {
        await _host.PublishAsync("amount", Flows.AmountCondition);
        var instance = await _host.StartAsync("amount", "B3", "alice", new { amount });

        var task = Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks);
        Assert.Equal(expectedApprover, task.AssigneeValue);
    }

    [Fact]
    public async Task Form_field_resolver_and_auto_approve_policy()
    {
        await _host.PublishAsync("field", Flows.SingleNode("Any", "FormField", "reviewerId"));
        var instance = await _host.StartAsync("field", "B4", "alice", new { reviewerId = "zoe" });
        Assert.Equal("zoe", Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks).AssigneeValue);

        // 解析不到审批人：Error 策略拒绝发起，AutoApprove 策略直接通过
        await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("field", "B5", "alice", new { }));

        await _host.PublishAsync("auto", Flows.SingleNode("Any", "Role", "Nobody", emptyPolicy: "AutoApprove"));
        var auto = await _host.StartAsync("auto", "B6", "alice");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(auto.Id)).Status);
    }

    [Fact]
    public async Task Role_whose_only_member_is_initiator_does_not_create_dead_task()
    {
        _host.Identity.Add("alice", "Auditor");
        await _host.PublishAsync("auditor", Flows.SingleNode("Any", "Role", "Auditor"));

        // 否则会生成一个角色内谁都不能处理的任务，流程永久卡住
        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("auditor", "B7", "alice"));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, ex.Code);
    }

    [Fact]
    public async Task Withdraw_is_allowed_only_before_any_approval()
    {
        await _host.PublishAsync("seq", Flows.SingleNode("Sequential"));
        var a = await _host.StartAsync("seq", "W1", "alice");

        await Assert.ThrowsAsync<ApprovalException>(() =>
            _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().WithdrawAsync(a.Id, "r1")));

        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().WithdrawAsync(a.Id, "alice", "填错了"));
        Assert.Equal(InstanceStatus.Withdrawn, (await _host.GetInstanceAsync(a.Id)).Status);
        Assert.Contains(("Withdrawn", "W1"), _host.Handler.Calls);

        var b = await _host.StartAsync("seq", "W2", "alice");
        await _host.ApproveMyTaskAsync("r1");
        await Assert.ThrowsAsync<ApprovalException>(() =>
            _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().WithdrawAsync(b.Id, "alice")));
    }

    [Fact]
    public async Task Transfer_moves_task_to_another_user()
    {
        await _host.PublishAsync("user", Flows.SingleNode("Any", "User", "r1"));
        var instance = await _host.StartAsync("user", "T1", "alice");
        var todo = Assert.Single(await _host.TodoAsync("r1"));

        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().TransferAsync(todo.TaskId, "r1", "bob", "出差"));

        Assert.Empty(await _host.TodoAsync("r1"));
        await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(todo.TaskId, "r1"));
        await _host.ApproveMyTaskAsync("bob");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
    }

    [Fact]
    public async Task Repeated_approval_is_rejected()
    {
        await _host.PublishAsync("all", Flows.SingleNode("All"));
        await _host.StartAsync("all", "R1", "alice");
        var todo = Assert.Single(await _host.TodoAsync("r1"));

        await _host.ApproveAsync(todo.TaskId, "r1");
        await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(todo.TaskId, "r1"));
    }

    [Fact]
    public async Task Concurrent_countersign_approvals_conflict_instead_of_losing_update()
    {
        await _host.PublishAsync("all", Flows.SingleNode("All"));
        var instance = await _host.StartAsync("all", "C1", "alice");
        var tasks = (await _host.GetInstanceAsync(instance.Id)).Tasks;

        // 请求 B 先读到实例（模拟两人同时打开页面并提交）
        await using var scopeB = _host.NewScope();
        await scopeB.ServiceProvider.GetRequiredService<TestDbContext>()
            .Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync(i => i.Id == instance.Id);

        await _host.ApproveAsync(tasks.Single(t => t.AssigneeValue == "r1").Id, "r1");

        var engineB = scopeB.ServiceProvider.GetRequiredService<ApprovalEngine>();
        var ex = await Assert.ThrowsAsync<ApprovalException>(() =>
            engineB.ApproveAsync(tasks.Single(t => t.AssigneeValue == "r2").Id, "r2"));
        Assert.Equal(ApprovalErrorCode.Conflict, ex.Code);

        // 重试（新请求）成功
        await _host.ApproveAsync(tasks.Single(t => t.AssigneeValue == "r2").Id, "r2");
    }

    [Fact]
    public async Task Dates_are_read_back_as_utc()
    {
        await _host.PublishAsync("user", Flows.SingleNode("Any", "User", "r1"));
        var instance = await _host.StartAsync("user", "U1", "alice");

        var reloaded = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(reloaded.Tasks).CreatedAt.Kind);
    }

    [Theory]
    [InlineData("approve", "withdraw", InstanceStatus.Approved)]
    [InlineData("withdraw", "approve", InstanceStatus.Withdrawn)]
    [InlineData("timeout", "approve", InstanceStatus.Running)]
    [InlineData("approve", "timeout", InstanceStatus.Approved)]
    public async Task Stale_withdraw_approval_and_timeout_commands_conflict_without_overwriting_winner(
        string winner, string stale, InstanceStatus expected)
    {
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "r1"));
        flow.Nodes.Values.Single(n => n.Type == NodeType.Approval).Timeout = new TimeoutPolicy { Minutes = 1 };
        await _host.PublishAsync("race", flow.ToJson());
        var instance = await _host.StartAsync("race", "race-business", "alice");
        var taskId = Assert.Single(instance.Tasks).Id;
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await using var oldScope = _host.NewScope();
        await oldScope.ServiceProvider.GetRequiredService<TestDbContext>().Set<ApprovalInstance>()
            .Include(i => i.Tasks).SingleAsync(i => i.Id == instance.Id);

        static async Task Apply(ApprovalEngine engine, string operation, Guid instanceId, Guid task)
        {
            if (operation == "withdraw") await engine.WithdrawAsync(instanceId, "alice", "撤回");
            else if (operation == "timeout") Assert.True(await engine.HandleTimeoutAsync(task));
            else await engine.ApproveAsync(task, "r1", "同意");
        }
        await _host.RunAsync(sp => Apply(sp.GetRequiredService<ApprovalEngine>(), winner, instance.Id, taskId));
        var conflict = await Assert.ThrowsAsync<ApprovalException>(() => Apply(
            oldScope.ServiceProvider.GetRequiredService<ApprovalEngine>(), stale, instance.Id, taskId));
        Assert.Equal(ApprovalErrorCode.Conflict, conflict.Code);
        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(expected, saved.Status);
        Assert.Equal(winner == "timeout" ? 1 : 0, Assert.Single(saved.Tasks).RemindCount);
        Assert.Equal(winner == "approve" ? 1 : 0, saved.Logs.Count(l => l.Action == ApprovalAction.Approve));
        Assert.Equal(winner == "withdraw" ? 1 : 0, saved.Logs.Count(l => l.Action == ApprovalAction.Withdraw));
        if (winner == "timeout")
        {
            await _host.ApproveAsync(taskId, "r1");
            Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
        }
    }

    [Fact]
    public async Task Find_handleable_task_respects_roles_and_sequence()
    {
        await _host.PublishAsync("seq", Flows.SingleNode("Sequential"));
        var instance = await _host.StartAsync("seq", "H1", "alice");

        Task<ApprovalTask?> Find(string user) =>
            _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().FindHandleableTaskAsync(instance.Id, user));

        Assert.Equal("r1", (await Find("r1"))?.AssigneeValue);
        Assert.Null(await Find("r2"));                       // 依次审批尚未轮到
        Assert.Null(await Find("alice"));                    // 发起人
        await _host.ApproveMyTaskAsync("r1");
        Assert.Equal("r2", (await Find("r2"))?.AssigneeValue);
    }

    [Fact]
    public async Task Business_callback_failure_rolls_back_approval()
    {
        await _host.PublishAsync("user", Flows.SingleNode("Any", "User", "r1"));
        var instance = await _host.StartAsync("user", "X1", "alice");
        _host.Handler.FailOnApproved = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.ApproveMyTaskAsync("r1"));

        var reloaded = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Running, reloaded.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(reloaded.Tasks).Status);
    }

    [Fact]
    public async Task Publishing_creates_new_version_and_running_instances_keep_old_one()
    {
        await _host.PublishAsync("v", Flows.SingleNode("Any", "User", "r1"));
        var old = await _host.StartAsync("v", "V1", "alice");

        await _host.PublishAsync("v", Flows.SingleNode("Any", "User", "r2"));
        var @new = await _host.StartAsync("v", "V2", "alice");

        Assert.Equal(1, old.DefinitionVersion);
        Assert.Equal(2, @new.DefinitionVersion);
        await _host.ApproveMyTaskAsync("r1");               // 旧实例仍按 v1 走完
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(old.Id)).Status);
    }
}

public sealed class DefinitionValidatorTests
{
    private static readonly IReadOnlySet<string> Known = new HashSet<string> { "User", "Role" };

    [Fact]
    public void Valid_definition_has_no_errors() =>
        Assert.Empty(FormDefinitionValidator.Validate(FlowDefinition.Parse(Flows.SingleNode("Any")), Known));

    [Fact]
    public void Detects_cycle()
    {
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any"));
        flow.Nodes["review"].Next = "review";
        Assert.Contains(FormDefinitionValidator.Validate(flow, Known), e => e.Contains("环路"));
    }

    [Fact]
    public void Detects_missing_default_branch_unknown_approver_and_dangling_next()
    {
        var flow = FlowDefinition.Parse(Flows.AmountCondition);
        flow.Nodes["cond"].Branches.RemoveAll(b => b.Expression is null);
        flow.Nodes["big"].Approvers[0] = new ApproverRule("DeptLeader", "1");
        flow.Nodes["small"].Next = "missing";

        var errors = FormDefinitionValidator.Validate(flow, Known);
        Assert.Contains(errors, e => e.Contains("默认分支"));
        Assert.Contains(errors, e => e.Contains("DeptLeader"));
        Assert.Contains(errors, e => e.Contains("missing"));
    }

    [Fact]
    public void Designer_position_survives_round_trip()
    {
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any"));
        flow.Nodes["review"].Position = new NodePosition(120.5, 80);

        var reparsed = FlowDefinition.Parse(flow.ToJson());
        Assert.Equal(new NodePosition(120.5, 80), reparsed.Nodes["review"].Position);
        Assert.Null(reparsed.Nodes["start"].Position);
    }

    [Fact]
    public async Task Publish_rejects_invalid_definition()
    {
        await using var host = new TestHost();
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any"));
        flow.Nodes["review"].Next = "review";

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("bad", flow.ToJson()));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, ex.Code);
    }
}
