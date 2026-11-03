using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Approval.Core.Maintenance;

public sealed partial class ApprovalLifecycleService
{
    private void Audit(string action, MaintenanceTarget target, Guid? resource = null, string? reason = null, ApprovalMaintenanceJob? job = null, int count = 0)
        => _db.Add(new ApprovalMaintenanceAudit { Id = Guid.NewGuid(), Action = action, Target = target, ResourceId = resource, Reason = reason,
            ActorId = accessor.ActorId, CreatedAt = Now, JobId = job?.Id, Batch = job?.CompletedBatches, Count = count });
    public Task<bool> SetHoldAsync(MaintenanceTarget target, Guid resourceId, bool held, string reason, CancellationToken ct = default) => _unit.ExecuteCommandAsync(async token =>
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveExecute, ct: token);
        reason ??= string.Empty;
        if (!Enum.IsDefined(target) || reason.Length > 1000 || held && string.IsNullOrWhiteSpace(reason)) throw new ApprovalException("保留标记参数无效");
        var row = await ResourceEntityAsync(target, resourceId, token);
        var record = row is null ? await _db.Set<ApprovalArchiveRecord>().FirstOrDefaultAsync(a => a.Target == target && a.ResourceId == resourceId, token) : null;
        if (row is null && record is null) throw new ApprovalException("资源不存在") { Code = ApprovalErrorCode.NotFound };
        await Auth.RequireAsync(ApprovalPermissions.ArchiveExecute, record is not null ? new() { ArchiveId = record.Id } : Resource(row!), ct: token);
        if (row is ApprovalFile file && file.DeletionPending)
        {
            var deletion = await _db.Set<ApprovalFileDeletion>().SingleAsync(d => d.FileId == file.Id, token);
            if (deletion.StartedAt is not null) throw new ApprovalException("附件删除已经开始，不能承诺恢复保留") { Code = ApprovalErrorCode.Conflict };
            deletion.Status = FileDeletionStatus.Cancelled; deletion.ConcurrencyStamp++; file.DeletionPending = false;
        }
        var existing = await _db.Set<ApprovalRetentionHold>().SingleOrDefaultAsync(h => h.Target == target && h.ResourceId == resourceId, token);
        if (held && existing is null) _db.Add(new ApprovalRetentionHold { Id = Guid.NewGuid(), Target = target, ResourceId = resourceId, Reason = reason, ActorId = accessor.ActorId, CreatedAt = Now });
        if (!held && existing is not null) _db.Remove(existing);
        if (row is ApprovalInstance instance) instance.ConcurrencyStamp++;
        if (row is ApprovalFile heldFile) heldFile.ConcurrencyStamp++;
        if (row is ApprovalDefinitionManagement heldDesign) heldDesign.Revision++;
        if (row is ApprovalArchiveRecord heldArchive) heldArchive.ConcurrencyStamp++;
        if (row is ApprovalOperation heldOperation) _db.Entry(heldOperation).Property(o => o.RequestHash).IsModified = true;
        if (row is ApprovalMessage heldMessage) _db.Entry(heldMessage).Property(m => m.Title).IsModified = true;
        if (row is ApprovalOutboxMessage heldOutbox) _db.Entry(heldOutbox).Property(o => o.Target).IsModified = true;
        if (row is ApprovalDraft heldDraft) _db.Entry(heldDraft).Property(d => d.FormDataJson).IsModified = true;
        if (record is not null) record.ConcurrencyStamp++;
        Audit(held ? "HoldSet" : "HoldReleased", target, resourceId, reason);
        try { await _db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new ApprovalException("资源已被其他维护操作修改，请重新读取", ex) { Code = ApprovalErrorCode.Conflict }; }
        return held;
    }, ct);
    private async Task<object?> ResourceEntityAsync(MaintenanceTarget target, Guid id, CancellationToken ct) => target switch
    {
        MaintenanceTarget.Instances => await _db.Set<ApprovalInstance>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.ReadMessages => await _db.Set<ApprovalMessage>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.SucceededOutbox or MaintenanceTarget.FailedOutbox => await _db.Set<ApprovalOutboxMessage>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.BusinessDrafts => await _db.Set<ApprovalDraft>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.DesignDrafts => await _db.Set<ApprovalDefinitionManagement>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.Operations => await _db.Set<ApprovalOperation>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.UnboundFiles => await _db.Set<ApprovalFile>().SingleOrDefaultAsync(x => x.Id == id, ct),
        MaintenanceTarget.ArchivedPayloads => await _db.Set<ApprovalArchiveRecord>().SingleOrDefaultAsync(x => x.Id == id, ct),
        _ => null
    };
    private static ApprovalResource Resource(object entity) => entity switch
    {
        ApprovalInstance i => new() { InstanceId = i.Id }, ApprovalMessage m => new() { MessageId = m.Id },
        ApprovalOutboxMessage o => new() { OutboxId = o.Id }, ApprovalDraft d => new() { DraftId = d.Id },
        ApprovalDefinitionManagement d => new() { DefinitionKey = d.Key }, ApprovalFile f => new() { FileId = f.Id },
        ApprovalOperation o => new() { InstanceId = o.InstanceId, DefinitionId = o.DefinitionId },
        ApprovalArchiveRecord a => new() { ArchiveId = a.Id }, _ => new()
    };
    private AsyncServiceScope OwnerScope(ApprovalMaintenanceJob job)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(new(job.ActorId, job.SystemRequested),
            job.SystemRequested ? [ApprovalPermissions.FileCleanup, ApprovalPermissions.MaintenanceRun] : []);
        return scope;
    }
    private async Task<T> RunOwnedAsync<T>(ApprovalMaintenanceJob job, Func<ApprovalLifecycleService, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        await using var strategyScope = OwnerScope(job);
        return await strategyScope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var scope = OwnerScope(job);
            var worker = scope.ServiceProvider.GetRequiredService<ApprovalLifecycleService>();

            worker._db.TransactionState.Serializable = true;
            return await worker._unit.ExecuteAsync(token => operation(worker, token), ct);
        });
    }
    public async Task<MaintenanceJobInfo> RunJobAsync(Guid id, int maxBatches = 1, CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.ArchiveExecute, ct: ct);
        var job = await _db.Set<ApprovalMaintenanceJob>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct)
            ?? throw new ApprovalException("维护任务不存在") { Code = ApprovalErrorCode.NotFound };
        if (job.Mode == MaintenanceMode.Purge) await Auth.RequireAsync(ApprovalPermissions.ArchivePurge, ct: ct);
        return await ProcessJobAsync(job, Math.Clamp(maxBatches, 1, Policy.MaxBatchesPerRun), ct);
    }
    internal async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        await Auth.RequireAsync(ApprovalPermissions.MaintenanceRun, ct: ct);
        if (!Policy.Enabled) return 0;
        var jobs = await _db.Set<ApprovalMaintenanceJob>().AsNoTracking().Where(j => j.Status == MaintenanceJobStatus.Pending || j.Status == MaintenanceJobStatus.Running)
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).Take(2).ToListAsync(ct);
        foreach (var job in jobs) await ProcessJobAsync(job, 1, ct);
        return jobs.Count;
    }
    private async Task<MaintenanceJobInfo> ProcessJobAsync(ApprovalMaintenanceJob job, int maxBatches, CancellationToken ct)
    {
        CheckPolicy(job.Target, job.Mode);
        using var run = gate.TryEnter(job.Id) ?? throw new ApprovalException("此维护任务正在执行") { Code = ApprovalErrorCode.Conflict };
        bool claimed;
        try { claimed = await RunOwnedAsync(job, async (w, token) =>
        {
            var current = await w._db.Set<ApprovalMaintenanceJob>().SingleAsync(j => j.Id == job.Id, token);
            if (current.Status == MaintenanceJobStatus.Completed) return false;
            await w.AuthorizeJobAsync(current, null, token);
            if (current.PolicyHash != w.Hash(current.Target, current.Mode)) throw new ApprovalException("保留策略变化，请重新预览并创建任务") { Code = ApprovalErrorCode.Conflict };
            current.Status = MaintenanceJobStatus.Running; current.LastErrorCode = null; current.ConcurrencyStamp++;
            return true;
        }, ct); }
        catch (DbUpdateConcurrencyException) { claimed = false; }
        if (claimed)
        {
            try
            {
                for (var batch = 0; batch < maxBatches; batch++)
                    if (!await RunOwnedAsync(job, (w, token) => w.BatchAsync(job.Id, token), ct)) break;
                await RunOwnedAsync(job, async (w, token) => { var current = await w._db.Set<ApprovalMaintenanceJob>().SingleAsync(j => j.Id == job.Id, token);
                    if (current.Status == MaintenanceJobStatus.Running) { current.Status = MaintenanceJobStatus.Pending; current.ConcurrencyStamp++; } return true; }, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await RunOwnedAsync(job, async (w, token) => { var current = await w._db.Set<ApprovalMaintenanceJob>().SingleAsync(j => j.Id == job.Id, token);
                    current.Status = MaintenanceJobStatus.Failed; current.LastErrorCode = ex is ApprovalException a ? a.Code.ToString() : ex.GetType().Name; current.ConcurrencyStamp++; return true; }, ct);
                throw;
            }
        }
        return Info(await _db.Set<ApprovalMaintenanceJob>().AsNoTracking().SingleAsync(j => j.Id == job.Id, ct));
    }
    private async Task AuthorizeJobAsync(ApprovalMaintenanceJob job, ApprovalResource? resource, CancellationToken ct)
    {
        if (job.SystemRequested)
        {
            if (job.Target != MaintenanceTarget.UnboundFiles || job.Mode != MaintenanceMode.Purge) throw new ApprovalException("无效系统维护请求") { Code = ApprovalErrorCode.Forbidden };
            await Auth.RequireAsync(ApprovalPermissions.FileCleanup, ct: ct);
        }
        else { await Auth.RequireAsync(ApprovalPermissions.ArchiveExecute, resource, ct: ct); if (job.Mode == MaintenanceMode.Purge) await Auth.RequireAsync(ApprovalPermissions.ArchivePurge, resource, ct: ct); }
    }
    private async Task<bool> BatchAsync(Guid id, CancellationToken ct)
    {
        var job = await _db.Set<ApprovalMaintenanceJob>().SingleAsync(j => j.Id == id, ct);
        if (job.Status != MaintenanceJobStatus.Running) throw new DbUpdateConcurrencyException("维护任务状态已变化");
        CheckPolicy(job.Target, job.Mode); await AuthorizeJobAsync(job, null, ct);
        if (job.PolicyHash != Hash(job.Target, job.Mode)) throw new ApprovalException("保留策略已变化") { Code = ApprovalErrorCode.Conflict };
        var candidates = await SelectAsync(job.Target, job.Mode, job.Cutoff, Policy.BatchSize, ct);
        if (candidates.Items.Count == 0) { job.Status = MaintenanceJobStatus.Completed; job.ConcurrencyStamp++; return false; }
        foreach (var candidate in candidates.Items)
        {
            await AuthorizeJobAsync(job, Resource(candidate.Entity), ct);
            var record = await SnapshotAsync(job.Target, candidate, job.Mode == MaintenanceMode.Purge, ct);
            if (job.Mode == MaintenanceMode.Purge) { await PurgeAsync(job.Target, candidate, record, ct); record.PurgedAt ??= Now; record.ConcurrencyStamp++; }
        }
        job.Processed += candidates.Items.Count; job.CompletedBatches++; job.ConcurrencyStamp++;
        Audit("BatchCommitted", job.Target, job: job, count: candidates.Items.Count);
        return true;
    }
    private async Task<ApprovalArchiveRecord> SnapshotAsync(MaintenanceTarget target, Candidate candidate, bool purging, CancellationToken ct)
    {
        if (candidate.Entity is ApprovalArchiveRecord archivedPayload) return archivedPayload;
        var sourceVersion = candidate.Entity is ApprovalDefinitionManagement m ? m.DraftRevision : 0;
        var existing = await _db.Set<ApprovalArchiveRecord>().SingleOrDefaultAsync(a => a.Target == target && a.ResourceId == candidate.Id && a.SourceVersion == sourceVersion, ct);
        var resource = await Auth.ResolveResourceAsync(Resource(candidate.Entity), ct);
        object payload = candidate.Entity;
        if (purging && candidate.Entity is ApprovalOperation op) payload = ExpiredOperation(op);
        if (candidate.Entity is ApprovalInstance instance)
            payload = new { Instance = instance, Definition = await _db.Set<ApprovalDefinition>().AsNoTracking().SingleAsync(d => d.Id == instance.DefinitionId, ct),
                Tasks = await _db.Set<ApprovalTask>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct), Logs = await _db.Set<ApprovalLog>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct),
                Files = await _db.Set<ApprovalFile>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct), Messages = await _db.Set<ApprovalMessage>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct),
                Outbox = await _db.Set<ApprovalOutboxMessage>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct), Cc = await _db.Set<ApprovalCarbonCopy>().AsNoTracking().Where(t => t.InstanceId == instance.Id).ToListAsync(ct) };
        else if (candidate.Entity is ApprovalDraft draft)
            payload = new { Draft = draft, Files = await _db.Set<ApprovalFile>().AsNoTracking().Where(f => f.DraftId == draft.Id).ToListAsync(ct) };
        var json = JsonSerializer.Serialize(payload, payload.GetType(), Json);
        var record = existing ?? new ApprovalArchiveRecord { Id = Guid.NewGuid(), Target = target, ResourceId = candidate.Id, SourceVersion = sourceVersion,
            InstanceId = resource.InstanceId, DefinitionId = resource.DefinitionId, DefinitionKey = resource.DefinitionKey, BusinessType = candidate.Entity is ApprovalInstance i ? i.BusinessType : null,
            BusinessKey = candidate.Entity is ApprovalInstance i2 ? i2.BusinessKey : null, UserId = candidate.Entity is ApprovalMessage msg ? msg.UserId : candidate.Entity is ApprovalDraft dr ? dr.UserId : null,
            ArchivedAt = Now, SnapshotJson = json, SnapshotHash = ApprovalCommandHash.Of(json) };
        // A purge refreshes the snapshot inside the same transaction so final delivery/read state is preserved.
        record.SnapshotJson = json; record.SnapshotHash = ApprovalCommandHash.Of(json);
        if (existing is null) _db.Add(record);
        return record;
    }
    private async Task PurgeAsync(MaintenanceTarget target, Candidate candidate, ApprovalArchiveRecord record, CancellationToken ct)
    {
        // Data-scope subqueries can carry AsNoTracking into the candidate expression. Attach
        // before updating flags so tombstones/expiry checkpoints cannot silently disappear.
        if (_db.Entry(candidate.Entity).State == EntityState.Detached) _db.Attach(candidate.Entity);
        switch (candidate.Entity)
        {
            case ApprovalInstance instance:
                foreach (var file in await _db.Set<ApprovalFile>().Where(f => f.InstanceId == instance.Id).ToListAsync(ct)) { file.InstanceId = null; file.ArchiveId = record.Id; }
                foreach (var op in await _db.Set<ApprovalOperation>().Where(o => o.InstanceId == instance.Id).ToListAsync(ct))
                {
                    op.IsTombstone = true; op.ResultJson = string.Empty;
                    foreach (var archivedOp in await _db.Set<ApprovalArchiveRecord>().Where(a => a.Target == MaintenanceTarget.Operations && a.ResourceId == op.Id).ToListAsync(ct))
                    { archivedOp.SnapshotJson = JsonSerializer.Serialize(ExpiredOperation(op), Json); archivedOp.SnapshotHash = ApprovalCommandHash.Of(archivedOp.SnapshotJson); archivedOp.ConcurrencyStamp++; }
                }
                _db.RemoveRange(await _db.Set<ApprovalMessage>().Where(x => x.InstanceId == instance.Id).ToListAsync(ct));
                _db.RemoveRange(await _db.Set<ApprovalOutboxMessage>().Where(x => x.InstanceId == instance.Id).ToListAsync(ct));
                _db.RemoveRange(await _db.Set<ApprovalTask>().Where(x => x.InstanceId == instance.Id).ToListAsync(ct));
                // 只有已授权且已归档的维护清理可以删除在线轨迹；普通业务只能追加。
                _db.TransactionState.AllowLogDeletion = true;
                _db.RemoveRange(await _db.Set<ApprovalLog>().Where(x => x.InstanceId == instance.Id).ToListAsync(ct));
                _db.RemoveRange(await _db.Set<ApprovalCarbonCopy>().Where(x => x.InstanceId == instance.Id).ToListAsync(ct));
                _db.Remove(instance); break;
            case ApprovalDraft draft:
                foreach (var file in await _db.Set<ApprovalFile>().Where(f => f.DraftId == draft.Id).ToListAsync(ct)) { file.DraftId = null; file.ArchiveId = record.Id; }
                _db.Remove(draft); break;
            case ApprovalDefinitionManagement design:
                design.DraftJson = null; design.DraftSourceDefinitionId = null; design.Revision++; design.DraftRevision = design.Revision;
                design.UpdatedAt = Now; design.UpdatedBy = accessor.ActorId; break;
            case ApprovalOperation operation: operation.IsTombstone = true; operation.ResultJson = string.Empty; break;
            case ApprovalFile file:
                file.DeletionPending = true;
                var deletion = await _db.Set<ApprovalFileDeletion>().SingleOrDefaultAsync(d => d.FileId == file.Id, ct);
                if (deletion is null) _db.Add(new ApprovalFileDeletion { Id = Guid.NewGuid(), FileId = file.Id, StorageKey = file.StorageKey, NextAttemptAt = Now });
                else { deletion.Status = FileDeletionStatus.Pending; deletion.NextAttemptAt = Now; deletion.ConcurrencyStamp++; }
                break;
            case ApprovalMessage message: _db.Remove(message); break;
            case ApprovalOutboxMessage outbox when outbox.Status == OutboxStatus.Succeeded: _db.Remove(outbox); break;
            case ApprovalArchiveRecord archived:
                foreach (var file in await _db.Set<ApprovalFile>().Where(f => f.ArchiveId == archived.Id).ToListAsync(ct))
                {
                    file.ArchiveId = null;
                    await PurgeAsync(MaintenanceTarget.UnboundFiles, new(file.Id, file), archived, ct);
                }
                archived.SnapshotJson = string.Empty; archived.PayloadExpiredAt = Now; break;
            default: throw new ApprovalException("此资源不能清理");
        }
    }
    private static object ExpiredOperation(ApprovalOperation op) => new { op.Id, op.Kind, op.Scope, op.RequestKey, op.RequestHash, op.HashVersion, op.CommittedAt, op.InstanceId, op.DefinitionId, IsTombstone = true };
}
