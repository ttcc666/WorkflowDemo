using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Maintenance;
using Approval.Core.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private static readonly DateTime OldLifecycleDate = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static void LifecycleOptions(ApprovalOptions o)
    { o.Lifecycle.Enabled = true; o.Lifecycle.AllowDeletion = true; o.Lifecycle.ArchiveAfter = TimeSpan.FromDays(1); o.Lifecycle.PurgeAfter = TimeSpan.FromDays(2); o.Lifecycle.OperationsAfter = TimeSpan.FromDays(1); o.Lifecycle.BatchSize = 1; }
    private async Task<Guid> EndLifecycleInstance(ServiceProvider host, AtomicFaults faults, string key, string business)
    {
        var id = await AtomicStart(host, faults, key, business);
        await AtomicRun(host, "bob", async sp =>
        {
            var task = await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>().SingleAsync(t => t.InstanceId == id && t.Status == ApprovalTaskStatus.Pending);
            return await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("lifecycle.approve", id.ToString(), "approve", new { task.Id }, ApprovalPermissions.TaskApprove, new() { TaskId = task.Id }),
                async (fresh, ct) => { await fresh.GetRequiredService<ApprovalEngine>().ApproveAsync(task.Id, "bob", ct: ct); return true; });
        });
        await AtomicRun(host, "alice", async sp => { await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalInstance>().Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.CompletedAt, OldLifecycleDate)); return true; });
        return id;
    }
    private static Task<MaintenanceJobInfo> LifecycleJob(ServiceProvider host, MaintenanceTarget target, MaintenanceMode mode) => AtomicRun(host, "alice", async sp =>
    {
        var preview = await sp.GetRequiredService<ApprovalLifecycleService>().PreviewAsync(target, mode);
        return await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("maintenance.create", target.ToString(), Guid.NewGuid().ToString(), new { target, mode, preview.PolicyHash }, ApprovalPermissions.ArchiveExecute),
            (fresh, ct) => fresh.GetRequiredService<ApprovalLifecycleService>().CreateJobAsync(target, mode, preview.PolicyHash, ct));
    });
    private static Task<bool> LifecycleHold(ServiceProvider host, Guid id, bool held) => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
        new("maintenance.hold", id.ToString(), Guid.NewGuid().ToString(), new { id, held }, ApprovalPermissions.ArchiveExecute, new() { InstanceId = id }),
        (fresh, ct) => fresh.GetRequiredService<ApprovalLifecycleService>().SetHoldAsync(MaintenanceTarget.Instances, id, held, "retention", ct)));
    private static Task<MaintenanceJobInfo> LifecycleRun(ServiceProvider host, Guid id, int batches = 3) => AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(id, batches));

    [Fact]
    public async Task Lifecycle_TwoWorkersAndTransientBatchRetry_KeepOneArchiveAndDurableCursor()
    {
        var faults = new AtomicFaults(); await using var first = await AtomicHostAsync(faults, configure: LifecycleOptions);
        await using var second = await AtomicHostAsync(faults, false, LifecycleOptions);
        await EndLifecycleInstance(first, faults, "first", "first"); await EndLifecycleInstance(first, faults, "second", "second");
        var job = await LifecycleJob(first, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        faults.MaintenanceBeforeCommit = 1;
        await Task.WhenAll(LifecycleRun(first, job.Id), LifecycleRun(second, job.Id));
        var done = await LifecycleRun(first, job.Id); Assert.Equal(MaintenanceJobStatus.Completed, done.Status); Assert.Equal(2, done.Processed); Assert.Equal(2, done.CompletedBatches);
        await AtomicRun(first, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(2, await db.Set<ApprovalArchiveRecord>().CountAsync()); Assert.Equal(2, await db.Set<ApprovalMaintenanceAudit>().CountAsync(a => a.JobId == job.Id)); Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); return true; });
        Assert.Equal(0, faults.MaintenanceBeforeCommit);
    }
    [Fact]
    public async Task Lifecycle_FailedCommitLeavesNoArchiveOrCursor_OriginalJobCanResume()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults, configure: LifecycleOptions);
        await EndLifecycleInstance(host, faults, "one", "one"); var job = await LifecycleJob(host, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        faults.MaintenancePermanentFailure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LifecycleRun(host, job.Id));
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); var saved = await db.Set<ApprovalMaintenanceJob>().SingleAsync(); Assert.Equal(0, saved.Processed); Assert.Equal(0, saved.CompletedBatches); Assert.Empty(await db.Set<ApprovalArchiveRecord>().ToListAsync()); Assert.Empty(await db.Set<ApprovalMaintenanceAudit>().ToListAsync()); return true; });
        faults.MaintenancePermanentFailure = false;
        var resumed = await LifecycleRun(host, job.Id); Assert.Equal(MaintenanceJobStatus.Completed, resumed.Status); Assert.Equal(1, resumed.Processed);
    }
    [Fact]
    public async Task Lifecycle_PendingOutboxAndHoldBlockPurge_ThenOriginalKeyExpiresWithoutCrossTenantImpact()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults, configure: LifecycleOptions);
        var id = await EndLifecycleInstance(host, faults, "first", "same-business");
        var archive = await LifecycleJob(host, MaintenanceTarget.Instances, MaintenanceMode.Archive); await LifecycleRun(host, archive.Id);
        var blocked = await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalLifecycleService>().PreviewAsync(MaintenanceTarget.Instances, MaintenanceMode.Purge)); Assert.Equal(0, blocked.Eligible);
        await AtomicRun(host, "alice", async sp => { await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(o => o.InstanceId == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OutboxStatus.Succeeded).SetProperty(o => o.CompletedAt, OldLifecycleDate)); return true; });
        await LifecycleHold(host, id, true);
        Assert.Equal(0, (await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalLifecycleService>().PreviewAsync(MaintenanceTarget.Instances, MaintenanceMode.Purge))).Eligible);
        await LifecycleHold(host, id, false);
        var purge = await LifecycleJob(host, MaintenanceTarget.Instances, MaintenanceMode.Purge); Assert.Equal(1, (await LifecycleRun(host, purge.Id)).Processed);
        var expired = await Assert.ThrowsAsync<ApprovalException>(() => AtomicStart(host, faults, "first", "same-business")); Assert.Equal(ApprovalErrorCode.ReplayExpired, expired.Code); Assert.Equal(1, faults.BusinessCalls);
        var other = await AtomicStart(host, faults, "different-key", "different-business"); Assert.NotEqual(id, other);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Single(await db.Set<ApprovalInstance>().ToListAsync()); Assert.True((await db.Set<ApprovalOperation>().SingleAsync(o => o.Kind == "atomic.start" && o.RequestKey == "first")).IsTombstone); Assert.Single(await db.Set<ApprovalArchiveRecord>().ToListAsync()); return true; });
        await AtomicRun(host, "alice", async sp => { Assert.Single(await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalArchiveRecord>().ToListAsync()); return true; }, "a");
    }
    [Fact]
    public async Task Lifecycle_OperationRetentionPersistsTombstoneThroughScopedSubqueries()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults, configure: LifecycleOptions);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); db.Add(new ApprovalOperation { Id = Guid.NewGuid(), ActorId = "alice", Kind = "test", Scope = "test", RequestKey = "key", LookupHash = new string('a', 64), RequestHash = new string('b', 64), ResultJson = "secret-result", CommittedAt = OldLifecycleDate }); await db.SaveChangesAsync(); return true; });
        var job = await LifecycleJob(host, MaintenanceTarget.Operations, MaintenanceMode.Purge); var done = await LifecycleRun(host, job.Id); Assert.Equal(1, done.Processed); Assert.Equal(MaintenanceJobStatus.Completed, done.Status);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); var op = await db.Set<ApprovalOperation>().SingleAsync(o => o.Kind == "test"); Assert.True(op.IsTombstone); Assert.Empty(op.ResultJson); Assert.Equal("key", op.RequestKey); Assert.DoesNotContain("secret-result", (await db.Set<ApprovalArchiveRecord>().SingleAsync()).SnapshotJson); return true; });
    }
    [Fact]
    public async Task Lifecycle_ArchivedAttachmentExpiryCommitsDeletionIntent_StorageFailureRetriesSafely()
    {
        var faults = new AtomicFaults(); var store = new LifecycleFileStore();
        await using var host = await AtomicHostAsync(faults, configure: o => { LifecycleOptions(o); o.Lifecycle.ArchivedPayloadsAfter = TimeSpan.FromDays(1); }, configureServices: s => s.AddSingleton<IApprovalFileStore>(store));
        var id = await EndLifecycleInstance(host, faults, "file", "file"); var fileId = Guid.NewGuid();
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); db.Add(new ApprovalFile { Id = fileId, InstanceId = id, StorageKey = "test-key", FileName = "file.pdf", ContentType = "application/pdf", Sha256 = new string('a', 64), UploadedBy = "alice", UploadedAt = OldLifecycleDate }); await db.SaveChangesAsync(); await db.Set<ApprovalOutboxMessage>().Where(o => o.InstanceId == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OutboxStatus.Succeeded).SetProperty(o => o.CompletedAt, OldLifecycleDate)); return true; });
        var archive = await LifecycleJob(host, MaintenanceTarget.Instances, MaintenanceMode.Archive); await LifecycleRun(host, archive.Id);
        var purge = await LifecycleJob(host, MaintenanceTarget.Instances, MaintenanceMode.Purge); await LifecycleRun(host, purge.Id);
        var recordId = await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); var record = await db.Set<ApprovalArchiveRecord>().SingleAsync(); Assert.Equal(record.Id, (await db.Set<ApprovalFile>().SingleAsync()).ArchiveId); record.PurgedAt = OldLifecycleDate; record.ConcurrencyStamp++; await db.SaveChangesAsync(); return record.Id; });
        var bytes = await AtomicRun(host, "alice", async sp => { await using var stream = await sp.GetRequiredService<ApprovalLifecycleService>().OpenArchiveFileAsync(recordId, fileId); using var data = new MemoryStream(); await stream.CopyToAsync(data); return data.ToArray(); }); Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        var expire = await LifecycleJob(host, MaintenanceTarget.ArchivedPayloads, MaintenanceMode.Purge); await LifecycleRun(host, expire.Id);
        Assert.Equal(0, store.Deletions);
        await AtomicRun(host, "alice", async sp => { var d = await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalFileDeletion>().SingleAsync(); Assert.Equal(FileDeletionStatus.Pending, d.Status); return true; });
        await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalLifecycleService>().DrainFileDeletesAsync()); Assert.True(store.Exists);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); var d = await db.Set<ApprovalFileDeletion>().SingleAsync(); Assert.Equal(FileDeletionStatus.Pending, d.Status); d.NextAttemptAt = OldLifecycleDate; d.ConcurrencyStamp++; await db.SaveChangesAsync(); return true; });
        Assert.Equal(1, await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalLifecycleService>().DrainFileDeletesAsync())); Assert.False(store.Exists); Assert.Equal(2, store.Deletions);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Empty(await db.Set<ApprovalFile>().ToListAsync()); Assert.Equal(FileDeletionStatus.Completed, (await db.Set<ApprovalFileDeletion>().SingleAsync()).Status); var record = await db.Set<ApprovalArchiveRecord>().SingleAsync(); Assert.Empty(record.SnapshotJson); Assert.NotNull(record.PayloadExpiredAt); return true; });
    }
    private sealed class LifecycleFileStore : IApprovalFileStore
    {
        public int Deletions; public bool Exists = true;
        public Task<string> SaveAsync(Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task DeleteAsync(string key, CancellationToken ct) { if (++Deletions == 1) throw new InvalidOperationException("injected storage failure"); Exists = false; return Task.CompletedTask; }
    }
}
