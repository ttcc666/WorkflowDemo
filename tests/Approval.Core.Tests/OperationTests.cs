using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class OperationTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;
    [Fact]
    public async Task KeyedCoreStart_ReplaysOriginalVersionAndRejectsDifferentContent()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "one", "alice") { OperationId = "intent-1", FormData = new { amount = 1 } };
        var first = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        await host.PublishAsync("doc", Flow.Replace("Review", "New review"));
        var replay = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        Assert.Equal(first.Id, replay.Id); Assert.Equal(first.DefinitionId, replay.DefinitionId); Assert.Equal(1, replay.DefinitionVersion);
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { FormData = new { amount = 2 } })));
        Assert.Equal(ApprovalErrorCode.Conflict, failure.Code);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
        });
    }

    [Fact]
    public async Task HostUnitOfWork_CommitsMultipleEngineCallsAndNotifiesOnlyAfterCommit()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var unit = sp.GetRequiredService<IApprovalUnitOfWork>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            await unit.ExecuteAsync(async token =>
            {
                db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "host" });
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "two", "alice"), token);
                Assert.Empty(host.Notifier.Notifications);
            });
            Assert.Equal(2, host.Notifier.Notifications.Count);
        });
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(1, await db.Set<TestRecord>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(2, await db.Set<ApprovalBusinessSlot>().CountAsync());
        });
    }

    [Fact]
    public async Task OuterRollback_RemovesBusinessWorkflowAndSlots_WithoutClearingHostTrackerOrNotifying()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var unit = sp.GetRequiredService<IApprovalUnitOfWork>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            var business = new TestRecord { Id = Guid.NewGuid(), Name = "host" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
            {
                db.Add(business);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "two", "alice"), token);
                throw new InvalidOperationException("host rollback");
            }));
            Assert.Contains(db.ChangeTracker.Entries<TestRecord>(), e => ReferenceEquals(e.Entity, business));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        Assert.Empty(host.Notifier.Notifications);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(0, await db.Set<TestRecord>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
        });
    }

    [Fact]
    public async Task RawExternalTransaction_AndBulkInsideHostUnit_AreRejectedBeforeMutations()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice")));
                Assert.False(db.ChangeTracker.HasChanges()); await transaction.RollbackAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(
                token => engine.ReassignUserAsync("bob", "charlie", "admin", "handover", token)));
        });
        Assert.Empty(host.Notifier.Notifications);
    }

    [Fact]
    public async Task BusinessSlot_IsRetainedForRunningInstanceAndReleasedAtFinalCompletion()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        var first = await host.StartAsync("doc", "one", "alice");
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("doc", "one", "alice"));
        await host.ApproveMyTaskAsync("bob");
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().CountAsync()));
        var second = await host.StartAsync("doc", "one", "alice"); Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task ParallelBusinessMode_RemainsExplicitAndDoesNotCreateSlots()
    {
        await using var host = new TestHost(o => o.SingleRunningInstancePerBusiness = false); await host.PublishAsync("doc", Flow);
        await host.StartAsync("doc", "same", "alice"); await host.StartAsync("doc", "same", "alice");
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync()); });
    }

    [Fact]
    public async Task StrictMode_RequiresClientKeyBeforeCreatingAnything()
    {
        await using var host = new TestHost(o => o.RequireOperationId = true); await host.PublishAsync("doc", Flow);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("doc", "one", "alice")); Assert.Equal(ApprovalErrorCode.OperationIdRequired, error.Code);
        var result = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice") { OperationId = "stable" }));
        Assert.Equal("one", result.BusinessKey);
    }

    [Fact]
    public async Task ExternalSyncAction_IsRejectedByValidationAndPublish_UnlessExplicitlyOptedIn()
    {
        const string external = """
            {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"act"},
            "act":{"type":"Action","name":"Http","actions":[{"type":"Http","mode":"Sync","parameters":{"url":"https://api.example.com"}}],"next":"end"},
            "end":{"type":"End","name":"End"}}}
            """;
        await using var host = new TestHost(o => o.AllowSynchronousExternalActions = false);
        await host.RunAsync(async sp =>
        {
            var service = sp.GetRequiredService<ApprovalDefinitionService>();
            var errors = await service.ValidateAsync(Approval.Core.Definitions.FlowDefinition.Parse(external));
            Assert.Contains(errors, e => e.Contains("同步外部副作用"));
            Assert.DoesNotContain(await service.ValidateAsync(Approval.Core.Definitions.FlowDefinition.Parse(external.Replace("Sync", "Async"))), e => e.Contains("同步外部副作用"));
        });
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("external", external));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        await using var legacy = new TestHost(o => o.AllowSynchronousExternalActions = true);
        await legacy.PublishAsync("external", external);
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinition>().CountAsync()));
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBack_AndAfterCommitCannotUndoSuccess()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        using var before = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RunAsync(sp =>
            sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
            {
                await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                before.Cancel(); token.ThrowIfCancellationRequested();
            }, before.Token)));
        Assert.Empty(host.Notifier.Notifications);
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
        using var after = new CancellationTokenSource();
        var id = await host.RunAsync(sp =>
        {
            var unit = sp.GetRequiredService<IApprovalUnitOfWork>();
            return unit.ExecuteAsync(async token =>
            {
                var result = await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                unit.AfterCommit(() => { after.Cancel(); return Task.CompletedTask; });
                return result.Id;
            }, after.Token);
        });
        Assert.True(after.IsCancellationRequested);
        Assert.Equal(id, (await host.GetInstanceAsync(id)).Id);
        Assert.Single(host.Notifier.Notifications);
    }

    [Fact]
    public async Task ExistingSlotBackfill_IsExplicitIdempotentAndDoesNotModifyInstance()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow); var instance = await host.StartAsync("doc", "one", "alice");
        await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().ExecuteDeleteAsync());
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var preview = await Approval.Core.Persistence.ApprovalBusinessSlotBackfill.RunAsync(db); Assert.True(preview.Ready); Assert.Equal(1, preview.Missing); Assert.Equal(0, preview.Changed);
            Assert.Equal(1, (await Approval.Core.Persistence.ApprovalBusinessSlotBackfill.RunAsync(db, apply: true)).Changed);
            Assert.Equal(0, (await Approval.Core.Persistence.ApprovalBusinessSlotBackfill.RunAsync(db, apply: true)).Changed);
        });
        Assert.Equal(InstanceStatus.Running, (await host.GetInstanceAsync(instance.Id)).Status);
    }
}
