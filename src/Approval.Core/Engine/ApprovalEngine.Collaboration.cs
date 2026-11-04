using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Runtime;
using Approval.Core.Notifications;

namespace Approval.Core.Engine;

public enum AddSignMode
{
    /// <summary>前加签：加签人先审，全部同意后回到自己继续审批。</summary>
    Before,

    /// <summary>后加签：自己同意，加签人也同意后节点才算通过（依次审批时才轮到下一位）。</summary>
    After,

    /// <summary>并加签：与自己同时审批，节点通过前加签人必须同意。</summary>
    Parallel
}

/// <summary>可退回的目标节点。</summary>
public sealed record ReturnTarget(string NodeId, string NodeName, bool IsInitiator);

// 退回（驳回到指定节点）、退回发起人后重新提交、加签
public sealed partial class ApprovalEngine
{
    // ───────────────────────── 退回 ─────────────────────────

    /// <summary>当前用户处理该任务时可以退回到的节点：发起人，以及本实例已审批通过、位于当前节点上游的审批节点。</summary>
    public async Task<IReadOnlyList<ReturnTarget>> GetReturnTargetsAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReturn, new() { TaskId = taskId }, userId, ct);
        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct, write: false);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        return ReturnTargetsOf(operation, instance, flow, task)
            .Select(n => new ReturnTarget(n.Id, n.Name, n.Type == NodeType.Start))
            .ToList();
    }

    /// <summary>
    /// 驳回并退回到指定节点（流程继续）：目标为审批节点时重新生成该节点的待办；目标为开始节点时退回发起人，由发起人重新提交或撤回。
    /// 退出所在的并行块时，块内其他分支的待办一并取消；在同一分支内退回不影响其他分支。
    /// </summary>
    public Task ReturnAsync(Guid taskId, string userId, string comment, string targetNodeId, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => ReturnAsyncCore(taskId, userId, comment, targetNodeId, token), ct);

    private async Task ReturnAsyncCore(Guid taskId, string userId, string comment, string targetNodeId, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReturn, new() { TaskId = taskId }, userId, ct);
        if (string.IsNullOrWhiteSpace(comment)) throw new ApprovalException("退回必须填写意见");

        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        var target = ReturnTargetsOf(operation, instance, flow, task).FirstOrDefault(n => n.Id == targetNodeId)
                     ?? throw new ApprovalException("不能退回到该节点：只能退回到已审批通过的上游节点或发起人");

        await ReturnCoreAsync(operation, instance, flow, task, userId, comment, target, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    private List<FlowNode> ReturnTargetsOf(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task)
    {
        var node = flow.GetNode(task.NodeId);
        if (task.Kind == ApprovalTaskKind.Resubmit || node.Type != NodeType.Approval || !node.AllowReturn) return [];

        var structure = operation.StructureOf(flow);
        var scope = structure.ScopeOf(node.Id);
        // 节点最近一次审批通过的时间，用于按经过的先后排序
        var passed = instance.Tasks
            .Where(t => t.Status == ApprovalTaskStatus.Approved && t.Kind == ApprovalTaskKind.Approval)
            .GroupBy(t => t.NodeId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.CompletedAt));

        var candidates = flow.Nodes.Values
            .Where(n => n.Type == NodeType.Approval && n.Id != node.Id && passed.ContainsKey(n.Id)
                        && structure.CanReach(n.Id, node.Id)
                        // 只能退回到同一分支或外层，不能退回到兄弟分支或已结束的并行块内部
                        && FlowStructure.IsPrefix(structure.ScopeOf(n.Id), scope))
            .OrderBy(n => passed[n.Id]);

        return [flow.GetNode(flow.StartNodeId), .. candidates];
    }

    private async Task ReturnCoreAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId, string comment,
        FlowNode target, CancellationToken ct)
    {
        var node = flow.GetNode(task.NodeId);
        if (target.Type == NodeType.Start) instance.Status = InstanceStatus.Returned;
        _tasks.CompleteTask(task, ApprovalTaskStatus.Rejected, userId, comment);
        _events.AddLog(operation, instance, node, ApprovalAction.Return, userId, $"退回到「{(target.Type == NodeType.Start ? "发起人" : target.Name)}」：{comment}");

        // 取消当前节点（含加签）的未完成任务；退出并行块时取消块内所有分支并清除合并计数
        _tasks.CancelOpenTasks(ApprovalTaskCoordinator.CurrentTasksOf(instance, node.Id));
        var structure = operation.StructureOf(flow);
        var depth = structure.ScopeOf(target.Id).Count;
        var scope = structure.ScopeOf(node.Id);
        if (scope.Count > depth)
        {
            var exitedFork = scope[depth].ForkId;
            bool InsideExited(string nodeId) =>
                nodeId == exitedFork || structure.ScopeOf(nodeId) is var s && s.Count > depth && s[depth].ForkId == exitedFork;

            _tasks.CancelOpenTasks(instance.Tasks.Where(t => InsideExited(t.NodeId)));
            var joins = operation.StateOf(instance).Joins;
            foreach (var fork in flow.Nodes.Values.Where(n => n.Type == NodeType.Parallel && InsideExited(n.Id)))
                joins.Remove(structure.JoinOf(fork.Id));
        }

        instance.Round++;
        var toInitiator = target.Type == NodeType.Start;
        if (toInitiator) instance.Status = InstanceStatus.Returned;
        await _events.RaiseAsync(operation, NotifyEvent.Returned, instance, flow, node,
            new EventInfo(toInitiator ? [new Assignee(AssigneeKind.User, instance.InitiatorId)] : [])
            {
                OperatorId = userId,
                Comment = comment,
                ReturnTo = toInitiator ? "发起人" : target.Name
            }, ct, fallback: toInitiator ? NotificationDefaults.ReturnedToInitiatorRule : null);

        if (!toInitiator)
        {
            await MoveToAsync(operation, instance, flow, target.Id, ct);
            return;
        }

        _tasks.AddTask(operation, instance, new ApprovalTask
        {
            Id = _unit.NewId("ApprovalEngine.Collaboration"),
            InstanceId = instance.Id,
            NodeId = target.Id,
            NodeName = target.Name,
            AssigneeKind = AssigneeKind.User,
            AssigneeValue = instance.InitiatorId,
            Kind = ApprovalTaskKind.Resubmit,
            Round = instance.Round,
            Status = ApprovalTaskStatus.Pending,
            CreatedAt = Now
        });
    }

    /// <summary>退回发起人后重新提交；formData 不为空时替换表单数据（业务单据修改后传入最新数据）。</summary>
    public Task ResubmitAsync(Guid taskId, string userId, string? comment = null, object? formData = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => ResubmitAsyncCore(taskId, userId, comment, formData, token), ct);

    private async Task ResubmitAsyncCore(Guid taskId, string userId, string? comment = null, object? formData = null, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskResubmit, new() { TaskId = taskId }, userId, ct);
        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        if (task.Kind != ApprovalTaskKind.Resubmit) throw new ApprovalException("该任务不是待重新提交的申请");
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        await ResubmitCoreAsync(operation, instance, flow, task, userId, comment, formData, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    private async Task ResubmitCoreAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId, string? comment,
        object? formData, CancellationToken ct)
    {
        instance.Status = InstanceStatus.Running;
        if (formData is not null) instance.FormDataJson = SerializeData(formData);
        _tasks.CompleteTask(task, ApprovalTaskStatus.Approved, userId, comment);
        var start = flow.GetNode(flow.StartNodeId);
        _events.AddLog(operation, instance, start, ApprovalAction.Resubmit, userId, comment);
        operation.StateOf(instance).Joins.Clear();
        await _events.RaiseAsync(operation, NotifyEvent.Resubmitted, instance, flow, start,
            new EventInfo([new Assignee(AssigneeKind.User, instance.InitiatorId)]) { OperatorId = userId, Comment = comment }, ct);
        await MoveToAsync(operation, instance, flow, start.Next!, ct);
    }

    // ───────────────────────── 加签 ─────────────────────────

    /// <summary>加签：邀请其他人参与当前节点的审批，加签人必须同意节点才能通过。</summary>
    public Task AddSignAsync(Guid taskId, string userId, IReadOnlyList<string> userIds, AddSignMode mode, string? comment = null,
        CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => AddSignAsyncCore(taskId, userId, userIds, mode, comment, token), ct);

    private async Task AddSignAsyncCore(Guid taskId, string userId, IReadOnlyList<string> userIds, AddSignMode mode, string? comment = null,
        CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskAddSign, new() { TaskId = taskId }, userId, ct);
        var signers = userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct().ToList();
        if (signers.Count == 0) throw new ApprovalException("请选择加签人");
        if (signers.Contains(userId)) throw new ApprovalException("不能给自己加签");
        foreach (var signer in signers) await EnsureActiveUserAsync(signer, ct);
        if (!Enum.IsDefined(mode)) throw new ApprovalException("无效的加签方式");

        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        var node = flow.GetNode(task.NodeId);
        if (task.Kind == ApprovalTaskKind.Resubmit || node.Type != NodeType.Approval) throw new ApprovalException("该任务不支持加签");
        if (!node.AllowAddSign) throw new ApprovalException("该节点不允许加签");
        if (mode == AddSignMode.After && node.RequireComment && string.IsNullOrWhiteSpace(comment))
            throw new ApprovalException("该节点必须填写审批意见");
        if (node.RequireQualifiedAssignee)
        {
            var qualified = await _assignees.GetEligibleApproversAsync(instance, node, ct);
            if (signers.Any(id => !qualified.Contains(id)))
                throw new ApprovalException("加签人不具备本节点审批资格") { Code = ApprovalErrorCode.Forbidden };
        }
        if (!_options.AllowInitiatorApprove && signers.Contains(instance.InitiatorId)) throw new ApprovalException("不能给发起人加签");

        var nodeTasks = ApprovalTaskCoordinator.CurrentTasksOf(instance, node.Id);
        var duplicated = signers.Where(u => ApprovalTaskCoordinator.OpenTasks(nodeTasks).Any(t => t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == u)).ToList();
        if (duplicated.Count > 0) throw new ApprovalException($"{string.Join("、", duplicated)} 已是该节点的审批人");

        var (kind, label) = mode switch
        {
            AddSignMode.Before => (ApprovalTaskKind.AddSignBefore, "前加签"),
            AddSignMode.After => (ApprovalTaskKind.AddSignAfter, "后加签"),
            _ => (ApprovalTaskKind.AddSignParallel, "并加签")
        };

        switch (mode)
        {
            case AddSignMode.Before:
                // 挂起自己的任务，加签人都同意后恢复
                task.Status = ApprovalTaskStatus.Suspended;
                task.NextRemindAt = null;
                break;

            case AddSignMode.After:
                _tasks.CompleteTask(task, ApprovalTaskStatus.Approved, userId, comment);
                break;
        }
        _events.AddLog(operation, instance, node, ApprovalAction.AddSign, userId,
            $"{label}：{string.Join("、", signers)}" + (string.IsNullOrWhiteSpace(comment) ? "" : $"，{comment}"));

        var dueAt = _tasks.DueAtFor(node);
        foreach (var signer in signers)
        {
            _tasks.AddTask(operation, instance, new ApprovalTask
            {
                Id = _unit.NewId("ApprovalEngine.Collaboration"),
                InstanceId = instance.Id,
                NodeId = node.Id,
                NodeName = node.Name,
                AssigneeKind = AssigneeKind.User,
                AssigneeValue = signer,
                Kind = kind,
                ParentTaskId = task.Id,
                Round = task.Round,
                Order = task.Order,
                Status = ApprovalTaskStatus.Pending,
                CreatedAt = Now,
                DueAt = dueAt,
                NextRemindAt = dueAt
            });
        }

        var assignees = signers.Select(u => new Assignee(AssigneeKind.User, u)).ToList();
        await _events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo(assignees) { DueAt = dueAt }, ct);
        if (mode == AddSignMode.After)
            await _events.RaiseAsync(operation, NotifyEvent.Approved, instance, flow, node,
                new EventInfo(ApprovalTaskCoordinator.PendingAssignees(ApprovalTaskCoordinator.CurrentTasksOf(instance, node.Id))) { OperatorId = userId, Comment = comment }, ct);

        await FlushOperationAsync(operation, instance, ct);
    }
}
