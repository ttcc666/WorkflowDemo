using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

public sealed record DefinitionGovernance(string Key, string Name, string? DraftJson, long Revision, int LastVersion,
    bool IsEnabled, Guid? ActiveDefinitionId, Guid? DraftSourceDefinitionId, bool HasDraftChanges,
    DateTime? UpdatedAt, string? UpdatedBy, IReadOnlyDictionary<string, bool> Capabilities);
public sealed record DefinitionGovernanceSummary(string Key, string Name, long Revision, int LastVersion,
    bool IsEnabled, Guid? ActiveDefinitionId, bool HasDraftChanges, DateTime? UpdatedAt,
    IReadOnlyDictionary<string, bool> Capabilities);

public sealed partial class ApprovalDefinitionService
{
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
        // Max is used only to initialize a legacy counter. The unique management row and Revision
        // then serialize publication; concurrent initialization conflicts roll back the whole unit.
        var versions = _db.Set<ApprovalDefinition>().Where(d => d.Key == key);
        var latest = await versions.Where(d => d.IsPublished).OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
        managed = new() { Id = _unit.NewId("DefinitionManagement"), Key = key, Name = latest?.Name ?? name,
            LastVersion = await versions.MaxAsync(d => (int?)d.Version, ct) ?? 0,
            ActiveDefinitionId = latest?.Id, UpdatedBy = _accessor.ActorId, UpdatedAt = _clock.GetUtcNow().UtcDateTime };
        _db.Add(managed);
        return managed;
    }
    private void Audit(ApprovalDefinitionManagement management, DefinitionAuditAction action, Guid? definitionId = null,
        Guid? source = null, int? version = null, string? note = null, string? json = null)
    {
        ValidateNote(note);
        management.Revision = checked(management.Revision + 1);
        management.UpdatedBy = _accessor.ActorId; management.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        _db.Add(new ApprovalDefinitionAudit { Id = _unit.NewId("DefinitionAudit"), Key = management.Key,
            Revision = management.Revision, Action = action, ActorId = management.UpdatedBy, CreatedAt = management.UpdatedAt,
            DefinitionId = definitionId, SourceDefinitionId = source, Version = version, ChangeNote = note,
            DefinitionHash = json is null ? null : ApprovalCommandHash.Of(json) });
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
        var definition = new ApprovalDefinition { Id = _unit.NewId("ApprovalDefinitionService"), Key = management.Key,
            Name = name, Version = checked(management.LastVersion + 1), DefinitionJson = flow.ToJson(), IsPublished = true,
            CreatedAt = _clock.GetUtcNow().UtcDateTime };
        _db.Add(definition); management.LastVersion = definition.Version; management.ActiveDefinitionId = definition.Id;
        if (!fromDraft && management.DraftJson is null) management.Name = name;
        Audit(management, DefinitionAuditAction.Published, definition.Id, source, definition.Version, note, definition.DefinitionJson);
        if (fromDraft) { management.PublishedDraftRevision = management.DraftRevision; management.DraftSourceDefinitionId = definition.Id; }
        await SaveGovernanceAsync(ct);
        return definition;
    }
    public Task<DefinitionGovernance> SaveDesignDraftAsync(string key, string name, string definitionJson, long expectedRevision,
        Guid? sourceDefinitionId = null, CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = key }, ct: token);
        ValidateIdentity(key, name);
        if (string.IsNullOrWhiteSpace(definitionJson)) throw new ApprovalException("设计草稿 JSON 不能为空");
        if (definitionJson.Length > 1_000_000) throw new ApprovalException("设计草稿超过 1000000 字符");
        var json = FlowDefinition.Parse(definitionJson).ToJson(); // Parse only: incomplete designs can be saved.
        if (sourceDefinitionId is { } source && !await _db.Set<ApprovalDefinition>().AnyAsync(d => d.Id == source && d.Key == key && d.IsPublished, token))
            throw new ApprovalException("来源版本不存在") { Code = ApprovalErrorCode.NotFound };
        var management = await GetOrCreateManagementAsync(key, name, token); CheckRevision(management, expectedRevision);
        management.Name = name; management.DraftJson = json; management.DraftSourceDefinitionId = sourceDefinitionId;
        Audit(management, DefinitionAuditAction.DraftSaved, source: sourceDefinitionId, json: json);
        management.DraftRevision = management.Revision;
        await SaveGovernanceAsync(token); return await DescribeAsync(management, token);
    }, ct);

    public Task<ApprovalDefinition> PublishDesignDraftAsync(string key, long expectedRevision, string? changeNote = null,
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
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
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
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
        CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionPublish, new() { DefinitionKey = key }, ct: token);
        ValidateNote(changeNote);
        var version = await _db.Set<ApprovalDefinition>().SingleOrDefaultAsync(d => d.Id == definitionId && d.Key == key && d.IsPublished, token)
            ?? throw new ApprovalException("目标版本不存在") { Code = ApprovalErrorCode.NotFound };
        var management = await GetOrCreateManagementAsync(key, version.Name, token); CheckRevision(management, expectedRevision);
        var previous = management.ActiveDefinitionId; management.ActiveDefinitionId = version.Id;
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
            value.UpdatedAt, value.UpdatedBy, await GovernanceCapabilitiesAsync(value.Key, ct));
    public async Task<DefinitionGovernance?> GetGovernanceAsync(string key, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, ct: ct);
        var managed = await _db.Set<ApprovalDefinitionManagement>().AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
        if (managed is not null) return await DescribeAsync(managed, ct);
        var latest = await _db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.Key == key && d.IsPublished).OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
        return latest is null ? null : new(key, latest.Name, null, 0, latest.Version, true, latest.Id, null, false, null, null, await GovernanceCapabilitiesAsync(key, ct));
    }
    public async Task<List<DefinitionGovernanceSummary>> ListGovernanceAsync(CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, ct: ct);
        var keys = await _db.Set<ApprovalDefinition>().Select(d => d.Key).Union(_db.Set<ApprovalDefinitionManagement>().Select(d => d.Key)).OrderBy(k => k).ToListAsync(ct);
        var result = new List<DefinitionGovernanceSummary>();
        foreach (var key in keys)
        {
            if (!await _authorization.CanAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, ct: ct)) continue;
            if (await GetGovernanceAsync(key, ct) is { } state)
                result.Add(new(state.Key, state.Name, state.Revision, state.LastVersion, state.IsEnabled, state.ActiveDefinitionId,
                    state.HasDraftChanges, state.UpdatedAt, state.Capabilities));
        }
        return result;
    }
    public async Task<PagedResult<ApprovalDefinitionAudit>> ListAuditAsync(string key, PageRequest? page = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionKey = key }, ct: ct);
        return await _db.Set<ApprovalDefinitionAudit>().AsNoTracking().Where(a => a.Key == key).OrderByDescending(a => a.Revision).ToPagedAsync(page ?? new(), ct);
    }
}
