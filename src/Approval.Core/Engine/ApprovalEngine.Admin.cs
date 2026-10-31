using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>批量改派中被跳过的待办及原因。</summary>
public sealed record ReassignSkip(Guid TaskId, Guid InstanceId, string? Title, string NodeName, string Reason);

/// <summary>批量改派结果：成功改派的待办数与跳过的待办。</summary>
public sealed record ReassignResult(int Count, IReadOnlyList<ReassignSkip> Skipped);

// 管理操作：终止流程、改派待办、离职交接（批量改派）。是否管理员由调用方（接口层）判断，这里只记录操作人
public sealed partial class ApprovalEngine
{
    /// <summary>终止进行中的流程：取消全部待办，状态为 Terminated，调用业务回调 OnTerminatedAsync（默认按驳回处理），通知申请人。</summary>
    public async Task TerminateAsync(Guid instanceId, string operatorId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ApprovalException("终止流程必须填写原因");

        var instance = await _db.Set<ApprovalInstance>()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new ApprovalException("流程不存在") { Code = ApprovalErrorCode.NotFound };
        if (instance.Status != InstanceStatus.Running) throw new ApprovalException("流程已结束，无法终止");

        var flow = await LoadFlowAsync(instance, ct);
        var node = OpenTasks(instance).Select(t => flow.Nodes.GetValueOrDefault(t.NodeId)).FirstOrDefault(n => n is not null);
        Touch(instance);
        AddLog(instance, node, ApprovalAction.Terminate, operatorId, reason);
        CancelOpenTasks(instance.Tasks);
        await FinishAsync(instance, flow, node, InstanceStatus.Terminated, new EventInfo([]) { OperatorId = operatorId, Comment = reason }, ct);

        await CommitAsync(instance, ct);
    }

    /// <summary>
    /// 管理员把待办改派给指定用户（处理人长期不在、离职）：不校验当前处理人；角色任务可改派给具体用户；
    /// 依次审批中尚未轮到、等待加签的任务也可改派（状态不变）；不重置时限。
    /// </summary>
    public async Task ReassignAsync(Guid taskId, string operatorId, string toUserId, string? reason = null, CancellationToken ct = default)
    {
        toUserId = toUserId?.Trim() ?? "";
        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        if (ReassignError(instance, task, toUserId) is { } error) throw new ApprovalException(error);

        await ReassignCoreAsync(instance, flow, task, operatorId, toUserId, reason, ct);
        await CommitAsync(instance, ct);
    }

    /// <summary>
    /// 离职交接：把某人的全部待办（指派给本人的任务，不含角色任务）改派给另一人，逐个流程提交。
    /// 不能改派的待办（对方是发起人且不允许自审、对方已是该节点审批人、并发冲突）跳过并在结果中说明。
    /// 处理过程中会清空 DbContext 的跟踪状态，请在独立的请求 / 作用域中调用。
    /// </summary>
    public async Task<ReassignResult> ReassignUserAsync(string fromUserId, string toUserId, string operatorId, string? reason = null,
        CancellationToken ct = default)
    {
        toUserId = toUserId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(fromUserId) || toUserId.Length == 0 || fromUserId == toUserId)
            throw new ApprovalException("改派对象无效");

        var instanceIds = await (
                from t in _db.Set<ApprovalTask>().AsNoTracking()
                join i in _db.Set<ApprovalInstance>().AsNoTracking() on t.InstanceId equals i.Id
                where t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == fromUserId
                      && (t.Status == ApprovalTaskStatus.Pending || t.Status == ApprovalTaskStatus.Waiting || t.Status == ApprovalTaskStatus.Suspended)
                      && i.Status == InstanceStatus.Running
                orderby i.CreatedAt
                select i.Id)
            .Distinct()
            .ToListAsync(ct);

