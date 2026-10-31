using Approval.Core.Abstractions;
using Approval.Core.Definitions;
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

/// <summary>审批详情（按查看者过滤，接口返回）：实例、任务、轨迹与查看者的访问级别，不含运行时状态、并发令牌等内部字段。</summary>
public sealed record InstanceDetail(
    Guid Id,
    Guid DefinitionId,
    string DefinitionKey,
    int DefinitionVersion,
    string BusinessType,
    string BusinessKey,
    string? Title,
    string InitiatorId,
    InstanceStatus Status,
    string? CurrentNodeId,
    int Round,
    string FormDataJson,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ApprovalTask> Tasks,
    IReadOnlyList<ApprovalLog> Logs,
    ApprovalAccessLevel Access);

/// <summary>只读查询：待办、已办、我发起的、审批详情。</summary>
public sealed class ApprovalQueryService(
    ApprovalDbContextAccessor accessor,
    IApprovalIdentityProvider identity,
    IApprovalAccessPolicy accessPolicy,
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

    /// <summary>审批详情（含任务与轨迹）。<b>不做权限判断</b>，供宿主后台使用；接口请用 <see cref="GetInstanceForViewerAsync"/>。</summary>
    public Task<ApprovalInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default) =>
        WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct);

    /// <summary>某业务单据的全部审批记录（驳回后重新发起会有多条）。<b>不做权限判断</b>。</summary>
    public Task<List<ApprovalInstance>> GetByBusinessAsync(string businessType, string businessKey, CancellationToken ct = default) =>
        WithDetails()
            .Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    /// <summary>按查看者权限返回审批详情；不存在或无权查看时返回 null（接口统一返回 404）。</summary>
    public async Task<InstanceDetail?> GetInstanceForViewerAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default) =>
        await GetInstanceAsync(instanceId, ct) is { } instance ? await ToDetailAsync(instance, viewer, ct) : null;

    /// <summary>某业务单据的审批记录，只返回查看者有权查看的。</summary>
    public async Task<List<InstanceDetail>> GetByBusinessForViewerAsync(string businessType, string businessKey, ApprovalViewer viewer,
        CancellationToken ct = default)
    {
        var result = new List<InstanceDetail>();
        foreach (var instance in await GetByBusinessAsync(businessType, businessKey, ct))
        {
            if (await ToDetailAsync(instance, viewer, ct) is { } detail) result.Add(detail);
        }
        return result;
    }

    /// <summary>实例绑定版本的流程定义公开视图（见 <see cref="FlowDefinition.ToPublicView"/>）；无权查看实例时返回 null。</summary>
    public async Task<ApprovalDefinition?> GetPublicDefinitionAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default)
    {
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null || !(await accessPolicy.GetAccessAsync(viewer, instance, ct)).CanView) return null;

        // 未跟踪的实体，替换 JSON 不会写回数据库
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        definition.DefinitionJson = FlowDefinition.Parse(definition.DefinitionJson).ToPublicView().ToJson();
        return definition;
    }

    private async Task<InstanceDetail?> ToDetailAsync(ApprovalInstance instance, ApprovalViewer viewer, CancellationToken ct)
    {
        var access = await accessPolicy.GetAccessAsync(viewer, instance, ct);
        if (!access.CanView) return null;

        return new InstanceDetail(instance.Id, instance.DefinitionId, instance.DefinitionKey, instance.DefinitionVersion, instance.BusinessType,
            instance.BusinessKey, instance.Title, instance.InitiatorId, instance.Status, instance.CurrentNodeId, instance.Round,
            instance.FormDataJson, instance.CreatedAt, instance.CompletedAt, instance.Tasks, instance.Logs, access.Level);
    }

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
