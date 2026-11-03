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

public sealed record ApprovalUser(string Id, string Name, IReadOnlyList<string> Roles)
{
    public bool IsActive { get; init; } = true;
}

/// <summary>可信用户状态由宿主返回；未注册时使用已注册的用户目录。</summary>
public interface IApprovalUserStatusProvider
{
    Task<bool> IsActiveAsync(string userId, CancellationToken ct);
}

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

/// <summary>审批人规则参数的类型，设计器据此生成参数控件。</summary>
public enum ApproverValueKind
{
    /// <summary>无参数（如发起人）。</summary>
    None,
    /// <summary>用户 ID，逗号分隔。</summary>
    User,
    /// <summary>角色名，逗号分隔。</summary>
    Role,
    /// <summary>表单人员字段（User / Users）的标识。</summary>
    FormUserField,
    /// <summary>级数 N（1–10），可写 <c>N@字段</c> 表示以表单人员字段中的人为基准（默认以发起人为基准）。</summary>
    Level,
    /// <summary>自由文本（自定义解析器的默认值）。</summary>
    Text
}

/// <summary>审批人类型描述：显示名、参数类型与说明。</summary>
/// <param name="Type">与 <see cref="IApproverResolver.Type"/> 一致。</param>
public sealed record ApproverTypeDescriptor(string Type, string DisplayName, ApproverValueKind ValueKind)
{
    public string? Help { get; init; }

    /// <summary>参数的可选值（Text 类型时显示为下拉）。</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>返回按顺序审批的人员列表（如逐级审批），审批节点必须为依次审批（发布时校验）。</summary>
    public bool Ordered { get; init; }
}

/// <summary>
/// 审批人解析器，按 <see cref="Type"/> 与流程定义中的 <see cref="ApproverRule.Type"/> 匹配。
/// 宿主可注册自定义解析器，如“项目经理”“合同归口部门负责人”；组织架构类（部门负责人、上级）见 <see cref="IApprovalOrganizationProvider"/>。
/// </summary>
public interface IApproverResolver
{
    string Type { get; }

    /// <summary>设计器中的显示名与参数控件，默认为文本参数。</summary>
    ApproverTypeDescriptor Descriptor => new(Type, Type, ApproverValueKind.Text);

    Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct);
}

public sealed record ApprovalDepartment(string Id, string Name, string? ParentId);

/// <summary>
/// 组织架构（可选）：对接 HR / 钉钉 / 企业微信的部门与汇报关系。注册后（<c>AddOrganizationProvider&lt;T&gt;()</c>）
/// 可使用审批人类型 DeptLeader（部门负责人）、Manager（第 N 级上级）、ManagerChain（逐级审批）。
/// </summary>
public interface IApprovalOrganizationProvider
{
    /// <summary>用户所在（主）部门；不属于任何部门时返回 null。</summary>
    Task<ApprovalDepartment?> GetDepartmentAsync(string userId, CancellationToken ct);

    /// <summary>部门负责人（可多人，按或签 / 会签由节点审批方式决定）。</summary>
    Task<IReadOnlyList<string>> GetDepartmentLeadersAsync(string departmentId, CancellationToken ct);

    /// <summary>上级部门；顶级部门返回 null。</summary>
    Task<ApprovalDepartment?> GetParentDepartmentAsync(string departmentId, CancellationToken ct);

    /// <summary>直属上级；没有时返回 null。</summary>
    Task<string?> GetManagerAsync(string userId, CancellationToken ct);
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
