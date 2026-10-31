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

public sealed record ApprovalUser(string Id, string Name, IReadOnlyList<string> Roles);

/// <summary>
/// 用户目录（可选）：供前端用户选择器（转交、加签、表单人员字段）与设计器角色下拉使用。
/// 未注册时相关接口返回 501，前端可改为手动输入。
/// </summary>
public interface IApprovalUserDirectory
{
    /// <summary>按 ID / 名称模糊搜索；keyword 为空时返回前 take 个用户。</summary>
    Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct);

    Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct);
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

    /// <summary>管理员终止流程。默认按驳回处理（关闭单据、解锁），需要区分时重写。</summary>
    Task OnTerminatedAsync(ApprovalInstance instance, CancellationToken ct) => OnRejectedAsync(instance, ct);
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
