using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Files;
using Approval.Core.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class MaintenanceSafetyTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private static void Configure(ApprovalOptions o)
    {
        o.Lifecycle.Enabled = true; o.Lifecycle.AllowDeletion = true; o.Lifecycle.ArchiveAfter = TimeSpan.FromDays(1); o.Lifecycle.PurgeAfter = TimeSpan.FromDays(2);
        o.Lifecycle.ReadMessagesAfter = o.Lifecycle.SucceededOutboxAfter = o.Lifecycle.FailedOutboxAfter = o.Lifecycle.BusinessDraftsAfter = o.Lifecycle.DesignDraftsAfter = o.Lifecycle.OperationsAfter = o.Lifecycle.UnboundFilesAfter = o.Lifecycle.ArchivedPayloadsAfter = TimeSpan.FromDays(1);
        o.Lifecycle.BatchSize = 10;
    }
    private static Task<MaintenanceJobInfo> Run(TestHost host, MaintenanceTarget target, MaintenanceMode mode = MaintenanceMode.Purge) => host.RunAsAsync("admin", async sp =>
    {
        var service = sp.GetRequiredService<ApprovalLifecycleService>(); var preview = await service.PreviewAsync(target, mode);
        var job = await service.CreateJobAsync(target, mode, preview.PolicyHash); return await service.RunJobAsync(job.Id, 3);
    });

    [Fact]
    public async Task FileDeletion_IsDurableBeforeStorageCall_FailureRetriesAndPendingFileCannotBind()
    {
        var store = new FailingStore();
        await using var host = new TestHost(Configure, b => { b.AddFormApprovals(); b.Services.AddSingleton<IApprovalFileStore>(store); }); host.Identity.Add("admin", "Admin");
        const string form = """
            {"startNodeId":"start","settings":{"startable":true},"form":{"fields":[{"key":"files","label":"Files","type":"File"}]},"nodes":{"start":{"type":"Start","name":"Start","next":"end"},"end":{"type":"End","name":"End"}}}
            """;
        await host.PublishAsync("form", form);
        var file = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalFileService>().UploadAsync("alice", "x.pdf", new MemoryStream([1, 2, 3])));
        host.Clock.Advance(TimeSpan.FromDays(2));
        await Run(host, MaintenanceTarget.UnboundFiles);
        Assert.True(store.Files.ContainsKey(file.StorageKey));
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.True((await db.Set<ApprovalFile>().SingleAsync()).DeletionPending); Assert.Equal(FileDeletionStatus.Pending, (await db.Set<ApprovalFileDeletion>().SingleAsync()).Status); });
        await Assert.ThrowsAsync<ApprovalValidationException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalFormService>().StartAsync("form", "alice", JsonSerializer.SerializeToElement(new { files = new[] { new { id = file.Id } } }))));
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().DrainFileDeletesAsync());
        Assert.True(store.Files.ContainsKey(file.StorageKey));
        await host.RunAsync(async sp => { var deletion = await sp.GetRequiredService<TestDbContext>().Set<ApprovalFileDeletion>().SingleAsync(); Assert.Equal(1, deletion.Attempts); Assert.Equal("InvalidOperationException", deletion.LastErrorCode); });
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().DrainFileDeletesAsync()));
        Assert.False(store.Files.ContainsKey(file.StorageKey));
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalFile>().ToListAsync()); Assert.Equal(FileDeletionStatus.Completed, (await db.Set<ApprovalFileDeletion>().SingleAsync()).Status); });
    }

    [Fact]
    public async Task AncillaryPolicies_KeepFailedMessagesAndDefinitionCounters_AndExpireOnlyOperationBody()
    {
        await using var host = new TestHost(Configure, b => b.AddFormApprovals()); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var now = host.Clock.GetUtcNow().UtcDateTime;
            db.Add(new ApprovalMessage { Id = Guid.NewGuid(), UserId = "alice", Title = "read", Content = "private-message", IsRead = true, ReadAt = now, CreatedAt = now });
            db.Add(new ApprovalOutboxMessage { Id = Guid.NewGuid(), Kind = OutboxKind.Notification, Target = "Test", PayloadJson = "private-header", Status = OutboxStatus.Succeeded, CompletedAt = now, CreatedAt = now });
            db.Add(new ApprovalOutboxMessage { Id = Guid.NewGuid(), Kind = OutboxKind.Notification, Target = "Test", PayloadJson = "failed-header", Status = OutboxStatus.Failed, CompletedAt = now, CreatedAt = now });
            db.Add(new ApprovalDraft { Id = Guid.NewGuid(), UserId = "alice", DefinitionKey = "doc", FormDataJson = "{}", CreatedAt = now, UpdatedAt = now });
            db.Add(new ApprovalOperation { Id = Guid.NewGuid(), LookupHash = new string('a', 64), ActorId = "alice", Kind = "test", Scope = "test", RequestKey = "original", RequestHash = new string('b', 64), ResultJson = "private-result", CommittedAt = now });
            await db.SaveChangesAsync();
        });
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("doc", "Doc", Flow, 1));
        host.Clock.Advance(TimeSpan.FromDays(3));
        foreach (var target in new[] { MaintenanceTarget.ReadMessages, MaintenanceTarget.SucceededOutbox, MaintenanceTarget.BusinessDrafts, MaintenanceTarget.DesignDrafts, MaintenanceTarget.Operations })
        {
            var result = await Run(host, target);
            Assert.True(result.Processed == 1, $"{target}: processed={result.Processed}, status={result.Status}");
        }
        await Run(host, MaintenanceTarget.FailedOutbox, MaintenanceMode.Archive);
        await Assert.ThrowsAsync<ApprovalException>(() => Run(host, MaintenanceTarget.FailedOutbox));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalMessage>().ToListAsync()); Assert.Empty(await db.Set<ApprovalDraft>().ToListAsync());
            Assert.Equal(OutboxStatus.Failed, (await db.Set<ApprovalOutboxMessage>().SingleAsync()).Status);
            var management = await db.Set<ApprovalDefinitionManagement>().SingleAsync(); Assert.Null(management.DraftJson); Assert.Equal(1, management.LastVersion); Assert.NotNull(management.ActiveDefinitionId);
            var operation = await db.Set<ApprovalOperation>().SingleAsync(); Assert.True(operation.IsTombstone); Assert.Equal("original", operation.RequestKey); Assert.Empty(operation.ResultJson);
            Assert.DoesNotContain("private-result", (await db.Set<ApprovalArchiveRecord>().SingleAsync(a => a.Target == MaintenanceTarget.Operations)).SnapshotJson);
            Assert.Equal(6, await db.Set<ApprovalArchiveRecord>().CountAsync()); Assert.Single(await db.Set<ApprovalDefinition>().ToListAsync());
        });
    }

    [Fact]
    public async Task ArchivedAttachmentsRemainReadableUntilExplicitPayloadExpiry_ThenKeepMarker()
    {
        var store = new FailingStore { Fail = false };
        await using var host = new TestHost(Configure, b => { b.AddFormApprovals(); b.Services.AddSingleton<IApprovalFileStore>(store); }); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        var instance = await host.StartAsync("doc", "one", "alice"); await host.ApproveMyTaskAsync("bob");
        var file = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalFileService>().UploadAsync("alice", "history.pdf", new MemoryStream([4, 5, 6])));
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); var row = await db.Set<ApprovalFile>().SingleAsync(); row.InstanceId = instance.Id; await db.SaveChangesAsync(); });
        host.Clock.Advance(TimeSpan.FromDays(3)); await Run(host, MaintenanceTarget.Instances, MaintenanceMode.Archive); await Run(host, MaintenanceTarget.Instances);
        var record = await host.RunAsAsync("admin", async sp => Assert.Single((await sp.GetRequiredService<ApprovalLifecycleService>().ListArchivesAsync()).Items));
        var contents = await host.RunAsAsync("admin", async sp => { await using var stream = await sp.GetRequiredService<ApprovalLifecycleService>().OpenArchiveFileAsync(record.Id, file.Id); using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer); return buffer.ToArray(); });
        Assert.Equal(new byte[] { 4, 5, 6 }, contents);
        host.Clock.Advance(TimeSpan.FromDays(2)); await Run(host, MaintenanceTarget.ArchivedPayloads);
        var expired = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ExportArchiveAsync(record.Id)));
        Assert.Equal(ApprovalErrorCode.ReplayExpired, expired.Code);
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().DrainFileDeletesAsync()); Assert.False(store.Files.ContainsKey(file.StorageKey));
        await host.RunAsync(async sp => { var row = await sp.GetRequiredService<TestDbContext>().Set<ApprovalArchiveRecord>().SingleAsync(); Assert.NotNull(row.PayloadExpiredAt); Assert.Empty(row.SnapshotJson); Assert.Equal(record.SnapshotHash, row.SnapshotHash); });
    }

    [Fact]
    public async Task Diagnostics_AreReadonlyAndBounded_AndContributorsCannotExportSecrets()
    {
        await using var host = new TestHost(o => o.Background.AssignmentSampleSize = 1, b => b.Services.AddScoped<IApprovalDiagnosticsContributor, UnsafeContributor>()); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        await host.StartAsync("doc", "one", "alice"); await host.StartAsync("doc", "two", "alice");
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.True(report.Ready); Assert.True(report.DataAvailable); Assert.Equal(2, report.RunningInstances); Assert.True(report.AssignmentSampleTruncated);
        Assert.Equal("contributor_unavailable", Assert.Single(report.Contributions).Code); Assert.DoesNotContain("secret-password", JsonSerializer.Serialize(report));
        var forbidden = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync())); Assert.Equal(ApprovalErrorCode.Forbidden, forbidden.Code);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); Assert.Empty(await db.Set<ApprovalMaintenanceJob>().ToListAsync()); });
    }
    private sealed class UnsafeContributor : IApprovalDiagnosticsContributor { public Task<ApprovalDiagnosticContribution> CheckAsync(CancellationToken ct) => Task.FromResult(new ApprovalDiagnosticContribution("secret-password", true, 1)); }
    [Fact]
    public async Task ArchiveReadDoesNotGrantSensitiveExport_AndRevokedResourcePermissionStopsQueuedPurge()
    {
        var permissions = new LimitedArchivePermissions();
        await using var host = new TestHost(Configure, b => b.Services.AddSingleton<IApprovalPermissionProvider>(permissions)); await host.PublishAsync("doc", Flow);
        var instance = await host.StartAsync("doc", "private", "alice", new { secret = "private-secret" }); await host.ApproveMyTaskAsync("bob"); host.Clock.Advance(TimeSpan.FromDays(3));
        await Run(host, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        var records = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ListArchivesAsync());
        Assert.DoesNotContain("private-secret", JsonSerializer.Serialize(records));
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ExportArchiveAsync(records.Items.Single().Id)));
        Assert.Equal(ApprovalErrorCode.Forbidden, denied.Code);
        var job = await host.RunAsAsync("admin", async sp => { var service = sp.GetRequiredService<ApprovalLifecycleService>(); var preview = await service.PreviewAsync(MaintenanceTarget.Instances, MaintenanceMode.Purge); return await service.CreateJobAsync(MaintenanceTarget.Instances, MaintenanceMode.Purge, preview.PolicyHash); });
        permissions.DeniedInstance = instance.Id;
        var revoked = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().RunJobAsync(job.Id, 3)));
        Assert.Equal(ApprovalErrorCode.Forbidden, revoked.Code);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Single(await db.Set<ApprovalInstance>().ToListAsync()); var stored = await db.Set<ApprovalMaintenanceJob>().SingleAsync(j => j.Id == job.Id); Assert.Equal(0, stored.Processed); Assert.Equal(0, stored.CompletedBatches); });
        var oldPreview = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().PreviewAsync(MaintenanceTarget.Instances, MaintenanceMode.Archive));
        await host.RunAsync(sp => { sp.GetRequiredService<ApprovalOptions>().Lifecycle.ArchiveAfter = TimeSpan.FromDays(2); return Task.FromResult(true); });
        var changed = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().CreateJobAsync(MaintenanceTarget.Instances, MaintenanceMode.Archive, oldPreview.PolicyHash)));
        Assert.Equal(ApprovalErrorCode.Conflict, changed.Code);
    }
    private sealed class LimitedArchivePermissions : IApprovalPermissionProvider
    {
        public Guid? DeniedInstance;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(request.Permission != ApprovalPermissions.ArchiveReadSensitive
            && !(request.Permission == ApprovalPermissions.ArchivePurge && request.Resource.InstanceId == DeniedInstance && DeniedInstance != null));
    }
    [Fact]
    public async Task ArchiveQueryScope_FiltersBeforeCountAndRejectsGuessedExportId()
    {
        await using var host = new TestHost(Configure, b => b.Services.AddScoped<IApprovalArchiveQueryScope, HiddenArchiveScope>()); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        await host.StartAsync("doc", "one", "alice"); await host.ApproveMyTaskAsync("bob"); host.Clock.Advance(TimeSpan.FromDays(3)); await Run(host, MaintenanceTarget.Instances, MaintenanceMode.Archive);
        var id = await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalArchiveRecord>().Select(a => a.Id).SingleAsync());
        var list = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ListArchivesAsync()); Assert.Equal(0, list.Total); Assert.Empty(list.Items);
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ExportArchiveAsync(id))); Assert.Equal(ApprovalErrorCode.NotFound, denied.Code);
        await using var missingScope = new TestHost(Configure, b => b.Services.AddScoped<IApprovalQueryScope, RestrictedInstanceScope>()); missingScope.Identity.Add("admin", "Admin");
        var unavailable = await Assert.ThrowsAsync<ApprovalException>(() => missingScope.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalLifecycleService>().ListArchivesAsync())); Assert.Equal(ApprovalErrorCode.AuthorizationUnavailable, unavailable.Code);
    }
    private sealed class HiddenArchiveScope : IApprovalArchiveQueryScope
    { public Task<IQueryable<ApprovalArchiveRecord>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalArchiveRecord> query, CancellationToken ct) => Task.FromResult(query.Where(a => false)); }
    private sealed class RestrictedInstanceScope : IApprovalQueryScope
    { public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> query, CancellationToken ct) => Task.FromResult(query.Where(i => false)); }
    private sealed class FailingStore : IApprovalFileStore
    {
        public readonly Dictionary<string, byte[]> Files = []; public bool Fail = true;
        public async Task<string> SaveAsync(Stream content, CancellationToken ct) { using var data = new MemoryStream(); await content.CopyToAsync(data, ct); var key = Guid.NewGuid().ToString("N"); Files[key] = data.ToArray(); return key; }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Files[storageKey]));
        public Task DeleteAsync(string storageKey, CancellationToken ct) { if (Fail) { Fail = false; throw new InvalidOperationException("secret-password"); } Files.Remove(storageKey); return Task.CompletedTask; }
    }
}
