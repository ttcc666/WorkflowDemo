using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;

namespace Approval.Core.Engine;

// 超时：由 ApprovalTimeoutScanner 定时调用，对到期待办提醒 / 催办、自动同意或自动驳回
public sealed partial class ApprovalEngine
{
    /// <summary>
    /// 处理一条到期待办（<see cref="ApprovalTask.NextRemindAt"/> 已到）。返回是否做了处理；
    /// 任务已被处理或尚未到期时返回 false。与用户操作并发时由实例并发令牌保证只有一方成功。
    /// </summary>
    public Task<bool> HandleTimeoutAsync(Guid taskId, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => HandleTimeoutAsyncCore(taskId, token), ct);

    private async Task<bool> HandleTimeoutAsyncCore(Guid taskId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskTimeout, new() { TaskId = taskId }, null, ct);
        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        if (instance.Status != InstanceStatus.Running || task.Status != ApprovalTaskStatus.Pending
            || task.NextRemindAt is not { } due || due > Now)
            return false;

        var node = flow.GetNode(task.NodeId);
        if (node.Timeout is not { } policy)
        {
            // 发起后流程定义不会变化，这里只是防御：没有时限配置则不再处理
            task.NextRemindAt = null;
            await CommitAsync(instance, ct);
            return false;
        }

        var assignee = new Assignee(task.AssigneeKind, task.AssigneeValue);
        var timeoutAction = _options.EnableAutomaticTimeoutDecisions ? policy.Action : TimeoutAction.Remind;
        task.RemindCount++;
        task.LastRemindedAt = Now;
        await RaiseAsync(NotifyEvent.Timeout, instance, flow, node, new EventInfo([assignee]) { DueAt = task.DueAt }, ct,
            fallback: timeoutAction == TimeoutAction.Remind ? NotificationDefaults.TimeoutRule : null);

        switch (timeoutAction)
        {
            case TimeoutAction.AutoApprove:
                await ApproveCoreAsync(instance, flow, task, SystemOperator, "超时自动同意", ApprovalAction.TimeoutApprove, ct);
                break;

            case TimeoutAction.AutoReject:
                await RejectCoreAsync(instance, flow, task, SystemOperator, "超时自动驳回", ApprovalAction.TimeoutReject, ct);
                break;

            default:
                var repeat = policy.RepeatMinutes is > 0 && task.RemindCount < Math.Max(1, policy.MaxReminders);
                task.NextRemindAt = repeat ? Now.AddMinutes(policy.RepeatMinutes!.Value) : null;
                AddLog(instance, node, ApprovalAction.Remind, SystemOperator,
                    $"超时提醒（第 {task.RemindCount} 次）：{Describe(assignee)}" + (repeat ? "" : "，不再提醒"));
                break;
        }

        await CommitAsync(instance, ct);
        return true;
    }
}
