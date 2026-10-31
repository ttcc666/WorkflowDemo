using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>对接宿主系统的用户/角色体系（必须由宿主实现并注册）。</summary>
public interface IApprovalIdentityProvider
{
    Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct);

    Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct);

    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct);
}

public sealed record Assignee(AssigneeKind Kind, string Value);

public sealed class ApproverResolveContext
{
    public required ApprovalInstance Instance { get; init; }
    public required FlowNode Node { get; init; }
    public required IReadOnlyDictionary<string, object?> FormData { get; init; }
}

/// <summary>
/// 审批人解析器，按 <see cref="Type"/> 与流程定义中的 <see cref="ApproverRule.Type"/> 匹配。
/// 宿主可注册自定义解析器，如“发起人部门负责人”“发起人第 N 级上级”。
/// </summary>
public interface IApproverResolver
{
    string Type { get; }

    Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct);
}

/// <summary>
/// 业务回调：流程结束时更新业务单据。回调在引擎 SaveChanges 之前执行，
/// 与审批状态写入同一个 DbContext / 事务，保证两者一致；回调抛异常会使整个操作回滚。
/// </summary>
public interface IApprovalBusinessHandler
{
    string BusinessType { get; }

    Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct);

    Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct);

    Task OnWithdrawnAsync(ApprovalInstance instance, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>条件分支表达式求值。</summary>
public interface IConditionEvaluator
{
    bool Evaluate(string expression, IReadOnlyDictionary<string, object?> variables);
}

public enum NotificationKind { TaskCreated, Cc, Completed }

public sealed record ApprovalNotification(
    NotificationKind Kind,
    Guid InstanceId,
    string BusinessType,
    string BusinessKey,
    string? Title,
    InstanceStatus Status,
    IReadOnlyList<Assignee> Recipients);

/// <summary>
/// 消息通知（站内信、邮件、企业微信等），可选。
/// 在事务提交成功后调用，失败只记录日志、不影响审批结果。
/// </summary>
public interface IApprovalNotifier
{
    Task NotifyAsync(ApprovalNotification notification, CancellationToken ct);
}
