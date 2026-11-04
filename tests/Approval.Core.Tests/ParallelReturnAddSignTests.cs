using Approval.Forms.Schema;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>并行分支、退回（驳回到指定节点）、加签。</summary>
public sealed class ParallelReturnAddSignTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string User(string id, string name, string next, string mode = "Any", string value = "") => $$"""
        "{{id}}": { "name": "{{name}}", "type": "Approval", "mode": "{{mode}}",
                    "approvers": [{ "type": "{{(value.StartsWith("role:") ? "Role" : "User")}}", "value": "{{(value.StartsWith("role:") ? value[5..] : value.Length > 0 ? value : "u_" + id)}}" }],
                    "next": "{{next}}" }
        """;

    private static string Flow(params string[] nodes) => $$"""
        { "startNodeId": "start", "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "{{FirstNext(nodes)}}" },
            {{string.Join(",\n", nodes)}},
            "end": { "name": "结束", "type": "End" } } }
        """;

    // 约定：第一个节点定义即开始节点的后续
    private static string FirstNext(string[] nodes) => nodes[0].Trim().Split('"')[1];

    /// <summary>n0 → 并行[a1 → a2, b] → 合并 → fin</summary>
    private static readonly string ParallelFlow = Flow(
        User("n0", "初审", "p"),
        """ "p": { "name": "并行", "type": "Parallel", "branches": [ { "priority": 1, "next": "a1" }, { "priority": 2, "next": "b" } ] } """,
        User("a1", "技术审核", "a2"),
        User("a2", "技术复核", "j"),
        User("b", "财务审核", "j"),
        """ "j": { "name": "合并", "type": "Join", "next": "fin" } """,
        User("fin", "终审", "end"));

    private async Task<Guid> TaskOf(string userId) => Assert.Single(await _host.TodoAsync(userId)).TaskId;

    private Task<ApprovalInstance> Instance(Guid id) => _host.GetInstanceAsync(id);

    private Task Run(Func<ApprovalEngine, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApprovalEngine>()));

    // ───────────────────────── 并行 ─────────────────────────

    [Fact]
    public async Task Repeated_branch_completion_cannot_release_join_and_rejection_cancels_siblings()
    {
        await _host.PublishAsync("par", ParallelFlow);
        var instance = await _host.StartAsync("par", "repeat", "alice");
        await _host.ApproveMyTaskAsync("u_n0");
        var branch = await TaskOf("u_b");
        await _host.ApproveAsync(branch, "u_b");
        await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(branch, "u_b"));
        Assert.Empty(await _host.TodoAsync("u_fin"));
        var state = ExecutionState.Load(await Instance(instance.Id));
        Assert.Equal(1, state.Joins["j"].Arrived); Assert.Single(state.Joins["j"].ArrivedBranches);
        Assert.NotEqual(Guid.Empty, state.Joins["j"].ActivationId);
        var other = await TaskOf("u_a1");
        await Run(e => e.RejectAsync(other, "u_a1", "分支不通过"));
        var ended = await Instance(instance.Id);
        Assert.Equal(InstanceStatus.Rejected, ended.Status); Assert.Null(ended.StateJson);
        Assert.DoesNotContain(ended.Tasks, t => t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended);
        Assert.Empty(await _host.TodoAsync("u_fin"));
    }

    [Fact]
    public async Task Parallel_branches_run_together_and_join_waits_for_all()
    {
        await _host.PublishAsync("par", ParallelFlow);
        var instance = await _host.StartAsync("par", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n0");

        Assert.Equal("a1,b", (await Instance(instance.Id)).CurrentNodeId);
        Assert.Single(await _host.TodoAsync("u_a1"));
        Assert.Single(await _host.TodoAsync("u_b"));

        await _host.ApproveMyTaskAsync("u_b");
        Assert.Empty(await _host.TodoAsync("u_fin"));   // 另一分支未完成，合并节点等待
        await _host.ApproveMyTaskAsync("u_a1");
        await _host.ApproveMyTaskAsync("u_a2");

        Assert.Equal("fin", (await Instance(instance.Id)).CurrentNodeId);
        await _host.ApproveMyTaskAsync("u_fin");
        Assert.Equal(InstanceStatus.Approved, (await Instance(instance.Id)).Status);
    }

    [Fact]
    public async Task Nested_parallel_and_condition_inside_branch()
    {
        await _host.PublishAsync("nest", Flow(
            """ "p": { "name": "外层", "type": "Parallel", "branches": [ { "priority": 1, "next": "q" }, { "priority": 2, "next": "cond" } ] } """,
            """ "q": { "name": "内层", "type": "Parallel", "branches": [ { "priority": 1, "next": "c" }, { "priority": 2, "next": "d" } ] } """,
            User("c", "C", "jq"),
            User("d", "D", "jq"),
            """ "jq": { "name": "内层合并", "type": "Join", "next": "jp" } """,
            """ "cond": { "name": "金额", "type": "Condition", "branches": [ { "priority": 1, "expression": "amount > 100", "next": "big" }, { "priority": 99, "expression": null, "next": "jp" } ] } """,
            User("big", "大额", "jp"),
            """ "jp": { "name": "外层合并", "type": "Join", "next": "end" } """));

        var small = await _host.StartAsync("nest", "B1", "alice", new { amount = 1 });
        Assert.Equal("c,d", (await Instance(small.Id)).CurrentNodeId);   // 条件分支直接到达外层合并
        await _host.ApproveMyTaskAsync("u_c");
        await _host.ApproveMyTaskAsync("u_d");
        Assert.Equal(InstanceStatus.Approved, (await Instance(small.Id)).Status);

        var big = await _host.StartAsync("nest", "B2", "alice", new { amount = 500 });
        await _host.ApproveMyTaskAsync("u_c");
        await _host.ApproveMyTaskAsync("u_d");
        Assert.Equal(InstanceStatus.Running, (await Instance(big.Id)).Status);
        await _host.ApproveMyTaskAsync("u_big");
        Assert.Equal(InstanceStatus.Approved, (await Instance(big.Id)).Status);
    }

    [Theory]
    [InlineData("""{ "priority": 1, "next": "a" }, { "priority": 2, "next": "end" }""", "合并前到达了结束节点")]
    [InlineData("""{ "priority": 1, "next": "a" }, { "priority": 2, "next": "x" }""", "被多个并行分支共用")]
    [InlineData("""{ "priority": 1, "next": "a" }""", "至少需要两个分支")]
    public void Validator_rejects_unstructured_parallel(string branches, string error)
    {
        var flow = FlowDefinition.Parse($$"""
        { "startNodeId": "start", "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "p" },
            "p": { "name": "并行", "type": "Parallel", "branches": [ {{branches}} ] },
            "a": { "name": "A", "type": "Approval", "approvers": [{ "type": "User", "value": "u" }], "next": "x" },
            "x": { "name": "X", "type": "Approval", "approvers": [{ "type": "User", "value": "u" }], "next": "j" },
            "j": { "name": "合并", "type": "Join", "next": "end" },
            "end": { "name": "结束", "type": "End" } } }
        """);
        var errors = FormDefinitionValidator.Validate(flow, new HashSet<string> { "User" });
        Assert.Contains(errors, e => e.Contains(error));
    }

    [Fact]
    public void Validator_rejects_join_without_parallel()
    {
        var flow = FlowDefinition.Parse(Flow(User("a", "A", "j"), """ "j": { "name": "合并", "type": "Join", "next": "end" } """));
        Assert.Contains(FormDefinitionValidator.Validate(flow, new HashSet<string> { "User" }), e => e.Contains("没有对应的并行节点"));
    }

    // ───────────────────────── 退回 ─────────────────────────

    [Fact]
    public async Task Return_to_previous_node_reactivates_it_and_flow_continues()
    {
        await _host.PublishAsync("lin", Flow(User("n1", "一审", "n2"), User("n2", "二审", "n3"), User("n3", "三审", "end")));
        var instance = await _host.StartAsync("lin", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n1");
        await _host.ApproveMyTaskAsync("u_n2");

        var taskId = await TaskOf("u_n3");
        var targets = await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().GetReturnTargetsAsync(taskId, "u_n3"));
        Assert.Equal(["start", "n1", "n2"], targets.Select(t => t.NodeId));
        Assert.True(targets[0].IsInitiator);

        await Run(e => e.ReturnAsync(taskId, "u_n3", "材料需补充", "n1"));

        var result = await Instance(instance.Id);
        Assert.Equal(InstanceStatus.Running, result.Status);
        Assert.Equal("n1", result.CurrentNodeId);
        Assert.Equal(1, result.Round);
        Assert.Contains(result.Logs, l => l.Action == ApprovalAction.Return && l.Comment == "退回到「一审」：材料需补充");

        await _host.ApproveMyTaskAsync("u_n1");
        await _host.ApproveMyTaskAsync("u_n2");
        await _host.ApproveMyTaskAsync("u_n3");
        Assert.Equal(InstanceStatus.Approved, (await Instance(instance.Id)).Status);
    }

    [Fact]
    public async Task Cannot_return_to_a_node_not_yet_passed_or_when_disabled()
    {
        await _host.PublishAsync("lin", Flow(User("n1", "一审", "n2"), User("n2", "二审", "end")));
        await _host.StartAsync("lin", "B1", "alice");
        var taskId = await TaskOf("u_n1");

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => Run(e => e.ReturnAsync(taskId, "u_n1", "x", "n2")));
        Assert.Contains("不能退回到该节点", ex.Message);

        await _host.PublishAsync("noret", Flow(
            """ "n1": { "name": "一审", "type": "Approval", "allowReturn": false, "approvers": [{ "type": "User", "value": "u_n1" }], "next": "end" } """));
        await _host.StartAsync("noret", "B2", "alice");
        var noReturn = (await _host.TodoAsync("u_n1")).Single(t => t.BusinessKey == "B2").TaskId;
        Assert.Empty(await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().GetReturnTargetsAsync(noReturn, "u_n1")));
    }

    [Fact]
    public async Task Return_inside_a_branch_keeps_sibling_branch_progress()
    {
        await _host.PublishAsync("par", ParallelFlow);
        var instance = await _host.StartAsync("par", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n0");
        await _host.ApproveMyTaskAsync("u_a1");
        await _host.ApproveMyTaskAsync("u_b");   // 财务分支已到达合并节点

        var taskId = await TaskOf("u_a2");
        var targets = await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().GetReturnTargetsAsync(taskId, "u_a2"));
        Assert.Equal(["start", "n0", "a1"], targets.Select(t => t.NodeId));   // 不能退回到兄弟分支 b

        await Run(e => e.ReturnAsync(taskId, "u_a2", "复核不通过", "a1"));
        Assert.Equal("a1", (await Instance(instance.Id)).CurrentNodeId);

        await _host.ApproveMyTaskAsync("u_a1");
        await _host.ApproveMyTaskAsync("u_a2");   // 财务分支无需重审，直接汇合
        Assert.Equal("fin", (await Instance(instance.Id)).CurrentNodeId);
    }

    [Fact]
    public async Task Return_out_of_parallel_block_cancels_other_branches()
    {
        await _host.PublishAsync("par", ParallelFlow);
        var instance = await _host.StartAsync("par", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n0");
        await _host.ApproveMyTaskAsync("u_a1");

        await Run(async e => await e.ReturnAsync(await TaskOf("u_a2"), "u_a2", "整体重审", "n0"));

        var result = await Instance(instance.Id);
        Assert.Equal("n0", result.CurrentNodeId);
        Assert.Null(result.StateJson);   // 合并计数已清除
        Assert.Empty(await _host.TodoAsync("u_b"));   // 兄弟分支的待办被取消

        await _host.ApproveMyTaskAsync("u_n0");
        Assert.Equal("a1,b", (await Instance(instance.Id)).CurrentNodeId);
    }

    [Fact]
    public async Task Return_to_initiator_then_resubmit_with_new_form()
    {
        await _host.PublishAsync("lin", Flow(User("n1", "一审", "n2"), User("n2", "二审", "end")));
        var instance = await _host.StartAsync("lin", "B1", "alice", new { amount = 1 });
        await _host.ApproveMyTaskAsync("u_n1");
        await Run(async e => await e.ReturnAsync(await TaskOf("u_n2"), "u_n2", "金额有误", "start"));

        // 发起人的待办中出现“重新提交”，并收到退回通知
        var todo = Assert.Single(await _host.TodoAsync("alice"));
        Assert.Equal(ApprovalTaskKind.Resubmit, todo.Kind);
        Assert.Contains(await _host.MessagesAsync("alice"), m => m.Content.Contains("被 u_n2 退回：金额有误"));
        await Assert.ThrowsAsync<ApprovalException>(() => Run(e => e.RejectAsync(todo.TaskId, "alice", "x")));

        await Run(e => e.ResubmitAsync(todo.TaskId, "alice", "已修改", new { amount = 2 }));

        var result = await Instance(instance.Id);
        Assert.Equal("n1", result.CurrentNodeId);
        Assert.Contains("\"amount\":2", result.FormDataJson);
        Assert.Contains(result.Logs, l => l.Action == ApprovalAction.Resubmit);
    }

    [Fact]
    public async Task Initiator_can_withdraw_after_being_returned()
    {
        await _host.PublishAsync("lin", Flow(User("n1", "一审", "n2"), User("n2", "二审", "end")));
        var instance = await _host.StartAsync("lin", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n1");
        await Run(async e => await e.ReturnAsync(await TaskOf("u_n2"), "u_n2", "不需要了", "start"));

        await Run(e => e.WithdrawAsync(instance.Id, "alice"));
        Assert.Equal(InstanceStatus.Withdrawn, (await Instance(instance.Id)).Status);
    }

    // ───────────────────────── 加签 ─────────────────────────

    [Fact]
    public async Task Before_sign_suspends_until_signer_approves()
    {
        await _host.PublishAsync("one", Flow(User("n1", "审核", "end")));
        var instance = await _host.StartAsync("one", "B1", "alice");
        await Run(async e => await e.AddSignAsync(await TaskOf("u_n1"), "u_n1", ["x"], AddSignMode.Before, "请先看下"));

        Assert.Empty(await _host.TodoAsync("u_n1"));
        var parent = (await Instance(instance.Id)).Tasks.Single(t => t.AssigneeValue == "u_n1");
        Assert.Equal(ApprovalTaskStatus.Suspended, parent.Status);

        await _host.ApproveMyTaskAsync("x");
        Assert.Equal(InstanceStatus.Running, (await Instance(instance.Id)).Status);   // 回到原审批人
        await _host.ApproveMyTaskAsync("u_n1");
        Assert.Equal(InstanceStatus.Approved, (await Instance(instance.Id)).Status);
    }

    [Fact]
    public async Task After_sign_requires_signer_before_node_passes()
    {
        await _host.PublishAsync("one", Flow(User("n1", "审核", "n2"), User("n2", "二审", "end")));
        var instance = await _host.StartAsync("one", "B1", "alice");
        await Run(async e => await e.AddSignAsync(await TaskOf("u_n1"), "u_n1", ["x", "y"], AddSignMode.After));

        Assert.Equal("n1", (await Instance(instance.Id)).CurrentNodeId);
        await _host.ApproveMyTaskAsync("x");
        Assert.Equal("n1", (await Instance(instance.Id)).CurrentNodeId);
        await _host.ApproveMyTaskAsync("y");
        Assert.Equal("n2", (await Instance(instance.Id)).CurrentNodeId);
    }

    [Fact]
    public async Task Parallel_sign_on_or_sign_node_is_still_required()
    {
        await _host.PublishAsync("any", Flow(User("n1", "审核", "end", "All", "role:Reviewer")));
        var instance = await _host.StartAsync("any", "B1", "alice");
        await Run(async e => await e.AddSignAsync(await TaskOf("r1"), "r1", ["x"], AddSignMode.Parallel));

        await _host.ApproveMyTaskAsync("r1");
        await _host.ApproveMyTaskAsync("r2");
        Assert.Equal(InstanceStatus.Running, (await Instance(instance.Id)).Status);   // 加签人尚未同意
        await _host.ApproveMyTaskAsync("x");
        Assert.Equal(InstanceStatus.Approved, (await Instance(instance.Id)).Status);
    }

    [Fact]
    public async Task After_sign_in_sequential_node_delays_next_person()
    {
        await _host.PublishAsync("seq", Flow(User("n1", "依次", "end", "Sequential", "role:Reviewer")));
        var instance = await _host.StartAsync("seq", "B1", "alice");
        await Run(async e => await e.AddSignAsync(await TaskOf("r1"), "r1", ["x"], AddSignMode.After));

        Assert.Empty(await _host.TodoAsync("r2"));
        await _host.ApproveMyTaskAsync("x");
        await _host.ApproveMyTaskAsync("r2");
        Assert.Equal(InstanceStatus.Approved, (await Instance(instance.Id)).Status);
    }

    [Fact]
    public async Task Add_sign_validation()
    {
        await _host.PublishAsync("any", Flow(User("n1", "审核", "end", "All", "role:Reviewer")));
        await _host.StartAsync("any", "B1", "alice");
        var taskId = await TaskOf("r1");

        Task Sign(params string[] users) => Run(e => e.AddSignAsync(taskId, "r1", users, AddSignMode.Parallel));
        Assert.Contains("自己", (await Assert.ThrowsAsync<ApprovalException>(() => Sign("r1"))).Message);
        Assert.Contains("发起人", (await Assert.ThrowsAsync<ApprovalException>(() => Sign("alice"))).Message);
        Assert.Contains("已是该节点的审批人", (await Assert.ThrowsAsync<ApprovalException>(() => Sign("r2"))).Message);

        await _host.PublishAsync("nosign", Flow(
            """ "n1": { "name": "审核", "type": "Approval", "allowAddSign": false, "approvers": [{ "type": "User", "value": "u" }], "next": "end" } """));
        await _host.StartAsync("nosign", "B2", "alice");
        var other = await TaskOf("u");
        await Assert.ThrowsAsync<ApprovalException>(() => Run(e => e.AddSignAsync(other, "u", ["x"], AddSignMode.Before)));
    }

    [Fact]
    public async Task Signer_can_return_and_rounds_separate_history()
    {
        await _host.PublishAsync("lin", Flow(User("n1", "一审", "n2"), User("n2", "二审", "end")));
        var instance = await _host.StartAsync("lin", "B1", "alice");
        await _host.ApproveMyTaskAsync("u_n1");
        await Run(async e => await e.AddSignAsync(await TaskOf("u_n2"), "u_n2", ["x"], AddSignMode.Before));
        await Run(async e => await e.ReturnAsync(await TaskOf("x"), "x", "一审意见不充分", "n1"));

        var result = await Instance(instance.Id);
        Assert.Equal("n1", result.CurrentNodeId);
        // 二审的挂起任务随退回取消；一审新一轮任务的轮次为 1
        Assert.All(result.Tasks.Where(t => t.NodeId == "n2"), t => Assert.NotEqual(ApprovalTaskStatus.Suspended, t.Status));
        Assert.Equal(1, result.Tasks.Single(t => t.NodeId == "n1" && t.Status == ApprovalTaskStatus.Pending).Round);
    }
}
