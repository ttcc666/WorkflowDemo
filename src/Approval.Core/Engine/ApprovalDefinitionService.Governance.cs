using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

public sealed record DefinitionGovernance(string Key, string Name, string? DraftJson, long Revision, int LastVersion,
    bool IsEnabled, Guid? ActiveDefinitionId, Guid? DraftSourceDefinitionId, bool HasDraftChanges,
    DateTime? UpdatedAt, string? UpdatedBy, IReadOnlyDictionary<string, bool> Capabilities)
{
    public string? Category { get; init; }
}
public sealed record DefinitionGovernanceSummary(string Key, string Name, long Revision, int LastVersion,
    bool IsEnabled, Guid? ActiveDefinitionId, bool HasDraftChanges, DateTime? UpdatedAt,
    IReadOnlyDictionary<string, bool> Capabilities)
{
    public string? Category { get; init; }
}

public sealed partial class ApprovalDefinitionService
{
    private static string? CategoryOf(FlowDefinition flow)
    {
        var category = flow.Settings?.Category?.Trim();
        if (category?.Length > 100 || category?.Any(char.IsControl) == true)
            throw new ApprovalException("流程分类不能超过100字符或包含控制字符");
        return string.IsNullOrEmpty(category) ? null : category;
    }

    public Task<DefinitionGovernance> CopyAsync(string sourceKey, string targetKey, string name, Guid? sourceDefinitionId = null,
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = sourceKey }, ct: token);
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = targetKey }, ct: token);
        ValidateIdentity(targetKey, name);
        if (sourceKey == targetKey || await _db.Set<ApprovalDefinition>().AnyAsync(d => d.Key == targetKey, token)
            || await _db.Set<ApprovalDefinitionManagement>().AnyAsync(d => d.Key == targetKey, token))
            throw new ApprovalException("目标流程标识已存在") { Code = ApprovalErrorCode.Conflict };
        var source = await _db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.Key == sourceKey && d.IsPublished
            && (sourceDefinitionId == null || d.Id == sourceDefinitionId)).OrderByDescending(d => d.Version).FirstOrDefaultAsync(token)
            ?? throw new ApprovalException("来源流程版本不存在") { Code = ApprovalErrorCode.NotFound };
        return await SaveDesignDraftAsync(targetKey, name, source.DefinitionJson, 0, ct: token);
    }, ct);

    private static void ValidateIdentity(string key, string name)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ApprovalException("流程标识须为 1–100 字符，名称须为 1–200 字符");
    }

    private static void ValidateNote(string? note)
    { if (note?.Length > 1000) throw new ApprovalException("变更说明不能超过 1000 字符"); }

    private static ApprovalException Conflict() => new("流程已被其他操作修改，请重新读取后提交") { Code = ApprovalErrorCode.Conflict };

    private static void CheckRevision(ApprovalDefinitionManagement management, long expected)
    { if (expected < 0 || management.Revision != expected) throw Conflict(); }

    private async Task<ApprovalDefinitionManagement> GetOrCreateManagementAsync(string key, string name, CancellationToken ct)
    {
        var managed = await _db.Set<ApprovalDefinitionManagement>().SingleOrDefaultAsync(x => x.Key == key, ct);
        if (managed is not null) return managed;
        // The unique management row and Revision serialize publication; conflicts roll back the whole unit.
        managed = new()
        {
            Id = _unit.NewId("DefinitionManagement"),
            Key = key,
            Name = name,
            UpdatedBy = _accessor.ActorId,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _db.Add(managed);
        return managed;
    }

    private void Audit(ApprovalDefinitionManagement management, DefinitionAuditAction action, Guid? definitionId = null,
        Guid? source = null, int? version = null, string? note = null, string? json = null)
    {
        ValidateNote(note);
        management.Revision = checked(management.Revision + 1);
        management.UpdatedBy = _accessor.ActorId; management.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        _db.Add(new ApprovalDefinitionAudit
        {
            Id = _unit.NewId("DefinitionAudit"),
            Key = management.Key,
            Revision = management.Revision,
            Action = action,
            ActorId = management.UpdatedBy,
            CreatedAt = management.UpdatedAt,
            DefinitionId = definitionId,
            SourceDefinitionId = source,
            Version = version,
            ChangeNote = note,
            DefinitionHash = json is null ? null : ApprovalCommandHash.Of(json)
        });
    }

    private async Task SaveGovernanceAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex) { throw new ApprovalException(Conflict().Message, ex) { Code = ApprovalErrorCode.Conflict }; }
        catch (DbUpdateException ex) when (ApprovalDatabaseErrors.Unique(ex))
        { throw new ApprovalException(Conflict().Message, ex) { Code = ApprovalErrorCode.Conflict }; }
    }

    private async Task<ApprovalDefinition> PublishManagedAsync(ApprovalDefinitionManagement management, string name,
        FlowDefinition flow, Guid? source, string? note, bool fromDraft, CancellationToken ct)
    {
        ValidateNote(note);
        var definition = new ApprovalDefinition
        {
            Id = _unit.NewId("ApprovalDefinitionService"),
            Key = management.Key,
            Name = name,
            Version = checked(management.LastVersion + 1),
            DefinitionJson = SerializeDefinition(flow),
            IsPublished = true,
            Category = CategoryOf(flow),
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _db.Add(definition); management.LastVersion = definition.Version; management.ActiveDefinitionId = definition.Id;
        if (fromDraft || management.DraftJson is null) management.Category = definition.Category;
        if (!fromDraft && management.DraftJson is null) management.Name = name;
        Audit(management, DefinitionAuditAction.Published, definition.Id, source, definition.Version, note, definition.DefinitionJson);
        if (fromDraft) { management.PublishedDraftRevision = management.DraftRevision; management.DraftSourceDefinitionId = definition.Id; }
        await SaveGovernanceAsync(ct);
        return definition;
    }

    public Task<DefinitionGovernance> SaveDesignDraftAsync(string key, string name, string definitionJson, long expectedRevision,
        Guid? sourceDefinitionId = null, CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = key }, ct: token);
        ValidateIdentity(key, name);
        if (string.IsNullOrWhiteSpace(definitionJson)) throw new ApprovalException("设计草稿 JSON 不能为空");
        if (definitionJson.Length > 1_000_000) throw new ApprovalException("设计草稿超过 1000000 字符");
        var flow = FlowDefinition.Parse(definitionJson);
        var json = SerializeDefinition(flow); // Incomplete approval designs can still be saved.
        if (sourceDefinitionId is { } source && !await _db.Set<ApprovalDefinition>().AnyAsync(d => d.Id == source && d.Key == key && d.IsPublished, token))
            throw new ApprovalException("来源版本不存在") { Code = ApprovalErrorCode.NotFound };
        var management = await GetOrCreateManagementAsync(key, name, token); CheckRevision(management, expectedRevision);
        management.Name = name; management.DraftJson = json; management.DraftSourceDefinitionId = sourceDefinitionId;
        management.Category = CategoryOf(flow);
        Audit(management, DefinitionAuditAction.DraftSaved, source: sourceDefinitionId, json: json);
        management.DraftRevision = management.Revision;
        await SaveGovernanceAsync(token); return await DescribeAsync(management, token);
    }, ct);

    public Task<ApprovalDefinition> PublishDesignDraftAsync(string key, long expectedRevision, string? changeNote = null,
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionPublish, new() { DefinitionKey = key }, ct: token);
        var management = await _db.Set<ApprovalDefinitionManagement>().SingleOrDefaultAsync(x => x.Key == key, token)
            ?? throw new ApprovalException("请先保存设计草稿") { Code = ApprovalErrorCode.NotFound };
        CheckRevision(management, expectedRevision);
        if (management.DraftJson is null) throw new ApprovalException("请先保存设计草稿");
        var flow = FlowDefinition.Parse(management.DraftJson); var errors = ValidateCore(flow);
        if (errors.Count > 0) throw new ApprovalException("流程定义校验失败：" + string.Join("；", errors)) { Code = ApprovalErrorCode.InvalidDefinition };
        return await PublishManagedAsync(management, management.Name, flow, management.DraftSourceDefinitionId, changeNote, true, token);
    }, ct);

    public Task<DefinitionGovernance> SetEnabledAsync(string key, bool enabled, long expectedRevision, string? changeNote = null,
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionDisable, new() { DefinitionKey = key }, ct: token);
        ValidateNote(changeNote);
        var latest = await _db.Set<ApprovalDefinition>().Where(d => d.Key == key && d.IsPublished).OrderByDescending(d => d.Version).FirstOrDefaultAsync(token)
            ?? throw new ApprovalException("流程尚未发布") { Code = ApprovalErrorCode.NotFound };
        var management = await GetOrCreateManagementAsync(key, latest.Name, token); CheckRevision(management, expectedRevision);
        management.IsEnabled = enabled; Audit(management, enabled ? DefinitionAuditAction.Enabled : DefinitionAuditAction.Disabled,
            management.ActiveDefinitionId, note: changeNote);
        await SaveGovernanceAsync(token); return await DescribeAsync(management, token, includeDraft: false);
    }, ct);

    public Task<DefinitionGovernance> SelectVersionAsync(string key, Guid definitionId, long expectedRevision, string? changeNote = null,
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Definitions, async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionPublish, new() { DefinitionKey = key }, ct: token);
        ValidateNote(changeNote);
        var version = await _db.Set<ApprovalDefinition>().SingleOrDefaultAsync(d => d.Id == definitionId && d.Key == key && d.IsPublished, token)
            ?? throw new ApprovalException("目标版本不存在") { Code = ApprovalErrorCode.NotFound };
        var management = await GetOrCreateManagementAsync(key, version.Name, token); CheckRevision(management, expectedRevision);
        var previous = management.ActiveDefinitionId; management.ActiveDefinitionId = version.Id;
        if (management.DraftJson is null) management.Category = version.Category;
        Audit(management, DefinitionAuditAction.VersionSelected, version.Id, previous, version.Version, changeNote);
        await SaveGovernanceAsync(token); return await DescribeAsync(management, token, includeDraft: false);
    }, ct);

    private async Task<IReadOnlyDictionary<string, bool>> GovernanceCapabilitiesAsync(string key, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        foreach (var code in new[] { ApprovalPermissions.DefinitionDesign, ApprovalPermissions.DefinitionPublish, ApprovalPermissions.DefinitionDisable })
            result[code] = await _authorization.CanAsync(code, new() { DefinitionKey = key }, ct: ct);
        return result;
    }

    private async Task<DefinitionGovernance> DescribeAsync(ApprovalDefinitionManagement value, CancellationToken ct, bool includeDraft = true) =>
        new(value.Key, value.Name, includeDraft ? value.DraftJson : null, value.Revision, value.LastVersion, value.IsEnabled, value.ActiveDefinitionId,
            value.DraftSourceDefinitionId, value.DraftJson is not null && value.PublishedDraftRevision != value.DraftRevision,
            value.UpdatedAt, value.UpdatedBy, await GovernanceCapabilitiesAsync(value.Key, ct))
        { Category = value.Category };

    public async Task<DefinitionGovernance?> GetGovernanceAsync(string key, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, ct: ct);
        var managed = await _db.Set<ApprovalDefinitionManagement>().AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
        return managed is null ? null : await DescribeAsync(managed, ct);
    }

    public async Task<List<DefinitionGovernanceSummary>> ListGovernanceAsync(string? category = null, bool uncategorized = false, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, ct: ct);
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category is not null && uncategorized) throw new ApprovalException("分类与未分类筛选不能同时指定");
        var management = await _db.Set<ApprovalDefinitionManagement>().AsNoTracking()
            .Where(d => (category == null || d.Category == category) && (!uncategorized || d.Category == null))
            .OrderBy(d => d.Key)
            .Select(d => new
            {
                d.Key,
                d.Name,
                d.Revision,
                d.LastVersion,
                d.IsEnabled,
                d.ActiveDefinitionId,
                d.UpdatedAt,
                d.Category,
                HasDraftChanges = d.DraftJson != null && d.PublishedDraftRevision != d.DraftRevision
            })
            .ToListAsync(ct);
        var result = new List<DefinitionGovernanceSummary>();
        foreach (var state in management)
        {
            if (!await _authorization.CanAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = state.Key }, ct: ct)) continue;
            result.Add(new(state.Key, state.Name, state.Revision, state.LastVersion, state.IsEnabled, state.ActiveDefinitionId,
                state.HasDraftChanges, state.UpdatedAt, await GovernanceCapabilitiesAsync(state.Key, ct))
            { Category = state.Category });
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default) =>
        (await ListGovernanceAsync(ct: ct)).Where(d => d.Category is not null).Select(d => d.Category!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public async Task<PagedResult<ApprovalDefinitionAudit>> ListAuditAsync(string key, PageRequest? page = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, ct: ct);
        return await _db.Set<ApprovalDefinitionAudit>().AsNoTracking().Where(a => a.Key == key).OrderByDescending(a => a.Revision).ToPagedAsync(page ?? new(), ct);
    }
}
