using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Templates;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>设计器所需的元数据：已注册的审批人类型、动作、渠道、模板变量。</summary>
public sealed record ApprovalMetadata(
    IReadOnlyList<string> ApproverTypes,
    IReadOnlyList<string> RecipientTypes,
    IReadOnlyList<ApprovalActionDescriptor> Actions,
    IReadOnlyList<ChannelInfo> Channels,
    IReadOnlyList<TemplateVariableInfo> TemplateVariables,
    IReadOnlyDictionary<NotifyEvent, string> DefaultTemplates);

/// <summary>流程定义管理：校验、发布新版本、查询。</summary>
public sealed class ApprovalDefinitionService(
    ApprovalDbContextAccessor accessor,
    IEnumerable<IApproverResolver> resolvers,
    IEnumerable<IApprovalActionHandler> actions,
    IEnumerable<IApprovalChannel> channels,
    TimeProvider clock)
{
    private readonly DbContext _db = accessor.Context;

    /// <summary>已注册的审批人类型（供设计器下拉选择）。</summary>
    public IReadOnlyList<string> ApproverTypes { get; } =
        resolvers.Select(r => r.Type).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

    // 同名时后注册的覆盖先注册的，与引擎一致
    private readonly Dictionary<string, IApprovalActionHandler> _actions =
        actions.GroupBy(a => a.Descriptor.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    private readonly List<ChannelInfo> _channels =
    [
        new(InAppChannel.Name, InAppChannel.DisplayName),
        .. channels.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => new ChannelInfo(g.Key, g.Last().DisplayName))
    ];

    public ApprovalMetadata Metadata => new(
        ApproverTypes,
        [.. NotificationDefaults.NotificationRecipientTypes, .. ApproverTypes],
        _actions.Values.Select(a => a.Descriptor).ToList(),
        _channels,
        TemplateVariables.Catalog,
        NotificationDefaults.Contents);

    public IReadOnlyList<string> Validate(FlowDefinition flow) =>
        FlowDefinitionValidator.Validate(flow, new FlowValidationContext
        {
            ApproverTypes = ApproverTypes.ToHashSet(StringComparer.OrdinalIgnoreCase),
            Channels = _channels.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Actions = _actions
        });

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
            CreatedAt = clock.GetUtcNow().UtcDateTime
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
