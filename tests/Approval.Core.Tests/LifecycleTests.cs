using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class LifecycleTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private static TestHost Host(bool enabled = true) {
        var host = new TestHost(o => { o.Lifecycle.Enabled = enabled; o.Lifecycle.AllowDeletion = true; o.Lifecycle.ArchiveAfter = TimeSpan.FromDays(1); o.Lifecycle.PurgeAfter = TimeSpan.FromDays(2); o.Lifecycle.BatchSize = 1; });
        host.Identity.Add("admin", "Admin"); return host;
    }
    private static async Task<ApprovalInstance> Finished(TestHost host, string key)
    {
        var instance = await host.StartAsync("doc", key, "alice"); await host.ApproveMyTaskAsync("bob"); return instance;
    }
    private static Task<MaintenanceJobInfo> Create(TestHost host, MaintenanceTarget target, MaintenanceMode mode) =>
        host.RunAsAsync("admin", async sp => { var service = sp.GetRequiredService<ApprovalLifecycleService>(); var preview = await service.PreviewAsync(target, mode); return await service.CreateJobAsync(target, mode, preview.PolicyHash); });
    [Fact]
    public async Task DisabledMaintenanceAndPreview_AreReadonlyAndCannotCreateDeletionJob()
    {
        await using var host = Host(false); await host.PublishAsync("doc", Flow); await Finished(host, "one"); host.Clock.Advance(TimeSpan.FromDays(3));
        var preview = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().PreviewAsync(MaintenanceTarget.Instances, MaintenanceMode.Archive));
        Assert.False(preview.Enabled); Assert.Equal(1, preview.Eligible);
        await Assert.ThrowsAsync<ApprovalException>(() => Create(host, MaintenanceTarget.Instances, MaintenanceMode.Archive));
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalArchiveRecord>().ToListAsync()); Assert.Empty(await db.Set<ApprovalMaintenanceJob>().ToListAsync()); Assert.Single(await db.Set<ApprovalInstance>().ToListAsync()); });
    }
    [Fact]
    public async Task ArchiveBatches_ResumeWithoutDuplicates_AndProtectRunningHeldAndPendingOutbox()
    {
        await using var host = Host(); await host.PublishAsync("doc", Flow);
        var first = await Finished(host, "first"); var held = await Finished(host, "held"); await Finished(host, "last");
        await host.StartAsync("doc", "running", "alice"); host.Clock.Advance(TimeSpan.FromDays(3));
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().SetHoldAsync(MaintenanceTarget.Instances, held.Id, true, "legal hold"));
        var job = await Create(host, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        var step = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(job.Id, 1));
        Assert.Equal(1, step.Processed); Assert.Equal(1, step.CompletedBatches);
        for (var i = 0; i < 3; i++) step = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(job.Id, 1));
        Assert.Equal(MaintenanceJobStatus.Completed, step.Status); Assert.Equal(2, step.Processed);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(2, await db.Set<ApprovalArchiveRecord>().CountAsync()); Assert.Equal(4, await db.Set<ApprovalInstance>().CountAsync()); Assert.False(await db.Set<ApprovalArchiveRecord>().AnyAsync(a => a.ResourceId == held.Id)); });
        var archive = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ListArchivesAsync());
        Assert.Contains(archive.Items, a => a.ResourceId == first.Id);
    }
    [Fact]
    public async Task PurgeKeepsHistoricalSnapshot_AndPermanentKeyTombstonePreventsReexecution()
    {
        await using var host = Host(); await host.PublishAsync("doc", Flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "keyed", "alice") { OperationId = "original" };
        var instance = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        await host.ApproveMyTaskAsync("bob"); host.Clock.Advance(TimeSpan.FromDays(3));
        var archive = await Create(host, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(archive.Id, 3));
        var purge = await Create(host, MaintenanceTarget.Instances, MaintenanceMode.Purge);
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(purge.Id, 3));
        var expired = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request)));
        Assert.Equal(ApprovalErrorCode.ReplayExpired, expired.Code);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync()); Assert.Single(await db.Set<ApprovalDefinition>().ToListAsync()); var op = await db.Set<ApprovalOperation>().SingleAsync(); Assert.True(op.IsTombstone); Assert.Equal(string.Empty, op.ResultJson); Assert.Equal(instance.Id, (await db.Set<ApprovalArchiveRecord>().SingleAsync()).ResourceId); });
        var history = await host.RunAsAsync("admin", async sp => { var service = sp.GetRequiredService<ApprovalLifecycleService>(); var record = Assert.Single((await service.ListArchivesAsync()).Items); return await service.ExportArchiveAsync(record.Id); });
        Assert.Contains(instance.Id.ToString(), history); Assert.Contains("Review", history);
    }
}
