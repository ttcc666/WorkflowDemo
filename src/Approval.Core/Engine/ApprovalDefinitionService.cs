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
public sealed class ApprovalDefinitionService
{
    private static readonly string[] BuiltInOrder = ["Role", "User", "FormField", "Initiator", "DeptLeader", "Manager", "ManagerChain"];
    private readonly DbContext _db;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyDictionary<string, ApproverTypeDescriptor> _approverTypes;
    private readonly IReadOnlyDictionary<string, IApprovalActionHandler> _actions;
    private readonly IReadOnlyList<ChannelInfo> _channels;

    public ApprovalDefinitionService(ApprovalDbContextAccessor accessor, IEnumerable<IApproverResolver> resolvers,
        IEnumerable<IApprovalActionHandler> actions, IEnumerable<IApprovalChannel> channels, TimeProvider clock)
        : this(accessor, new ApprovalExtensionCatalog(() => resolvers, () => [], () => actions, () => channels), clock)
    {
    }

    internal ApprovalDefinitionService(ApprovalDbContextAccessor accessor, ApprovalExtensionCatalog extensions, TimeProvider clock)
    {
        _db = accessor.Context;
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

    public IReadOnlyList<string> Validate(FlowDefinition flow) =>
        FlowDefinitionValidator.Validate(flow, new FlowValidationContext
        {
            ApproverTypes = _approverTypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ApproverDescriptors = _approverTypes,
            Channels = _channels.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Actions = _actions
        });

    /// <summary>不阻止发布的提示，如节点隐藏的字段可能通过标题、通知模板或计算字段间接暴露。</summary>
    public IReadOnlyList<string> Warnings(FlowDefinition flow) => FlowDefinitionValidator.Warnings(flow);

    public async Task<ApprovalDefinition> PublishAsync(string key, string name, FlowDefinition flow, CancellationToken ct = default)
    {
        var errors = Validate(flow);
        if (errors.Count > 0)
            throw new ApprovalException("流程定义校验失败：" + string.Join("；", errors)) { Code = ApprovalErrorCode.InvalidDefinition };

        var latestVersion = await _db.Set<ApprovalDefinition>()
            .Where(d => d.Key == key)
            .MaxAsync(d => (int?)d.Version, ct) ?? 0;

        var definition = new ApprovalDefinition
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Version = latestVersion + 1,
            DefinitionJson = flow.ToJson(),
            IsPublished = true,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _db.Set<ApprovalDefinition>().Add(definition);
        await _db.SaveChangesAsync(ct);
        return definition;
    }

    public Task<ApprovalDefinition> PublishAsync(string key, string name, string flowJson, CancellationToken ct = default) =>
        PublishAsync(key, name, FlowDefinition.Parse(flowJson), ct);

    public Task<List<ApprovalDefinition>> ListLatestAsync(CancellationToken ct = default) =>
        _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.IsPublished && d.Version == _db.Set<ApprovalDefinition>()
                .Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version))
            .OrderBy(d => d.Key)
            .ToListAsync(ct);

    /// <summary>已发布流程的标识与名称（列表筛选下拉使用，不含定义内容）。</summary>
    public Task<List<FlowSummary>> ListFlowsAsync(CancellationToken ct = default) =>
        _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.IsPublished && d.Version == _db.Set<ApprovalDefinition>()
                .Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version))
            .OrderBy(d => d.Key)
            .Select(d => new FlowSummary(d.Key, d.Name))
            .ToListAsync(ct);

    public Task<ApprovalDefinition?> GetLatestAsync(string key, CancellationToken ct = default) =>
        _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == key && d.IsPublished)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

    /// <summary>按 ID 取某个具体版本（审批详情页按实例绑定的版本画流程图）。</summary>
    public Task<ApprovalDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Set<ApprovalDefinition>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<List<ApprovalDefinition>> ListVersionsAsync(string key, CancellationToken ct = default) =>
        _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == key)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);
}
