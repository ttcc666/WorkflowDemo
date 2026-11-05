using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Runtime;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Engine;

/// <summary>批量改派中被跳过的待办及原因。</summary>
public sealed record ReassignSkip(Guid TaskId, Guid InstanceId, string? Title, string NodeName, string Reason);

/// <summary>批量改派结果：成功改派的待办数与跳过的待办。</summary>
public sealed record ReassignResult(int Count, IReadOnlyList<ReassignSkip> Skipped);

// 管理操作同样经过 Core 权限门禁；批量改派逐项校验资源范围。
public sealed partial class ApprovalEngine
{
    /// <summary>终止进行中的流程：取消全部待办，状态为 Terminated，调用业务回调 OnTerminatedAsync（默认按驳回处理），通知申请人。</summary>
    public Task TerminateAsync(Guid instanceId, string operatorId, string reason, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => TerminateAsyncCore(instanceId, operatorId, reason, token), ct);

    private async Task TerminateAsyncCore(Guid instanceId, string operatorId, string reason, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceTerminate, new() { InstanceId = instanceId }, operatorId, ct);
        if (string.IsNullOrWhiteSpace(reason)) throw new ApprovalException("终止流程必须填写原因");

        var instance = await _db.Set<ApprovalInstance>()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new ApprovalException("流程不存在") { Code = ApprovalErrorCode.NotFound };
        if (instance.Status is not (InstanceStatus.Running or InstanceStatus.Returned)) throw new ApprovalException("流程已结束，无法终止");

        var flow = await LoadFlowAsync(operation, instance, ct);
        var node = ApprovalTaskCoordinator.OpenTasks(instance).Select(t => flow.Nodes.GetValueOrDefault(t.NodeId)).FirstOrDefault(n => n is not null);
        Touch(instance);
        _events.AddLog(operation, instance, node, ApprovalAction.Terminate, operatorId, reason);
        _tasks.CancelOpenTasks(instance.Tasks);
        await FinishAsync(operation, instance, flow, node, InstanceStatus.Terminated, new EventInfo([]) { OperatorId = operatorId, Comment = reason }, ct);

        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>
    /// 管理员把待办改派给指定用户（处理人长期不在、离职）：不校验当前处理人；角色任务可改派给具体用户；
    /// 依次审批中尚未轮到、等待加签的任务也可改派（状态不变）；不重置时限。
    /// </summary>
    public Task ReassignAsync(Guid taskId, string operatorId, string toUserId, string? reason = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => ReassignAsyncCore(taskId, operatorId, toUserId, reason, token), ct);

    private async Task ReassignAsyncCore(Guid taskId, string operatorId, string toUserId, string? reason = null, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReassign, new() { TaskId = taskId }, operatorId, ct);
        toUserId = toUserId?.Trim() ?? "";
        await EnsureActiveUserAsync(toUserId, ct);
        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        if (await ReassignErrorAsync(instance, task, flow, toUserId, reason, ct) is { } error) throw new ApprovalException(error);

