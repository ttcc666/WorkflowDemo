using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>
/// 默认查看规则：发起人、管理员看全部；任意任务的处理人或候选人（用户任务本人、角色任务的角色成员、实际处理人，
/// 含已转交、已取消、尚未轮到的任务与加签任务）与抄送对象（含角色成员）可作为参与人查看。
/// 宿主可继承并重写 <see cref="GetAccessAsync"/> 追加规则。
/// </summary>
public class DefaultApprovalAccessPolicy(ApprovalDbContextAccessor accessor, IApprovalIdentityProvider identity) : IApprovalAccessPolicy
{
    protected DbContext Db { get; } = accessor.Context;

    protected IApprovalIdentityProvider Identity { get; } = identity;

    public virtual async Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstance instance, CancellationToken ct)
    {
        if (viewer.IsAdmin || instance.InitiatorId == viewer.UserId) return ApprovalAccess.Full;

        var nodes = await GetParticipatedNodesAsync(viewer.UserId, instance.Id, ct);
        return nodes.Count == 0 ? ApprovalAccess.None : ApprovalAccess.Participant(nodes);
    }

    /// <summary>用户作为处理人、候选人或抄送人涉及的节点（按数据库查询，不依赖实例上已加载的导航属性）。</summary>
    protected async Task<HashSet<string>> GetParticipatedNodesAsync(string userId, Guid instanceId, CancellationToken ct)
    {
        // 角色一次取全，再在查询中判断，避免逐个任务调用 IsInRoleAsync
        var roles = (await Identity.GetRolesAsync(userId, ct)).ToList();

        var taskNodes = await Db.Set<ApprovalTask>().AsNoTracking()
            .Where(t => t.InstanceId == instanceId
                        && (t.HandledBy == userId
                            || (t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == userId)
                            || (t.AssigneeKind == AssigneeKind.Role && roles.Contains(t.AssigneeValue))))
            .Select(t => t.NodeId)
            .Distinct()
            .ToListAsync(ct);

        var ccNodes = await Db.Set<ApprovalCarbonCopy>().AsNoTracking()
            .Where(c => c.InstanceId == instanceId
                        && ((c.AssigneeKind == AssigneeKind.User && c.AssigneeValue == userId)
                            || (c.AssigneeKind == AssigneeKind.Role && roles.Contains(c.AssigneeValue))))
            .Select(c => c.NodeId)
            .Distinct()
            .ToListAsync(ct);

        return [.. taskNodes, .. ccNodes];
    }
}
