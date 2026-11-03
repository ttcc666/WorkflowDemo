using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Extensions;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Templates;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>设计器所需的元数据：已注册的审批人类型（含参数类型描述）、通知接收人类型、动作、渠道、模板变量。</summary>
public sealed record ApprovalMetadata(
    IReadOnlyList<ApproverTypeDescriptor> ApproverTypes,
    IReadOnlyList<ApproverTypeDescriptor> RecipientTypes,
    IReadOnlyList<ApprovalActionDescriptor> Actions,
    IReadOnlyList<ChannelInfo> Channels,
    IReadOnlyList<TemplateVariableInfo> TemplateVariables,
    IReadOnlyDictionary<NotifyEvent, string> DefaultTemplates);

/// <summary>流程标识与名称（最新发布版本）。</summary>
public sealed record FlowSummary(string Key, string Name);

/// <summary>流程定义管理：校验、发布新版本、查询。</summary>
public sealed partial class ApprovalDefinitionService
{
    private static readonly string[] BuiltInOrder = ["Role", "User", "FormField", "Initiator", "DeptLeader", "Manager", "ManagerChain"];
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly ApprovalUnitOfWork _unit;
    private readonly ApprovalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ApprovalDbContextAccessor _accessor;
    private readonly IReadOnlyDictionary<string, ApproverTypeDescriptor> _approverTypes;
    private readonly IReadOnlyDictionary<string, IApprovalActionHandler> _actions;
    private readonly IReadOnlyList<ChannelInfo> _channels;

    public ApprovalDefinitionService(ApprovalDbContextAccessor accessor, IEnumerable<IApproverResolver> resolvers,
        IEnumerable<IApprovalActionHandler> actions, IEnumerable<IApprovalChannel> channels, TimeProvider clock, ApprovalOptions? options = null)
        : this(accessor, new ApprovalExtensionCatalog(() => resolvers, () => [], () => actions, () => channels), clock, options)
    {
    }

    internal ApprovalDefinitionService(ApprovalDbContextAccessor accessor, ApprovalExtensionCatalog extensions, TimeProvider clock, ApprovalOptions? options = null)
    {
        _db = accessor.Context;
        _accessor = accessor;
        _unit = accessor.UnitOfWork;
        _options = options ?? new();
        _authorization = accessor.Authorization;
        _clock = clock;
        _approverTypes = extensions.ApproverDescriptors;
        _actions = extensions.Actions;
        _channels = extensions.ChannelDescriptors;
    }

