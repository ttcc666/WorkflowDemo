using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Forms;
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
    ApprovalTaskKind Kind,
    ApprovalTaskStatus Status);

/// <summary>流程列表项（我已处理、我发起的、抄送我的、管理员监控）：不含任务与轨迹，带流程名称。</summary>
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

/// <summary>
/// 审批详情（按查看者过滤，接口返回）：实例、任务、轨迹与查看者的访问级别，不含运行时状态、并发令牌等内部字段。
/// 参与人看不到的字段（<see cref="HiddenFields"/>，见 <see cref="FlowNode.FieldPermissions"/>）已从表单数据与修改轨迹中去掉。
/// </summary>
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
    ApprovalAccessLevel Access,
    IReadOnlyList<string> HiddenFields)
{
    /// <summary>当前查看者是否可以撤回；仅供界面显示，提交操作时引擎会再次校验。</summary>
    public bool CanWithdraw { get; init; }
}

/// <summary>待办筛选：关键字（标题 / 申请人）、流程标识、只看已超时。</summary>
public sealed record TodoQuery(string? Keyword = null, string? DefinitionKey = null, bool OverdueOnly = false);

/// <summary>流程列表筛选：关键字（标题 / 申请人）、状态、流程标识、发起时间区间 [From, To)（UTC）、发起人（管理员监控）。</summary>
public sealed record InstanceQuery(
    string? Keyword = null,
    InstanceStatus? Status = null,
    string? DefinitionKey = null,
    DateTime? From = null,
    DateTime? To = null,
    string? InitiatorId = null);

