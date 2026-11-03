using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine.Runtime;

/// <summary>审批任务的创建、状态流转、有效轮次与转交关系；流程推进由导航器负责。</summary>
internal sealed class ApprovalTaskCoordinator(DbContext db, ApprovalUnitOfWork unit, ApprovalEventWriter events, TimeProvider clock)
{
    private const string SystemOperator = ApprovalEngine.SystemOperator;
    private readonly DbContext _db = db;
    private readonly ApprovalUnitOfWork _unit = unit;
    private readonly ApprovalEventWriter _events = events;
    private readonly TimeProvider _clock = clock;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;


    /// <summary>
    /// 把任务交给另一位用户（转交、改派）：原任务标记为已转交，生成新任务；保留状态、时限与提醒进度（避免通过转交拖延），
    /// 加签关系跟随新任务。
    /// </summary>
    internal ApprovalTask HandOver(ApprovalOperationContext operation, ApprovalInstance instance, ApprovalTask task, string toUserId, string handledBy, string? comment)
    {
        var status = task.Status;
        var nextRemindAt = task.NextRemindAt;
        CompleteTask(task, ApprovalTaskStatus.Transferred, handledBy, comment);

        var handed = new ApprovalTask
        {
            Id = _unit.NewId("ApprovalEngine.Admin"),
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
        AddTask(operation, instance, handed);
        foreach (var child in instance.Tasks.Where(t => t.ParentTaskId == task.Id)) child.ParentTaskId = handed.Id;
        return handed;
    }

    /// <summary>parentId 下指定类型的加签任务是否都已同意。</summary>
    internal static bool AddSignChildrenDone(List<ApprovalTask> nodeTasks, Guid? parentId, ApprovalTaskKind kind) =>
        parentId is not null && nodeTasks.Where(t => t.ParentTaskId == parentId && t.Kind == kind)
            .All(t => t.Status == ApprovalTaskStatus.Approved);

    /// <summary>前加签的加签人都同意后，恢复原任务为待处理并通知原审批人。</summary>
    internal async Task ResumeAfterBeforeSignAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, FlowNode node, List<ApprovalTask> nodeTasks,
        Guid? parentId, CancellationToken ct)
    {
        var parent = nodeTasks.FirstOrDefault(t => t.Id == parentId);
        if (parent is not { Status: ApprovalTaskStatus.Suspended } || !AddSignChildrenDone(nodeTasks, parentId, ApprovalTaskKind.AddSignBefore))
            return;

        parent.Status = ApprovalTaskStatus.Pending;
        _events.WriteEvent(operation, instance, "TaskCreated", parent.NodeId, parent.NodeName, parent.Id, SystemOperator, null);
        parent.DueAt = parent.NextRemindAt = DueAtFor(node);
        var assignee = new Assignee(parent.AssigneeKind, parent.AssigneeValue);
        await _events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo([assignee]) { DueAt = parent.DueAt }, ct);
    }

    /// <summary>
    /// 节点是否通过：主任务满足审批方式（或签任一同意 / 会签与依次全部同意），且没有未完成的后加签、并加签任务。
    /// 前加签任务不阻塞：或签时其他人已同意，被挂起的任务及其加签任务一并取消。
    /// </summary>
    internal static bool IsNodeFinished(FlowNode node, List<ApprovalTask> nodeTasks)
    {
        var primary = nodeTasks.Where(t => t.Kind == ApprovalTaskKind.Approval).ToList();
        var modeSatisfied = node.Mode == ApproveMode.Any
            ? primary.Any(t => t.Status == ApprovalTaskStatus.Approved)
            : primary.Count > 0 && primary.All(t => t.Status == ApprovalTaskStatus.Approved);
        var blocked = nodeTasks.Any(t => t.Kind is ApprovalTaskKind.AddSignAfter or ApprovalTaskKind.AddSignParallel && IsOpen(t));
        return modeSatisfied && !blocked;
    }

    internal async Task CreateTasksAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, FlowNode node, IReadOnlyList<Assignee> assignees,
        CancellationToken ct)
    {
        var dueAt = DueAtFor(node);
        for (var i = 0; i < assignees.Count; i++)
        {
            // 依次审批只激活第一个人，后续的人轮到时才开始计时
            var pending = node.Mode != ApproveMode.Sequential || i == 0;
            AddTask(operation, instance, new ApprovalTask
            {
                Id = _unit.NewId("ApprovalEngine"),
                InstanceId = instance.Id,
                NodeId = node.Id,
                NodeName = node.Name,
                AssigneeKind = assignees[i].Kind,
                AssigneeValue = assignees[i].Value,
                Kind = ApprovalTaskKind.Approval,
                Round = instance.Round,
                Order = i,
                Status = pending ? ApprovalTaskStatus.Pending : ApprovalTaskStatus.Waiting,
                CreatedAt = Now,
                DueAt = pending ? dueAt : null,
                NextRemindAt = pending ? dueAt : null
            });
        }

        var notified = node.Mode == ApproveMode.Sequential ? assignees.Take(1).ToList() : assignees.ToList();
        await _events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo(notified) { DueAt = dueAt }, ct);
    }

    internal async Task<bool> ActivateNextSequentialAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, FlowNode node,
        List<ApprovalTask> nodeTasks, CancellationToken ct)
    {
        var next = nodeTasks.Where(t => t.Kind == ApprovalTaskKind.Approval && t.Status == ApprovalTaskStatus.Waiting).MinBy(t => t.Order);
        if (next is null) return false;

        next.Status = ApprovalTaskStatus.Pending;
        next.DueAt = next.NextRemindAt = DueAtFor(node);
        _events.WriteEvent(operation, instance, "TaskCreated", next.NodeId, next.NodeName, next.Id, SystemOperator, null);
        var assignee = new Assignee(next.AssigneeKind, next.AssigneeValue);
        await _events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo([assignee]) { DueAt = next.DueAt }, ct);
        return true;
    }

    internal DateTime? DueAtFor(FlowNode node) => node.Timeout is { Minutes: > 0 } timeout ? Now.AddMinutes(timeout.Minutes) : null;

    internal static bool IsOpen(ApprovalTask t) =>
        t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended;

    internal static IEnumerable<ApprovalTask> OpenTasks(IEnumerable<ApprovalTask> tasks) => tasks.Where(IsOpen);

    internal static IEnumerable<ApprovalTask> OpenTasks(ApprovalInstance instance) => OpenTasks(instance.Tasks);

    /// <summary>节点最近一轮（退回后重新激活的那一轮）的有效任务，不含已取消、已转交的。</summary>
    internal static List<ApprovalTask> CurrentTasksOf(ApprovalInstance instance, string nodeId)
    {
        var tasks = instance.Tasks.Where(t => t.NodeId == nodeId).ToList();
        if (tasks.Count == 0) return tasks;
        var round = tasks.Max(t => t.Round);
        return tasks
            .Where(t => t.Round == round && t.Status is not (ApprovalTaskStatus.Canceled or ApprovalTaskStatus.Transferred))
            .ToList();
    }

    internal static List<Assignee> PendingAssignees(IEnumerable<ApprovalTask> tasks) =>
        tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).Select(t => new Assignee(t.AssigneeKind, t.AssigneeValue)).ToList();

    internal void CompleteTask(ApprovalTask task, ApprovalTaskStatus status, string userId, string? comment)
    {
        task.Status = status;
        task.HandledBy = userId;
        task.Comment = comment;
        task.CompletedAt = Now;
        task.NextRemindAt = null;
    }

    internal void CancelOpenTasks(IEnumerable<ApprovalTask> tasks)
    {
        foreach (var t in OpenTasks(tasks).ToList())
        {
            t.Status = ApprovalTaskStatus.Canceled;
            t.CompletedAt = Now;
            t.NextRemindAt = null;
        }
    }

    // 显式 Add：已赋值 Guid 主键的新实体若仅通过导航集合被发现，EF 会误判为已存在而生成 UPDATE
    internal void AddTask(ApprovalOperationContext operation, ApprovalInstance instance, ApprovalTask task)
    {
        _db.Set<ApprovalTask>().Add(task);
        // EF relationship fixup 可能已经加入导航集合，避免同一任务在内存中重复出现。
        if (!instance.Tasks.Contains(task)) instance.Tasks.Add(task);
        if (task.Status == ApprovalTaskStatus.Pending)
            _events.WriteEvent(operation, instance, "TaskCreated", task.NodeId, task.NodeName, task.Id, SystemOperator, null);
    }
}
