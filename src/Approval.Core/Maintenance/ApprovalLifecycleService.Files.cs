using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Maintenance;

public sealed partial class ApprovalLifecycleService
{
    public async Task<int> CleanupUnboundFilesAsync(CancellationToken ct = default)
    {
        await Auth.RequireAsync(ApprovalPermissions.FileCleanup, ct: ct);
        if (!Policy.Enabled || !Policy.AllowDeletion || Policy.UnboundFilesAfter is null) return 0;
        var job = await _unit.ExecuteAsync(async token =>
        {
            var j = new ApprovalMaintenanceJob { Id = Guid.NewGuid(), ActorId = "approval-background", SystemRequested = true,
                Target = MaintenanceTarget.UnboundFiles, Mode = MaintenanceMode.Purge, CreatedAt = Now,
                Cutoff = Now - Policy.UnboundFilesAfter.Value, PolicyHash = Hash(MaintenanceTarget.UnboundFiles, MaintenanceMode.Purge) };
            _db.Add(j); await _db.SaveChangesAsync(token); return j;
        }, ct);
        var result = await ProcessJobAsync(job, 1, ct);
        await DrainFileDeletesAsync(ct);
        return result.Processed;
    }
    /// <summary>已提交删除任务的至少一次执行。存储 DeleteAsync 必须幂等；失败保留任务并重试。</summary>
    public async Task<int> DrainFileDeletesAsync(CancellationToken ct = default)
    {
        if (accessor.IsSystem) await Auth.RequireAsync(ApprovalPermissions.FileCleanup, ct: ct);
        else await Auth.RequireAsync(ApprovalPermissions.ArchivePurge, ct: ct);
        if (!Policy.Enabled || !Policy.AllowDeletion || store is null) return 0;
        var ids = await _db.Set<ApprovalFileDeletion>().AsNoTracking().Where(d =>
            (d.Status == FileDeletionStatus.Pending || d.Status == FileDeletionStatus.Processing) && d.NextAttemptAt <= Now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Select(d => d.Id).Take(Policy.BatchSize).ToListAsync(ct);
        var count = 0;
        var systemJob = new ApprovalMaintenanceJob { ActorId = "approval-background", SystemRequested = true, PolicyHash = "", Target = MaintenanceTarget.UnboundFiles, Mode = MaintenanceMode.Purge };
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            var deletion = await RunOwnedAsync(systemJob, async (w, token) =>
            {
                var d = await w._db.Set<ApprovalFileDeletion>().SingleAsync(d => d.Id == id, token);
                if (d.Status is FileDeletionStatus.Completed or FileDeletionStatus.Cancelled || d.NextAttemptAt > w.Now) return null;
                var file = await w._db.Set<ApprovalFile>().SingleOrDefaultAsync(f => f.Id == d.FileId, token);
                if (file is null || !file.DeletionPending || file.InstanceId != null || file.DraftId != null || file.ArchiveId != null
                    || await w._db.Set<ApprovalRetentionHold>().AnyAsync(h => h.Target == MaintenanceTarget.UnboundFiles && h.ResourceId == d.FileId, token))
                { d.Status = FileDeletionStatus.Cancelled; d.ConcurrencyStamp++; return null; }
                d.Status = FileDeletionStatus.Processing; d.StartedAt ??= w.Now; d.LeaseId = lease; d.Attempts++;
                d.NextAttemptAt = w.Now + w.Policy.LeaseDuration; d.ConcurrencyStamp++;
                return new { d.StorageKey, d.FileId };
            }, ct);
            if (deletion is null) continue;
            try
            {
                await store.DeleteAsync(deletion.StorageKey, ct);
                await RunOwnedAsync(systemJob, async (w, token) =>
                {
                    var d = await w._db.Set<ApprovalFileDeletion>().SingleAsync(d => d.Id == id, token);
                    if (d.LeaseId != lease || d.Status != FileDeletionStatus.Processing) throw new DbUpdateConcurrencyException("File deletion lease replaced");
                    var file = await w._db.Set<ApprovalFile>().SingleOrDefaultAsync(f => f.Id == d.FileId, token);
                    if (file is not null) w._db.Remove(file);
                    d.Status = FileDeletionStatus.Completed; d.LeaseId = null; d.LastErrorCode = null; d.ConcurrencyStamp++;
                    w.Audit("FileDeleted", MaintenanceTarget.UnboundFiles, d.FileId); return true;
                }, ct);
                count++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await RunOwnedAsync(systemJob, async (w, token) =>
                {
                    var d = await w._db.Set<ApprovalFileDeletion>().SingleAsync(d => d.Id == id, token);
                    if (d.LeaseId == lease) { d.Status = FileDeletionStatus.Pending; d.LeaseId = null; d.NextAttemptAt = w.Now + TimeSpan.FromMinutes(1); d.LastErrorCode = ex.GetType().Name; d.ConcurrencyStamp++; }
                    return false;
                }, ct);
            }
        }
        return count;
    }
}