/// <summary>只读查询：待办、已办、我发起的、抄送我的、全部实例（管理员）、审批详情。列表均分页，按时间排序。</summary>
public sealed class ApprovalQueryService(
    ApprovalDbContextAccessor accessor,
    IApprovalIdentityProvider identity,
    IApprovalAccessPolicy accessPolicy,
    ApprovalOptions options,
    TimeProvider clock)
{
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization = accessor.Authorization;
    private readonly DbContext _db = accessor.Context;
    private readonly Dictionary<Guid, FlowDefinition> _flows = [];

    /// <summary>我的待办：分配给我本人的任务 + 分配给我所在角色的任务，按到达时间排序。</summary>
    public async Task<PagedResult<TodoItem>> GetTodoAsync(string userId, TodoQuery? query = null, PageRequest? page = null,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskRead, new(), userId, ct);
        var scopedInstances = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        query ??= new TodoQuery();
        var roles = (await identity.GetRolesAsync(userId, ct)).ToList();
        var keyword = Trim(query.Keyword);
        var definitionKey = Trim(query.DefinitionKey);
        var overdueOnly = query.OverdueOnly;
        var now = clock.GetUtcNow().UtcDateTime;

        var items =
            from t in TodoTasks(userId, roles)
            join i in scopedInstances on t.InstanceId equals i.Id
            join d in _db.Set<ApprovalDefinition>().AsNoTracking() on i.DefinitionId equals d.Id
            where (keyword == null || (i.Title != null && i.Title.Contains(keyword)) || i.InitiatorId.Contains(keyword))
                  && (definitionKey == null || i.DefinitionKey == definitionKey)
                  && (!overdueOnly || (t.DueAt != null && t.DueAt < now))
            orderby t.CreatedAt, t.Id
            select new TodoItem(t.Id, i.Id, i.DefinitionKey, d.Name, i.BusinessType, i.BusinessKey, i.Title, i.InitiatorId,
                t.NodeName, t.AssigneeKind, t.AssigneeValue, t.CreatedAt, t.DueAt, t.Kind, t.Status);

        return await items.ToPagedAsync(page ?? new PageRequest(), ct);
    }

    /// <summary>
    /// 指派给某人（用户任务，不含角色任务）的未完成任务：待处理、尚未轮到、等待加签，流程进行中，不含退回发起人的申请。
    /// 用于离职交接前预览（管理员，接口层校验权限）。
    /// </summary>
    public async Task<PagedResult<TodoItem>> GetOpenUserTasksAsync(string userId, PageRequest? page = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReassignBulk, new() { TargetUserId = userId }, null, ct);
        var scopedInstances = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        return await (from t in _db.Set<ApprovalTask>().AsNoTracking()
         join i in scopedInstances on t.InstanceId equals i.Id
         join d in _db.Set<ApprovalDefinition>().AsNoTracking() on i.DefinitionId equals d.Id
         where t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == userId && t.Kind != ApprovalTaskKind.Resubmit
               && (t.Status == ApprovalTaskStatus.Pending || t.Status == ApprovalTaskStatus.Waiting || t.Status == ApprovalTaskStatus.Suspended)
               && i.Status == InstanceStatus.Running
         orderby t.CreatedAt, t.Id
         select new TodoItem(t.Id, i.Id, i.DefinitionKey, d.Name, i.BusinessType, i.BusinessKey, i.Title, i.InitiatorId,
             t.NodeName, t.AssigneeKind, t.AssigneeValue, t.CreatedAt, t.DueAt, t.Kind, t.Status))
        .ToPagedAsync(page ?? new PageRequest(), ct);
    }

    /// <summary>待办数量（工作台角标）。</summary>
    public async Task<int> GetTodoCountAsync(string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskRead, new(), userId, ct);
        var visible = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>(), ct);
        return await TodoTasks(userId, (await identity.GetRolesAsync(userId, ct)).ToList()).CountAsync(t => visible.Select(i => i.Id).Contains(t.InstanceId), ct);
    }

    /// <summary>我处理过的流程。</summary>
    public async Task<PagedResult<InstanceListItem>> GetDoneAsync(string userId, InstanceQuery? query = null, PageRequest? page = null,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new(), userId, ct);
        return await ToListItemsAsync(_db.Set<ApprovalInstance>().Where(i => i.Tasks.Any(t => t.HandledBy == userId)), query, page, ct);
    }

    /// <summary>我发起的流程。</summary>
    public async Task<PagedResult<InstanceListItem>> GetStartedByAsync(string userId, InstanceQuery? query = null, PageRequest? page = null,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new(), userId, ct);
        return await ToListItemsAsync(_db.Set<ApprovalInstance>().Where(i => i.InitiatorId == userId), query, page, ct);
    }

    /// <summary>抄送我的：抄送给本人或所在角色的流程（多次抄送只列一次）。</summary>
    public async Task<PagedResult<InstanceListItem>> GetCarbonCopiedAsync(string userId, InstanceQuery? query = null, PageRequest? page = null,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new(), userId, ct);
        var roles = (await identity.GetRolesAsync(userId, ct)).ToList();
        var copies = _db.Set<ApprovalCarbonCopy>().Where(c =>
            (c.AssigneeKind == AssigneeKind.User && c.AssigneeValue == userId)
            || (c.AssigneeKind == AssigneeKind.Role && roles.Contains(c.AssigneeValue)));
        return await ToListItemsAsync(_db.Set<ApprovalInstance>().Where(i => copies.Any(c => c.InstanceId == i.Id)), query, page, ct);
    }

    /// <summary>全部流程（管理员监控）。需要实例监控权限，并在分页前应用宿主查询范围。</summary>
    public async Task<PagedResult<InstanceListItem>> SearchInstancesAsync(InstanceQuery? query = null, PageRequest? page = null,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceReadAll, new(), null, ct);
        return await ToListItemsAsync(_db.Set<ApprovalInstance>(), query, page, ct);
    }

    /// <summary>审批详情（含任务与轨迹）。需要敏感数据读取权限，供宿主后台使用；接口请用 <see cref="GetInstanceForViewerAsync"/>。</summary>
    public async Task<ApprovalInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceReadSensitive, new() { InstanceId = instanceId }, null, ct);
        return await WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
    }

    /// <summary>某业务单据的全部审批记录（驳回后重新发起会有多条）。需要敏感数据读取权限，并应用宿主查询范围。</summary>
    public async Task<List<ApprovalInstance>> GetByBusinessAsync(string businessType, string businessKey, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceReadSensitive, new() { BusinessType = businessType, BusinessKey = businessKey }, null, ct);
        var candidates = await (await _authorization.ScopeAsync(WithDetails(), ct))
            .Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        return candidates.Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey).ToList();
    }

    /// <summary>按查看者权限返回审批详情；不存在或无权查看时返回 null（接口统一返回 404）。</summary>
    public async Task<InstanceDetail?> GetInstanceForViewerAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default) =>
        await WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct) is { } instance ? await ToDetailAsync(instance, viewer, ct) : null;

    /// <summary>某业务单据的审批记录，只返回查看者有权查看的。</summary>
    public async Task<List<InstanceDetail>> GetByBusinessForViewerAsync(string businessType, string businessKey, ApprovalViewer viewer,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { BusinessType = businessType, BusinessKey = businessKey }, viewer.UserId, ct);
        var result = new List<InstanceDetail>();
        foreach (var instance in await (await _authorization.ScopeAsync(WithDetails(), ct)).Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey).ToListAsync(ct))
        {
            if (instance.BusinessType != businessType || instance.BusinessKey != businessKey) continue;
            if (await ToDetailAsync(instance, viewer, ct) is { } detail) result.Add(detail);
        }
        return result;
    }

    /// <summary>实例绑定版本的流程定义公开视图（见 <see cref="FlowDefinition.ToPublicView"/>）；无权查看实例时返回 null。</summary>
    public async Task<ApprovalDefinition?> GetPublicDefinitionAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { InstanceId = instanceId }, viewer.UserId, ct);
        viewer = await _authorization.ViewerAsync(viewer, instanceId, ct);
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null || !(await accessPolicy.GetAccessAsync(viewer, instance, ct)).CanView) return null;

        // 未跟踪的实体，替换 JSON 不会写回数据库
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        definition.DefinitionJson = FlowDefinition.Parse(definition.DefinitionJson).ToPublicView().ToJson();
        return definition;
    }

    private async Task<InstanceDetail?> ToDetailAsync(ApprovalInstance instance, ApprovalViewer viewer, CancellationToken ct)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { InstanceId = instance.Id }, viewer.UserId, ct);
        viewer = await _authorization.ViewerAsync(viewer, instance.Id, ct);
        var access = await accessPolicy.GetAccessAsync(viewer, instance, ct);
        if (!access.CanView) return null;

        // 参与人：在其参与的节点都设为隐藏的字段不返回（表单数据、修改轨迹中的字段差异）
        var hidden = access.Level == ApprovalAccessLevel.Participant && access.VisibleViaNodes.Count > 0
            ? FieldVisibility.HiddenFields(await FlowOfAsync(instance.DefinitionId, ct), access.VisibleViaNodes)
            : FieldVisibility.None;

        return new InstanceDetail(instance.Id, instance.DefinitionId, instance.DefinitionKey, instance.DefinitionVersion, instance.BusinessType,
            instance.BusinessKey, instance.Title, instance.InitiatorId, instance.Status, instance.CurrentNodeId, instance.Round,
            FieldVisibility.RemoveFields(instance.FormDataJson, hidden), instance.CreatedAt, instance.CompletedAt, instance.Tasks,
            FieldVisibility.FilterLogs(instance.Logs, hidden), access.Level, hidden.Order(StringComparer.OrdinalIgnoreCase).ToList())
        {
            CanWithdraw = ApprovalEngine.CanWithdraw(instance, viewer.UserId, options)
                && await _authorization.CanAsync(ApprovalPermissions.InstanceWithdraw, new() { InstanceId = instance.Id }, viewer.UserId, ct)
        };
    }

    /// <summary>实例绑定版本的流程定义（同一请求内按定义 ID 缓存，按业务查询多条记录时只解析一次）。</summary>
    private async Task<FlowDefinition> FlowOfAsync(Guid definitionId, CancellationToken ct)
    {
        if (_flows.TryGetValue(definitionId, out var flow)) return flow;
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == definitionId, ct);
        return _flows[definitionId] = FlowDefinition.Parse(definition.DefinitionJson);
    }

    /// <summary>用户可处理的待办任务：分配给本人或所在角色、流程进行中；不允许自审时排除自己发起的（重新提交任务除外）。</summary>
    private IQueryable<ApprovalTask> TodoTasks(string userId, List<string> roles)
    {
        var excludeOwn = !options.AllowInitiatorApprove;
        return _db.Set<ApprovalTask>().AsNoTracking().Where(t =>
            t.Status == ApprovalTaskStatus.Pending
            && ((t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == userId)
                || (t.AssigneeKind == AssigneeKind.Role && roles.Contains(t.AssigneeValue)))
            && _db.Set<ApprovalInstance>().Any(i => i.Id == t.InstanceId && i.Status == InstanceStatus.Running
                // 职责分离：不允许审批自己的申请时，自己发起的申请不出现在待办中
                && (!excludeOwn || i.InitiatorId != userId || t.Kind == ApprovalTaskKind.Resubmit)));
    }

    /// <summary>按筛选条件分页，发起时间倒序（Id 作为第二排序保证翻页稳定）。</summary>
    private async Task<PagedResult<InstanceListItem>> ToListItemsAsync(IQueryable<ApprovalInstance> instances, InstanceQuery? query,
        PageRequest? page, CancellationToken ct)
    {
        instances = await _authorization.ScopeAsync(instances, ct);
        query ??= new InstanceQuery();
        if (Trim(query.Keyword) is { } keyword)
            instances = instances.Where(i => (i.Title != null && i.Title.Contains(keyword)) || i.InitiatorId.Contains(keyword));
        if (query.Status is { } status) instances = instances.Where(i => i.Status == status);
        if (Trim(query.DefinitionKey) is { } definitionKey) instances = instances.Where(i => i.DefinitionKey == definitionKey);
        if (query.From is { } from) instances = instances.Where(i => i.CreatedAt >= from);
        if (query.To is { } to) instances = instances.Where(i => i.CreatedAt < to);
        if (Trim(query.InitiatorId) is { } initiator) instances = instances.Where(i => i.InitiatorId == initiator);

        return await (from i in instances.AsNoTracking()
                join d in _db.Set<ApprovalDefinition>().AsNoTracking() on i.DefinitionId equals d.Id
                orderby i.CreatedAt descending, i.Id descending
                select new InstanceListItem(i.Id, i.DefinitionKey, d.Name, i.DefinitionVersion, i.BusinessType, i.BusinessKey, i.Title,
                    i.InitiatorId, i.Status, i.CurrentNodeId, i.CreatedAt, i.CompletedAt))
            .ToPagedAsync(page ?? new PageRequest(), ct);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IQueryable<ApprovalInstance> WithDetails() =>
        _db.Set<ApprovalInstance>().AsNoTracking()
            .Include(i => i.Tasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.Order))
            .Include(i => i.Logs.OrderBy(l => l.Sequence))
            .AsSplitQuery();
}