        await ReassignCoreAsync(operation, instance, flow, task, operatorId, toUserId, reason, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>
    /// 离职交接：把某人的全部待办（指派给本人的任务，不含角色任务）改派给另一人，逐个流程提交。
    /// 不能改派的待办（对方是发起人且不允许自审、对方已是该节点审批人、并发冲突）跳过并在结果中说明。
    /// 每个实例使用独立事务与作用域；拒绝加入外层原子工作单元，不清空宿主跟踪器。
    /// 必须提供稳定操作键，用于恢复候选清单、逐实例结果及最终汇总。
    /// </summary>
    public Task<ReassignResult> ReassignUserAsync(string fromUserId, string toUserId, string operatorId, string? reason,
        string operationId, CancellationToken ct = default) => ReassignUserCommandAsync(fromUserId, toUserId, operatorId, reason, operationId, ct);

    private async Task<ReassignResult> ReassignUserCommandAsync(string fromUserId, string toUserId, string operatorId, string? reason,
        string operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 200 || operationId.Any(char.IsControl))
            throw new ApprovalException("批量改派需要稳定 OperationId") { Code = ApprovalErrorCode.OperationIdRequired };
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReassignBulk, new() { TargetUserId = fromUserId }, operatorId, ct);
        toUserId = toUserId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(fromUserId) || toUserId.Length == 0 || fromUserId == toUserId)
            throw new ApprovalException("改派对象无效");

        if (_unit.IsActive || _db.Database.CurrentTransaction is not null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("批量改派逐实例提交，不能加入外层原子工作单元或带未提交修改的作用域");
        _unit.Writes.Validate();
        var commands = _commands;
        var payload = new { fromUserId, toUserId, reason };
        var resource = new ApprovalResource { TargetUserId = fromUserId };
        var finalCommand = new ApprovalCommand("task.reassign-bulk.result", fromUserId, operationId, payload, ApprovalPermissions.TaskReassignBulk, resource);
        var lookup = ApprovalCommandHash.Of(operatorId, finalCommand.Kind, finalCommand.Scope, operationId);
        if (await _db.Set<ApprovalOperation>().AsNoTracking().AnyAsync(o => o.LookupHash == lookup, ct))
        {
            var result = await commands.ExecuteAsync<ReassignResult>(finalCommand, (_, _) => throw new InvalidOperationException("已提交汇总不能重新执行"), ct);
            var candidates = await commands.ExecuteAsync<List<Guid>>(new("task.reassign-bulk.plan", fromUserId, operationId, payload,
                ApprovalPermissions.TaskReassignBulk, resource), (_, _) => throw new InvalidOperationException("已提交汇总必须有原候选计划"), ct);
            await ValidateBulkResultReadAccessAsync(candidates, operatorId, ct);
            return result;
        }
        var instanceIds = await commands.ExecuteAsync(new("task.reassign-bulk.plan", fromUserId, operationId, payload, ApprovalPermissions.TaskReassignBulk, resource),
                (sp, token) => sp.GetRequiredService<ApprovalEngine>().ReassignCandidatesAsync(fromUserId, token), ct);
        var count = 0; var skipped = new List<ReassignSkip>();
        foreach (var instanceId in instanceIds)
        {
            try
            {
                var result = await commands.ExecuteAsync(new("task.reassign-bulk.item", instanceId.ToString(), operationId,
                    payload, ApprovalPermissions.TaskReassignBulk,
                    new() { InstanceId = instanceId, TargetUserId = fromUserId }),
                    (sp, token) => sp.GetRequiredService<ApprovalEngine>().ReassignBatchInstanceAsync(instanceId, fromUserId, toUserId, operatorId, reason, token), ct);
                count += result.Count; skipped.AddRange(result.Skipped);
            }
            catch (ApprovalException ex) when (ex.Code == ApprovalErrorCode.Conflict)
            {
                var tasks = await (await ReassignTasksAsync(fromUserId, ct)).Where(t => t.InstanceId == instanceId).ToListAsync(ct);
                skipped.AddRange(tasks.Select(t => new ReassignSkip(t.Id, instanceId, null, t.NodeName, ex.Message)));
            }
        }
        var summary = new ReassignResult(count, skipped);
        var finalResult = await commands.ExecuteAsync(finalCommand, (_, _) => Task.FromResult(summary), ct);
        // ExecuteAsync may replay a concurrently committed summary even when the initial lookup was empty.
        await ValidateBulkResultReadAccessAsync(instanceIds, operatorId, ct);
        return finalResult;
    }

    private async Task ValidateBulkResultReadAccessAsync(IReadOnlyList<Guid> instanceIds, string operatorId, CancellationToken ct)
    {
        if (_contextAccessor.IsSystem) return;
        foreach (var id in instanceIds)
        {
            await _authorization.RequireAsync(ApprovalPermissions.InstanceRead, new() { InstanceId = id }, operatorId, ct);
            var instance = await _db.Set<ApprovalInstance>().AsNoTracking().SingleAsync(i => i.Id == id, ct);
            if (!(await _accessPolicy().GetAccessAsync(new(operatorId, false), ApprovalContextSnapshots.Instance(instance), ct)).CanView)
                throw new ApprovalException("无权读取原操作结果") { Code = ApprovalErrorCode.NotFound };
        }
    }

    private async Task<List<Guid>> ReassignCandidatesAsync(string fromUserId, CancellationToken ct)
        => await (await ReassignTasksAsync(fromUserId, ct)).Select(t => t.InstanceId).Distinct().ToListAsync(ct);

    private async Task<IQueryable<ApprovalTask>> ReassignTasksAsync(string fromUserId, CancellationToken ct)
    {
        var scoped = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        return from t in _db.Set<ApprovalTask>().AsNoTracking()
                      join i in scoped on t.InstanceId equals i.Id
                      where t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == fromUserId
                          && (t.Status == ApprovalTaskStatus.Pending || t.Status == ApprovalTaskStatus.Waiting || t.Status == ApprovalTaskStatus.Suspended)
                          && (i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Returned)
                      orderby i.CreatedAt, i.Id
                      select t;
    }

    private async Task<ReassignResult> ReassignBatchInstanceAsync(Guid instanceId, string fromUserId, string toUserId, string operatorId, string? reason, CancellationToken ct)
    {
        var operation = new ApprovalOperationContext();
        using var authority = _unit.Writes.Enter(ApprovalWriteOwner.Engine);
        var instance = await _db.Set<ApprovalInstance>().Include(i => i.Tasks).FirstAsync(i => i.Id == instanceId, ct);
        var flow = await LoadFlowAsync(operation, instance, ct); var skipped = new List<ReassignSkip>(); var count = 0;
        foreach (var task in instance.Tasks.Where(t => t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == fromUserId && ApprovalTaskCoordinator.IsOpen(t)).OrderBy(t => t.CreatedAt).ToList())
        {
            var error = !await _authorization.CanAsync(ApprovalPermissions.TaskReassign, new() { TaskId = task.Id }, operatorId, ct)
                ? "没有改派此任务的权限" : await ReassignErrorAsync(instance, task, flow, toUserId, reason, ct);
            if (error is not null) { skipped.Add(new(task.Id, instance.Id, instance.Title, task.NodeName, error)); continue; }
            await ReassignCoreAsync(operation, instance, flow, task, operatorId, toUserId, reason, ct); count++;
        }
        if (count > 0) { Touch(instance); await FlushOperationAsync(operation, instance, ct); }
        return new(count, skipped);
    }

    /// <summary>能否改派，不能时返回原因。</summary>
    private async Task<string?> ReassignErrorAsync(ApprovalInstance instance, ApprovalTask task, FlowDefinition flow,
        string toUserId, string? reason, CancellationToken ct)
    {
        if (instance.Status is not (InstanceStatus.Running or InstanceStatus.Returned)) return "流程已结束";
        if (!ApprovalTaskCoordinator.IsOpen(task)) return "任务已处理";
        if (task.Kind == ApprovalTaskKind.Resubmit) return "退回发起人的申请不能改派";
        if (toUserId.Length == 0) return "请选择改派对象";
        if (task.AssigneeKind == AssigneeKind.User && task.AssigneeValue == toUserId) return "对方已是该任务的处理人";
        if (!_options.AllowInitiatorApprove && toUserId == instance.InitiatorId) return "不能改派给发起人（不允许审批自己的申请）";
        if (ApprovalTaskCoordinator.OpenTasks(ApprovalTaskCoordinator.CurrentTasksOf(instance, task.NodeId))
            .Any(t => t.Id != task.Id && t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == toUserId))
            return "对方已是该节点的审批人";
        var node = flow.GetNode(task.NodeId);
        if (node.RequireQualifiedAssignee)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "岗位改派必须填写原因";
            if (!(await _assignees.GetEligibleApproversAsync(instance, node, ct)).Contains(toUserId)) return "目标用户不存在或不具备本节点审批资格";
        }
        return null;
    }

    private async Task ReassignCoreAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string operatorId, string toUserId,
        string? reason, CancellationToken ct)
    {
        await EnsureActiveUserAsync(toUserId, ct);
        operation.TaskId = task.Id;
        var from = ApprovalEventWriter.Describe(new Assignee(task.AssigneeKind, task.AssigneeValue));
        var handed = _tasks.HandOver(operation, instance, task, toUserId, operatorId, reason);
        _events.AddLog(operation, instance, ApprovalAction.Reassign, operatorId,
            $"改派：{from} → {toUserId}" + (string.IsNullOrWhiteSpace(reason) ? "" : $"，{reason}"), task.NodeId, task.NodeName);

        // 尚未轮到 / 等待加签的任务在激活时再通知
        if (handed.Status != ApprovalTaskStatus.Pending) return;
        var target = new Assignee(AssigneeKind.User, toUserId);
        await _events.RaiseAsync(operation, NotifyEvent.Transferred, instance, flow, flow.GetNode(task.NodeId),
            new EventInfo([target]) { OperatorId = operatorId, Comment = reason, TransferTo = toUserId, DueAt = handed.DueAt }, ct);
    }
}