    /// <summary>已注册的审批人类型（供设计器选择，按参数类型生成控件）。</summary>
    public IReadOnlyList<ApproverTypeDescriptor> ApproverTypes =>
        _approverTypes.Values
            .OrderBy(d => Array.IndexOf(BuiltInOrder, d.Type) is var i and >= 0 ? i : BuiltInOrder.Length)
            .ThenBy(d => d.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public ApprovalMetadata Metadata => new(
        ApproverTypes,
        [.. NotificationDefaults.NotificationRecipientDescriptors, .. ApproverTypes],
        _actions.Values.Select(a => a.Descriptor).ToList(),
        _channels,
        TemplateVariables.Catalog,
        NotificationDefaults.Contents);

    public IReadOnlyList<string> Validate(FlowDefinition flow)
    {
        _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionValidate).GetAwaiter().GetResult();
        return ValidateCore(flow);
    }
    public async Task<IReadOnlyList<string>> ValidateAsync(FlowDefinition flow, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionValidate, new(), null, ct);
        return ValidateCore(flow);
    }
    private IReadOnlyList<string> ValidateCore(FlowDefinition flow)
    {
        var errors = FlowDefinitionValidator.Validate(flow, new FlowValidationContext
        {
            ApproverTypes = _approverTypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ApproverDescriptors = _approverTypes,
            Channels = _channels.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Actions = _actions
        }).ToList();
        if (flow.Nodes.Values.SelectMany(n => n.Actions).Any(s => _actions.TryGetValue(s.Type, out var h) && h.CommitsWorkUnit))
            errors.Add("动作不能自行提交工作单元，请迁移到由框架提交的本地写入或 Outbox");
        if (!_options.AllowSynchronousExternalActions && flow.Nodes.Values.SelectMany(n => n.Actions)
            .Any(step => step.Mode == ActionMode.Sync && _actions.TryGetValue(step.Type, out var handler) && handler.HasExternalSideEffects))
            errors.Add("同步外部副作用不能随数据库事务回滚，请将动作改为 Async/Outbox；旧宿主需要显式声明过渡和补偿边界");
        return errors;
    }

    /// <summary>不阻止发布的提示，如节点隐藏的字段可能通过标题、通知模板或计算字段间接暴露。</summary>
    public IReadOnlyList<string> Warnings(FlowDefinition flow) => FlowDefinitionValidator.Warnings(flow);

    public Task<ApprovalDefinition> PublishAsync(string key, string name, FlowDefinition flow, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => PublishAsyncCore(key, name, flow, token), ct);

    private async Task<ApprovalDefinition> PublishAsyncCore(string key, string name, FlowDefinition flow, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionPublish, new() { DefinitionKey = key }, null, ct);
        var errors = ValidateCore(flow).ToList();
        if (errors.Count > 0)
            throw new ApprovalException("流程定义校验失败：" + string.Join("；", errors)) { Code = ApprovalErrorCode.InvalidDefinition };

        ValidateIdentity(key, name);
        var management = await GetOrCreateManagementAsync(key, name, ct);
        return await PublishManagedAsync(management, name, flow, null, null, false, ct);
    }

    public Task<ApprovalDefinition> PublishAsync(string key, string name, string flowJson, CancellationToken ct = default) =>
        PublishAsync(key, name, FlowDefinition.Parse(flowJson), ct);

    public async Task<List<ApprovalDefinition>> ListLatestAsync(CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionReadSensitive, new(), null, ct);
        var definitions = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.IsPublished && d.Version == _db.Set<ApprovalDefinition>()
                .Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version))
            .OrderBy(d => d.Key)
            .ToListAsync(ct);
        return await FilterReadableDefinitionsAsync(definitions, ct);
    }

    /// <summary>已发布流程的标识与名称（列表筛选下拉使用，不含定义内容）。</summary>
    public async Task<List<FlowSummary>> ListFlowsAsync(CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionRead, new(), null, ct);
        var definitions = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.IsPublished && d.Version == _db.Set<ApprovalDefinition>()
                .Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version))
            .OrderBy(d => d.Key)
            .Select(d => new { d.Id, d.Key, d.Name })
            .ToListAsync(ct);
        var result = new List<FlowSummary>();
        foreach (var definition in definitions)
            if (await _authorization.CanAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionRead, new() { DefinitionId = definition.Id }, ct: ct))
                result.Add(new(definition.Key, definition.Name));
        return result;
    }

    public async Task<ApprovalDefinition?> GetLatestAsync(string key, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, null, ct);
        return await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == key && d.IsPublished)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>按 ID 取某个具体版本（审批详情页按实例绑定的版本画流程图）。</summary>
    public async Task<ApprovalDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionId = id }, null, ct);
        return await _db.Set<ApprovalDefinition>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<List<ApprovalDefinition>> ListVersionsAsync(string key, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, null, ct);
        var definitions = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == key)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);
        return await FilterReadableDefinitionsAsync(definitions, ct);
    }

    private async Task<List<ApprovalDefinition>> FilterReadableDefinitionsAsync(List<ApprovalDefinition> definitions, CancellationToken ct)
    {
        var result = new List<ApprovalDefinition>();
        foreach (var definition in definitions)
            if (await _authorization.CanAsync(Approval.Core.Abstractions.ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionId = definition.Id }, ct: ct))
                result.Add(definition);
        return result;
    }
}