        var count = 0;
        var skipped = new List<ReassignSkip>();
        foreach (var instanceId in instanceIds)
        {
            _db.ChangeTracker.Clear();
            var instance = await _db.Set<ApprovalInstance>().Include(i => i.Tasks).FirstAsync(i => i.Id == instanceId, ct);
            var flow = await LoadFlowAsync(instance, ct);
            var tasks = instance.Tasks
                .Where(t => t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == fromUserId && IsOpen(t))
                .OrderBy(t => t.CreatedAt)
                .ToList();

            var reassigned = new List<ApprovalTask>();
            foreach (var task in tasks)
            {
                if (ReassignError(instance, task, toUserId) is { } error)
                {
                    skipped.Add(new ReassignSkip(task.Id, instance.Id, instance.Title, task.NodeName, error));
                    continue;
                }
                await ReassignCoreAsync(instance, flow, task, operatorId, toUserId, reason, ct);
                reassigned.Add(task);
            }
            if (reassigned.Count == 0) continue;

            Touch(instance);
            try
            {
                await CommitAsync(instance, ct);
                count += reassigned.Count;
            }
            catch (ApprovalException ex) when (ex.Code == ApprovalErrorCode.Conflict)
            {
                // 与其他操作同时修改了该流程：本流程不改派，可稍后重试
                skipped.AddRange(reassigned.Select(t => new ReassignSkip(t.Id, instance.Id, instance.Title, t.NodeName, ex.Message)));
            }
        }
        _db.ChangeTracker.Clear();
        return new ReassignResult(count, skipped);
    }

    /// <summary>能否改派，不能时返回原因。</summary>
    private string? ReassignError(ApprovalInstance instance, ApprovalTask task, string toUserId)
    {
        if (instance.Status != InstanceStatus.Running) return "流程已结束";
        if (!IsOpen(task)) return "任务已处理";
        if (task.Kind == ApprovalTaskKind.Resubmit) return "退回发起人的申请不能改派";
        if (toUserId.Length == 0) return "请选择改派对象";
        if (task.AssigneeKind == AssigneeKind.User && task.AssigneeValue == toUserId) return "对方已是该任务的处理人";
        if (!_options.AllowInitiatorApprove && toUserId == instance.InitiatorId) return "不能改派给发起人（不允许审批自己的申请）";
        if (OpenTasks(CurrentTasksOf(instance, task.NodeId))
            .Any(t => t.Id != task.Id && t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == toUserId))
            return "对方已是该节点的审批人";
        return null;
    }

    private async Task ReassignCoreAsync(ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string operatorId, string toUserId,
        string? reason, CancellationToken ct)
    {
        var from = Describe(new Assignee(task.AssigneeKind, task.AssigneeValue));
        var handed = HandOver(instance, task, toUserId, operatorId, reason);
        AddLog(instance, ApprovalAction.Reassign, operatorId,
            $"改派：{from} → {toUserId}" + (string.IsNullOrWhiteSpace(reason) ? "" : $"，{reason}"), task.NodeId, task.NodeName);

        // 尚未轮到 / 等待加签的任务在激活时再通知
        if (handed.Status != ApprovalTaskStatus.Pending) return;
        var target = new Assignee(AssigneeKind.User, toUserId);
        Notify(NotificationKind.TaskCreated, instance, [target]);
        await RaiseAsync(NotifyEvent.Transferred, instance, flow, flow.GetNode(task.NodeId),
            new EventInfo([target]) { OperatorId = operatorId, Comment = reason, TransferTo = toUserId, DueAt = handed.DueAt }, ct);
    }

    /// <summary>
    /// 把任务交给另一位用户（转交、改派）：原任务标记为已转交，生成新任务；保留状态、时限与提醒进度（避免通过转交拖延），
    /// 加签关系跟随新任务。
    /// </summary>
    private ApprovalTask HandOver(ApprovalInstance instance, ApprovalTask task, string toUserId, string handledBy, string? comment)
    {
        var status = task.Status;
        var nextRemindAt = task.NextRemindAt;
        CompleteTask(task, ApprovalTaskStatus.Transferred, handledBy, comment);

        var handed = new ApprovalTask
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            NodeId = task.NodeId,
            NodeName = task.NodeName,
            AssigneeKind = AssigneeKind.User,
            AssigneeValue = toUserId,
            Kind = task.Kind,
            ParentTaskId = task.ParentTaskId,
            Round = task.Round,
            Order = task.Order,
            Status = status,
            CreatedAt = Now,
            DueAt = task.DueAt,
            NextRemindAt = nextRemindAt,
            RemindCount = task.RemindCount,
            LastRemindedAt = task.LastRemindedAt
        };
        AddTask(instance, handed);
        foreach (var child in instance.Tasks.Where(t => t.ParentTaskId == task.Id)) child.ParentTaskId = handed.Id;
        return handed;
    }
}
