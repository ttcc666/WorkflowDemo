using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine.Query;

/// <summary>审批实例、业务历史和实例绑定定义的读取、访问控制与数据投影。</summary>
internal sealed class ApprovalInstanceReader(
    ApprovalDbContextAccessor accessor, IApprovalAccessPolicy accessPolicy, ApprovalOptions options, IApprovalDataProjection? projection)
{
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization = accessor.Authorization;
    private readonly DbContext _db = accessor.Context;
    private readonly Dictionary<Guid, FlowDefinition> _flows = [];
    private readonly Dictionary<Guid, string> _definitionJson = [];

    /// <summary>审批详情（含任务与轨迹）。需要敏感数据读取权限，供宿主后台使用；接口请用 <see cref="GetInstanceForViewerAsync"/>。</summary>
    internal async Task<InstanceDetail?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceReadSensitive, new() { InstanceId = instanceId }, null, ct);
        var instance = await WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        return instance is null ? null : SensitiveDetail(instance);
    }

    /// <summary>某业务单据的全部审批记录（驳回后重新发起会有多条）。需要敏感数据读取权限，并应用宿主查询范围。</summary>
    internal async Task<List<InstanceDetail>> GetByBusinessAsync(string businessType, string businessKey, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceReadSensitive, new() { BusinessType = businessType, BusinessKey = businessKey }, null, ct);
        var candidates = await (await _authorization.ScopeAsync(WithDetails(), ct))
            .Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        return candidates.Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey).Select(SensitiveDetail).ToList();
    }

    /// <summary>按查看者权限返回审批详情；不存在或无权查看时返回 null（接口统一返回 404）。</summary>
    internal async Task<InstanceDetail?> GetInstanceForViewerAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default) =>
        await WithDetails().FirstOrDefaultAsync(i => i.Id == instanceId, ct) is { } instance ? await ToDetailAsync(instance, viewer, ct) : null;

    /// <summary>某业务单据的审批记录，只返回查看者有权查看的。</summary>
    internal async Task<List<InstanceDetail>> GetByBusinessForViewerAsync(string businessType, string businessKey, ApprovalViewer viewer,
        CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { BusinessType = businessType, BusinessKey = businessKey }, viewer.UserId, ct);
        var result = new List<InstanceDetail>();
        var instances = (await (await _authorization.ScopeAsync(WithDetails(), ct)).Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey).ToListAsync(ct))
            .Where(i => i.BusinessType == businessType && i.BusinessKey == businessKey).ToList();
        var definitions = instances.Select(i => i.DefinitionId).Distinct().Where(id => !_flows.ContainsKey(id) && !_definitionJson.ContainsKey(id)).ToList();
        if (definitions.Count > 0)
            foreach (var definition in await _db.Set<ApprovalDefinition>().AsNoTracking().Where(d => definitions.Contains(d.Id)).ToListAsync(ct))
                _definitionJson[definition.Id] = definition.DefinitionJson;
        foreach (var instance in instances)
        {
            if (await ToDetailAsync(instance, viewer, ct) is { } detail) result.Add(detail);
        }
        return result;
    }

    /// <summary>实例绑定版本的流程定义公开视图（见 <see cref="FlowDefinition.ToPublicView"/>）；无权查看实例时返回 null。</summary>
    internal async Task<ApprovalDefinitionSnapshot?> GetPublicDefinitionAsync(Guid instanceId, ApprovalViewer viewer, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { InstanceId = instanceId }, viewer.UserId, ct);
        viewer = await _authorization.ViewerAsync(viewer, instanceId, ct);
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null || !(await accessPolicy.GetAccessAsync(viewer, ApprovalContextSnapshots.Instance(instance), ct)).CanView) return null;

        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        return ApprovalContextSnapshots.Definition(definition) with { DefinitionJson = FlowDefinition.Parse(definition.DefinitionJson).ToPublicView().ToJson() };
    }

    private async Task<InstanceDetail?> ToDetailAsync(ApprovalInstance instance, ApprovalViewer viewer, CancellationToken ct)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceRead, new() { InstanceId = instance.Id }, viewer.UserId, ct);
        viewer = await _authorization.ViewerAsync(viewer, instance.Id, ct);
        var access = await accessPolicy.GetAccessAsync(viewer, ApprovalContextSnapshots.Instance(instance), ct);
        if (!access.CanView) return null;

        // 参与人：在其参与的节点都设为隐藏的字段不返回（表单数据、修改轨迹中的字段差异）
        var flow = await FlowOfAsync(instance.DefinitionId, ct);
        if (flow.Form is not null && projection is null)
            throw new ApprovalException("缺少快照模块的数据可见性适配器") { Code = ApprovalErrorCode.InvalidDefinition };
        var context = new ApprovalDataProjectionContext(ApprovalContextSnapshots.Instance(instance), ApprovalContextSnapshots.Logs(instance.Logs));
        var data = projection?.Project(FlowDefinition.Parse(flow.ToJson()), context, access)
            ?? new ApprovalDataView(instance.FormDataJson, context.Logs, []);

        return Assemble(instance, data, access.Level) with
        {
            CanWithdraw = ApprovalEngine.CanWithdraw(instance, viewer.UserId, options)
                && await _authorization.CanAsync(ApprovalPermissions.InstanceWithdraw, new() { InstanceId = instance.Id }, viewer.UserId, ct)
        };
    }

    private static InstanceDetail SensitiveDetail(ApprovalInstance instance) => Assemble(instance,
        new(instance.FormDataJson, ApprovalContextSnapshots.Logs(instance.Logs), []), ApprovalAccessLevel.Full);

    private static InstanceDetail Assemble(ApprovalInstance instance, ApprovalDataView data, ApprovalAccessLevel access) => new(
        instance.Id, instance.DefinitionId, instance.DefinitionKey, instance.DefinitionVersion, instance.BusinessType,
        instance.BusinessKey, instance.Title, instance.InitiatorId, instance.Status, instance.CurrentNodeId, instance.Round,
        data.DataJson, instance.CreatedAt, instance.CompletedAt, ApprovalContextSnapshots.Tasks(instance.Tasks),
        data.Logs, access, data.HiddenFields);

    /// <summary>实例绑定版本的流程定义（同一请求内按定义 ID 缓存，按业务查询多条记录时只解析一次）。</summary>
    private async Task<FlowDefinition> FlowOfAsync(Guid definitionId, CancellationToken ct)
    {
        if (_flows.TryGetValue(definitionId, out var flow)) return flow;
        if (_definitionJson.TryGetValue(definitionId, out var json)) return _flows[definitionId] = FlowDefinition.Parse(json);
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == definitionId, ct);
        return _flows[definitionId] = FlowDefinition.Parse(definition.DefinitionJson);
    }

    private IQueryable<ApprovalInstance> WithDetails() =>
        _db.Set<ApprovalInstance>().AsNoTracking()
            .Include(i => i.Tasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.Order))
            .Include(i => i.Logs.OrderBy(l => l.Sequence))
            .AsSplitQuery();
}
