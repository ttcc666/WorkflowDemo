using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>查看审批的人：用户 ID 与是否管理员（管理员由接口层判断，如 ASP.NET Core 授权策略）。</summary>
public sealed record ApprovalViewer(string UserId, bool IsAdmin);

public enum ApprovalAccessLevel
{
    /// <summary>无权查看（接口返回 404，不暴露实例是否存在）。</summary>
    None,
    /// <summary>参与人（处理人、候选人、抄送人）：受节点字段隐藏规则约束。</summary>
    Participant,
    /// <summary>发起人、管理员或宿主授权：查看全部内容。</summary>
    Full
}

/// <param name="VisibleViaNodes">参与人可见时，查看者作为处理人 / 候选人 / 抄送人涉及的节点（用于字段隐藏规则）。</param>
public sealed record ApprovalAccess(ApprovalAccessLevel Level, IReadOnlySet<string> VisibleViaNodes)
{
    private static readonly IReadOnlySet<string> NoNodes = new HashSet<string>();

    public static ApprovalAccess None { get; } = new(ApprovalAccessLevel.None, NoNodes);

    public static ApprovalAccess Full { get; } = new(ApprovalAccessLevel.Full, NoNodes);

    public static ApprovalAccess Participant(IEnumerable<string> nodes) => new(ApprovalAccessLevel.Participant, nodes.ToHashSet());

    public bool CanView => Level != ApprovalAccessLevel.None;
}

/// <summary>
/// 审批的查看权限（详情、按业务查询、实例的流程定义、附件）。默认实现见 <see cref="Engine.DefaultApprovalAccessPolicy"/>，
/// 宿主可通过 <c>AddAccessPolicy&lt;T&gt;()</c> 替换或继承默认实现追加规则（如“证书管理员可看全部证书审批”）。
/// </summary>
public interface IApprovalAccessPolicy
{
    Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstanceContext instance, CancellationToken ct);
}
