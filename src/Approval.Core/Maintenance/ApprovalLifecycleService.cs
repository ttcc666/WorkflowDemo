using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Files;
using Approval.Core.Persistence;
using Approval.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Maintenance;

public sealed record MaintenancePreview(MaintenanceTarget Target, MaintenanceMode Mode, bool Enabled, bool Configured,
    DateTime? Cutoff, string PolicyHash, int Eligible, IReadOnlyList<Guid> SampleIds);
public sealed record MaintenanceJobInfo(Guid Id, MaintenanceTarget Target, MaintenanceMode Mode, MaintenanceJobStatus Status,
    int CompletedBatches, int Processed, string? LastErrorCode, DateTime CreatedAt);
public sealed record MaintenanceBatchInfo(int? Batch, string Action, int Count, string ActorId, DateTime CommittedAt);
public sealed record ArchiveSummary(Guid Id, MaintenanceTarget Target, Guid ResourceId, string? DefinitionKey,
    string? BusinessType, DateTime ArchivedAt, DateTime? PurgedAt, DateTime? PayloadExpiredAt, string SnapshotHash);

public sealed partial class ApprovalLifecycleService(ApprovalDbContextAccessor accessor, IServiceScopeFactory scopes,
    IApprovalTenantContext tenant, ApprovalOptions options, TimeProvider clock, IApprovalFileStore? store = null, IApprovalArchiveQueryScope? archiveScope = null)
{
    private readonly ApprovalDbContext _db = (ApprovalDbContext)accessor.Context;
    private readonly ApprovalUnitOfWork _unit = accessor.UnitOfWork;
    private Approval.Core.Authorization.IApprovalAuthorization Auth => accessor.Authorization;
    private ApprovalLifecycleOptions Policy => options.Lifecycle;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<IQueryable<ApprovalArchiveRecord>> ArchivesAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(options.PermissionTimeout, clock);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { return await (archiveScope ?? throw new InvalidOperationException("未注册归档数据范围")).ApplyAsync(new(tenant.TenantId, accessor.ActorId, accessor.IsSystem), _db.Set<ApprovalArchiveRecord>().AsNoTracking(), token.Token).WaitAsync(token.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ApprovalException("归档数据范围暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static MaintenanceJobInfo Info(ApprovalMaintenanceJob j) => new(j.Id, j.Target, j.Mode, j.Status, j.CompletedBatches, j.Processed, j.LastErrorCode, j.CreatedAt);
    private string Hash(MaintenanceTarget target, MaintenanceMode mode) => ApprovalCommandHash.Payload(new { target, mode, Policy.Enabled, Policy.AllowDeletion, Retention = Policy.Retention(target, mode)?.Ticks, Policy.BatchSize });
    private void CheckPolicy(MaintenanceTarget target, MaintenanceMode mode)
    {
        if (!Enum.IsDefined(target) || !Enum.IsDefined(mode)) throw new ApprovalException("未知维护类型");
        if (!Policy.Enabled || (mode == MaintenanceMode.Purge && !Policy.AllowDeletion)) throw new ApprovalException("维护或删除策略未启用");
        if (Policy.Retention(target, mode) is null) throw new ApprovalException("此类型未配置保留期");
        if (target == MaintenanceTarget.FailedOutbox && mode == MaintenanceMode.Purge) throw new ApprovalException("失败 Outbox 只能归档，不能按年龄删除");
        if (target == MaintenanceTarget.ArchivedPayloads && mode != MaintenanceMode.Purge) throw new ApprovalException("归档详情到期只支持清理");
    }
    public async Task<MaintenancePreview> PreviewAsync(MaintenanceTarget target, MaintenanceMode mode, CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, ct: ct);
        if (!Enum.IsDefined(target) || !Enum.IsDefined(mode)) throw new ApprovalException("未知维护类型");
        var age = Policy.Retention(target, mode); var cutoff = age is null ? (DateTime?)null : Now - age.Value;
        var set = cutoff is null || target == MaintenanceTarget.FailedOutbox && mode == MaintenanceMode.Purge
            ? new Candidates(0, []) : await SelectAsync(target, mode, cutoff.Value, Math.Min(20, Policy.BatchSize), ct);
        return new(target, mode, Policy.Enabled && (mode != MaintenanceMode.Purge || Policy.AllowDeletion), cutoff is not null,
            cutoff, Hash(target, mode), set.Total, set.Items.Select(x => x.Id).ToList());
    }
    public Task<MaintenanceJobInfo> CreateJobAsync(MaintenanceTarget target, MaintenanceMode mode, string policyHash, CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveExecute, ct: token);
        if (mode == MaintenanceMode.Purge) await Auth.RequireAsync(ApprovalPermissions.ArchivePurge, ct: token);
        CheckPolicy(target, mode);
        if (Hash(target, mode) != policyHash) throw new ApprovalException("保留策略已变化，请重新预览") { Code = ApprovalErrorCode.Conflict };
        var job = new ApprovalMaintenanceJob { Id = Guid.NewGuid(), Target = target, Mode = mode, ActorId = accessor.ActorId,
            CreatedAt = Now, Cutoff = Now - Policy.Retention(target, mode)!.Value, PolicyHash = policyHash };
        _db.Add(job); await _db.SaveChangesAsync(token); return Info(job);
    }, ct);
    public async Task<List<MaintenanceJobInfo>> ListJobsAsync(CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, ct: ct);
        return (await _db.Set<ApprovalMaintenanceJob>().AsNoTracking().OrderByDescending(j => j.CreatedAt).ThenBy(j => j.Id).Take(100).ToListAsync(ct)).Select(Info).ToList();
    }
    public async Task<PagedResult<MaintenanceBatchInfo>> ListBatchesAsync(Guid id, PageRequest? page = null, CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, ct: ct);
        if (!await _db.Set<ApprovalMaintenanceJob>().AnyAsync(j => j.Id == id, ct)) throw new ApprovalException("维护任务不存在") { Code = ApprovalErrorCode.NotFound };
        return await _db.Set<ApprovalMaintenanceAudit>().AsNoTracking().Where(a => a.JobId == id).OrderBy(a => a.Batch).ThenBy(a => a.Id)
            .Select(a => new MaintenanceBatchInfo(a.Batch, a.Action, a.Count, a.ActorId, a.CreatedAt)).ToPagedAsync(page ?? new(), ct);
    }
    public async Task<PagedResult<ArchiveSummary>> ListArchivesAsync(PageRequest? page = null, CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, ct: ct);
        return await (await ArchivesAsync(ct)).OrderByDescending(a => a.ArchivedAt).ThenBy(a => a.Id)
            .Select(a => new ArchiveSummary(a.Id, a.Target, a.ResourceId, a.DefinitionKey, a.BusinessType, a.ArchivedAt, a.PurgedAt, a.PayloadExpiredAt, a.SnapshotHash)).ToPagedAsync(page ?? new(), ct);
    }
    public async Task<string> ExportArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var resource = new ApprovalResource { ArchiveId = id };
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, resource, ct: ct);
        await Auth.RequireAsync(ApprovalPermissions.ArchiveReadSensitive, resource, ct: ct);
        var record = await _db.Set<ApprovalArchiveRecord>().AsNoTracking().SingleAsync(a => a.Id == id, ct);
        if (record.PayloadExpiredAt is not null) throw new ApprovalException("详细历史已经结束保留期；仍可查询归档标记") { Code = ApprovalErrorCode.ReplayExpired };
        return record.SnapshotJson;
    }
    public async Task<Stream> OpenArchiveFileAsync(Guid id, Guid fileId, CancellationToken ct = default)
    {
        var resource = new ApprovalResource { ArchiveId = id };
        await Auth.RequireAsync(ApprovalPermissions.ArchiveRead, resource, ct: ct);
        await Auth.RequireAsync(ApprovalPermissions.ArchiveReadSensitive, resource, ct: ct);
        var file = await _db.Set<ApprovalFile>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId && f.ArchiveId == id && !f.DeletionPending, ct)
            ?? throw new ApprovalException("归档附件不存在") { Code = ApprovalErrorCode.NotFound };
        return await (store ?? throw new InvalidOperationException("没有附件存储")).OpenReadAsync(file.StorageKey, ct);
    }
    private sealed record Candidate(Guid Id, object Entity);
    private sealed record Candidates(int Total, List<Candidate> Items);
    private async Task<Candidates> TakeAsync<T>(IQueryable<T> query, Func<T, Guid> id, int take, CancellationToken ct) where T : class =>
        new(await query.CountAsync(ct), (await query.Take(take).ToListAsync(ct)).Select(x => new Candidate(id(x), x)).ToList());
    private async Task<Candidates> SelectAsync(MaintenanceTarget target, MaintenanceMode mode, DateTime cutoff, int take, CancellationToken ct)
    {
        var holds = _db.Set<ApprovalRetentionHold>(); var archives = _db.Set<ApprovalArchiveRecord>();
        var visible = await Auth.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        var visibleIds = visible.Select(i => i.Id);
        var running = _db.Set<ApprovalInstance>().Where(i => i.Status == InstanceStatus.Running).Select(i => i.Id);
        var heldInstances = holds.Where(h => h.Target == MaintenanceTarget.Instances).Select(h => h.ResourceId);
        var prior = archives.Where(a => a.Target == target);
        switch (target)
        {
            case MaintenanceTarget.Instances:
                var instances = visible.Where(i => i.Status != InstanceStatus.Running && i.CompletedAt != null && i.CompletedAt < cutoff && !heldInstances.Contains(i.Id));
                if (mode == MaintenanceMode.Archive) instances = instances.Where(i => !prior.Any(a => a.ResourceId == i.Id));
                else instances = instances.Where(i => prior.Any(a => a.ResourceId == i.Id && a.PurgedAt == null)
                    && !_db.Set<ApprovalBusinessSlot>().Any(s => s.InstanceId == i.Id)
                    && !_db.Set<ApprovalTask>().Any(t => t.InstanceId == i.Id && (t.Status == ApprovalTaskStatus.Pending || t.Status == ApprovalTaskStatus.Waiting || t.Status == ApprovalTaskStatus.Suspended))
                    && !_db.Set<ApprovalOutboxMessage>().Any(o => o.InstanceId == i.Id && o.Status != OutboxStatus.Succeeded)
                    && !holds.Any(h => h.Target == MaintenanceTarget.Operations && _db.Set<ApprovalOperation>().Any(o => o.Id == h.ResourceId && o.InstanceId == i.Id))
                    && !holds.Any(h => h.Target == MaintenanceTarget.ReadMessages && _db.Set<ApprovalMessage>().Any(m => m.Id == h.ResourceId && m.InstanceId == i.Id))
                    && !holds.Any(h => h.Target == MaintenanceTarget.SucceededOutbox && _db.Set<ApprovalOutboxMessage>().Any(o => o.Id == h.ResourceId && o.InstanceId == i.Id)));
                return await TakeAsync(instances.OrderBy(i => i.CompletedAt).ThenBy(i => i.Id), i => i.Id, take, ct);
            case MaintenanceTarget.ReadMessages:
                var messages = _db.Set<ApprovalMessage>().Where(m => m.IsRead && m.ReadAt < cutoff && !holds.Any(h => h.Target == target && h.ResourceId == m.Id)
                    && (m.InstanceId == null || visibleIds.Contains(m.InstanceId.Value) && !running.Contains(m.InstanceId.Value) && !heldInstances.Contains(m.InstanceId.Value)));
                if (mode == MaintenanceMode.Archive) messages = messages.Where(m => !prior.Any(a => a.ResourceId == m.Id));
                return await TakeAsync(messages.OrderBy(m => m.ReadAt).ThenBy(m => m.Id), m => m.Id, take, ct);
            case MaintenanceTarget.SucceededOutbox:
            case MaintenanceTarget.FailedOutbox:
                var status = target == MaintenanceTarget.SucceededOutbox ? OutboxStatus.Succeeded : OutboxStatus.Failed;
                var outbox = _db.Set<ApprovalOutboxMessage>().Where(o => o.Status == status && o.CompletedAt < cutoff && o.LeaseId == null
                    && !holds.Any(h => h.Target == target && h.ResourceId == o.Id)
                    && (o.InstanceId == null || visibleIds.Contains(o.InstanceId.Value) && !running.Contains(o.InstanceId.Value) && !heldInstances.Contains(o.InstanceId.Value)));
                if (mode == MaintenanceMode.Archive) outbox = outbox.Where(o => !prior.Any(a => a.ResourceId == o.Id));
                return await TakeAsync(outbox.OrderBy(o => o.CompletedAt).ThenBy(o => o.Id), o => o.Id, take, ct);
            case MaintenanceTarget.BusinessDrafts:
                var drafts = _db.Set<ApprovalDraft>().Where(d => d.UpdatedAt < cutoff && !holds.Any(h => h.Target == target && h.ResourceId == d.Id));
                if (mode == MaintenanceMode.Archive) drafts = drafts.Where(d => !prior.Any(a => a.ResourceId == d.Id));
                return await TakeAsync(drafts.OrderBy(d => d.UpdatedAt).ThenBy(d => d.Id), d => d.Id, take, ct);
            case MaintenanceTarget.DesignDrafts:
                var designs = _db.Set<ApprovalDefinitionManagement>().Where(d => d.DraftJson != null && d.UpdatedAt < cutoff && !holds.Any(h => h.Target == target && h.ResourceId == d.Id));
                if (mode == MaintenanceMode.Archive) designs = designs.Where(d => !prior.Any(a => a.ResourceId == d.Id && a.SourceVersion == d.DraftRevision));
                return await TakeAsync(designs.OrderBy(d => d.UpdatedAt).ThenBy(d => d.Id), d => d.Id, take, ct);
            case MaintenanceTarget.Operations:
                var ops = _db.Set<ApprovalOperation>().Where(o => !o.IsTombstone && o.CommittedAt < cutoff && !holds.Any(h => h.Target == target && h.ResourceId == o.Id)
                    && (o.InstanceId == null || visibleIds.Contains(o.InstanceId.Value) && !running.Contains(o.InstanceId.Value) && !heldInstances.Contains(o.InstanceId.Value)));
                if (mode == MaintenanceMode.Archive) ops = ops.Where(o => !prior.Any(a => a.ResourceId == o.Id));
                return await TakeAsync(ops.OrderBy(o => o.CommittedAt).ThenBy(o => o.Id), o => o.Id, take, ct);
            case MaintenanceTarget.UnboundFiles:
                var files = _db.Set<ApprovalFile>().Where(f => f.InstanceId == null && f.DraftId == null && f.ArchiveId == null && !f.DeletionPending
                    && f.UploadedAt < cutoff && !holds.Any(h => h.Target == target && h.ResourceId == f.Id));
                if (mode == MaintenanceMode.Archive) files = files.Where(f => !prior.Any(a => a.ResourceId == f.Id));
                return await TakeAsync(files.OrderBy(f => f.UploadedAt).ThenBy(f => f.Id), f => f.Id, take, ct);
            case MaintenanceTarget.ArchivedPayloads:
                if (mode != MaintenanceMode.Purge) return new(0, []);
                var payloads = (await ArchivesAsync(ct)).Where(a => a.PurgedAt != null && a.PurgedAt < cutoff && a.PayloadExpiredAt == null
                    && !holds.Any(h => h.Target == target && h.ResourceId == a.Id)
                    && !(a.Target == MaintenanceTarget.Instances && heldInstances.Contains(a.ResourceId))
                    && !holds.Any(h => h.Target == a.Target && h.ResourceId == a.ResourceId)
                    && !_db.Set<ApprovalFile>().Any(f => f.ArchiveId == a.Id && holds.Any(h => h.Target == MaintenanceTarget.UnboundFiles && h.ResourceId == f.Id)));
                return await TakeAsync(payloads.OrderBy(a => a.PurgedAt).ThenBy(a => a.Id), a => a.Id, take, ct);
            default: throw new ApprovalException("未知维护类型");
        }
    }
}
