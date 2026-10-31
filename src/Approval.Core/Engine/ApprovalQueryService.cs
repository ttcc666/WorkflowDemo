using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

public sealed record TodoItem(
    Guid TaskId,
    Guid InstanceId,
    string DefinitionKey,
    string DefinitionName,
    string BusinessType,
    string BusinessKey,
    string? Title,
    string InitiatorId,
    string NodeName,
    AssigneeKind AssigneeKind,
    string AssigneeValue,
    DateTime CreatedAt,
    DateTime? DueAt,
    ApprovalTaskKind Kind);

/// <summary>列表项（我已处理、我发起的）：不含任务与轨迹，带流程名称。</summary>
public sealed record InstanceListItem(
    Guid Id,
    string DefinitionKey,
    string DefinitionName,
    int DefinitionVersion,
    string BusinessType,
    string BusinessKey,
    string? Title,
    string InitiatorId,
    InstanceStatus Status,
    string? CurrentNodeId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>只读查询：待办、已办、我发起的、审批详情。</summary>
public sealed class ApprovalQueryService(
    ApprovalDbContextAccessor accessor,
    IApprovalIdentityProvider identity,
    ApprovalOptions options)
{
    private readonly DbContext _db = accessor.Context;

    /// <summary>我的待办：分配给我本人的任务 + 分配给我所在角色的任务。</summary>
    public async Task<List<TodoItem>> GetTodoAsync(string userId, CancellationToken ct = default)
    {
        var roles = (await identity.GetRolesAsync(userId, ct)).ToList();
        var excludeOwn = !options.AllowInitiatorApprove;

        var query =
            from t in _db.Set<ApprovalTask>().AsNoTracking()
            join i in _db.Set<ApprovalInstance>().AsNoTracking() on t.InstanceId equals i.Id
            join d in _db.Set<ApprovalDefinition>().AsNoTracking() on i.DefinitionId equals d.Id
            where t.Status == ApprovalTaskStatus.Pending && i.Status == InstanceStatus.Running
                  && ((t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == userId)
                      || (t.AssigneeKind == AssigneeKind.Role && roles.Contains(t.AssigneeValue)))
                  // 职责分离：不允许审批自己的申请时，自己发起的申请不出现在待办中
                  && (!excludeOwn || i.InitiatorId != userId || t.Kind == ApprovalTaskKind.Resubmit)
            orderby t.CreatedAt
            select new TodoItem(t.Id, i.Id, i.DefinitionKey, d.Name, i.BusinessType, i.BusinessKey, i.Title, i.InitiatorId,
                t.NodeName, t.AssigneeKind, t.AssigneeValue, t.CreatedAt, t.DueAt, t.Kind);

        return await query.ToListAsync(ct);
    }

    /// <summary>我处理过的流程。</summary>
    public Task<List<InstanceListItem>> GetDoneAsync(string userId, CancellationToken ct = default) =>
        ToListItems(_db.Set<ApprovalInstance>().Where(i => i.Tasks.Any(t => t.HandledBy == userId)), ct);

    public Task<List<InstanceListItem>> GetStartedByAsync(string userId, CancellationToken ct = default) =>
        ToListItems(_db.Set<ApprovalInstance>().Where(i => i.InitiatorId == userId), ct);

    /// <summary>审批详情（含任务与轨迹）。</summary>
    public Task<ApprovalInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default) =>
        WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct);

    /// <summary>某业务单据的全部审批记录（驳回后重新发起会有多条）。</summary>
    public Task<List<ApprovalInstance>> GetByBusinessAsync(string businessType, string businessKey, CancellationToken ct = default) =>
        WithDetails()
            .Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    private Task<List<InstanceListItem>> ToListItems(IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
        (from i in instances.AsNoTracking()
         join d in _db.Set<ApprovalDefinition>().AsNoTracking() on i.DefinitionId equals d.Id
         orderby i.CreatedAt descending
         select new InstanceListItem(i.Id, i.DefinitionKey, d.Name, i.DefinitionVersion, i.BusinessType, i.BusinessKey, i.Title,
             i.InitiatorId, i.Status, i.CurrentNodeId, i.CreatedAt, i.CompletedAt))
        .ToListAsync(ct);

    private IQueryable<ApprovalInstance> WithDetails() =>
        _db.Set<ApprovalInstance>().AsNoTracking()
            .Include(i => i.Tasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.Order))
            .Include(i => i.Logs.OrderBy(l => l.Sequence))
            .AsSplitQuery();
}
