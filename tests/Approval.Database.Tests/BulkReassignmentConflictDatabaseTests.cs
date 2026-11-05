using System.Data.Common;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class HandoverScope : IApprovalQueryScope
    {
        public Guid Hidden;
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.Id != Hidden));
    }

    private sealed class HandoverConflict : SaveChangesInterceptor
    {
        public Guid InstanceId;
        public bool Failed;
        public Func<Task>? AfterRollback;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Failed && eventData.Context!.ChangeTracker.Entries<ApprovalLog>().Any(e =>
                e.State == EntityState.Added && e.Entity.InstanceId == InstanceId && e.Entity.Action == ApprovalAction.Reassign))
            { Failed = true; throw new DbUpdateConcurrencyException("controlled provider handover conflict"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class HandoverRollback(HandoverConflict conflict) : DbTransactionInterceptor
    {
        public override async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (conflict.Failed && conflict.AfterRollback is { } callback)
            { conflict.AfterRollback = null; await callback(); }
        }
    }

    [Theory]
    [InlineData(InstanceStatus.Running, false)]
    [InlineData(InstanceStatus.Returned, false)]
    [InlineData(InstanceStatus.Approved, false)]
    [InlineData(InstanceStatus.Running, true)]
    public async Task BulkReassignmentConflictQuery_TranslatesOnEachProvider(InstanceStatus afterConflict, bool hide)
    {
        var conflict = new HandoverConflict(); var visibility = new HandoverScope(); var faults = new AtomicFaults();
        await using var host = await AtomicHostAsync(faults, configureServices: services =>
        {
            services.AddSingleton<IApprovalQueryScope>(visibility);
            services.ConfigureDbContext<AtomicContractContext>(o => o.AddInterceptors(conflict, new HandoverRollback(conflict)));
        });
        var success = await AtomicStart(host, faults, "first", "first");
        var contested = await AtomicStart(host, faults, "second", "second"); conflict.InstanceId = contested;
        var expected = new List<Guid>();
        await using (var db = fixture.CreateStorage())
        {
            // Distinct timestamps keep the fixture independent of generated GUIDs; candidate order is not asserted.
            (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == success)).CreatedAt = Timestamp;
            (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == contested)).CreatedAt = Timestamp.AddMinutes(1);
            var original = await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == contested); expected.Add(original.Id);
            foreach (var state in new[] { ApprovalTaskStatus.Waiting, ApprovalTaskStatus.Suspended, ApprovalTaskStatus.Approved,
                ApprovalTaskStatus.Rejected, ApprovalTaskStatus.Transferred, ApprovalTaskStatus.Canceled })
            {
                var id = Guid.NewGuid();
                db.Add(new ApprovalTask { Id = id, InstanceId = contested, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = state, CreatedAt = Timestamp });
                if (state is ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended) expected.Add(id);
            }
            db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = contested, NodeId = "review", NodeName = "Role",
                AssigneeKind = AssigneeKind.Role, AssigneeValue = "bob", Status = ApprovalTaskStatus.Pending, CreatedAt = Timestamp });
            await db.SaveChangesAsync();
        }
        conflict.AfterRollback = async () =>
        {
            await using var db = fixture.CreateStorage();
            (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == contested)).Status = afterConflict;
            await db.SaveChangesAsync();
            if (hide) visibility.Hidden = contested;
        };
        Task<ReassignResult> Run() => AtomicRun(host, "admin", sp => sp.GetRequiredService<ApprovalEngine>()
            .ReassignUserAsync("bob", "charlie", "admin", "handover", "conflict-batch"));
        var result = await Run();
        if (hide)
        {
            Assert.Equal(Approval.Core.ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<Approval.Core.ApprovalException>(() => Run())).Code);
            visibility.Hidden = Guid.Empty;
        }
        var replay = await Run();
        Assert.True(conflict.Failed); Assert.Equal(1, result.Count); Assert.Equal(result.Count, replay.Count);
        Assert.Equal(!hide && afterConflict is InstanceStatus.Running or InstanceStatus.Returned ? expected.Order() : Enumerable.Empty<Guid>(), result.Skipped.Select(s => s.TaskId).Order());
        Assert.Equal(result.Skipped, replay.Skipped);
        await using var saved = fixture.CreateStorage();
        Assert.Equal("charlie", (await saved.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == success && t.Status == ApprovalTaskStatus.Pending)).AssigneeValue);
        Assert.Equal(1, await saved.Set<ApprovalLog>().CountAsync(l => l.InstanceId == success && l.Action == ApprovalAction.Reassign));
        Assert.False(await saved.Set<ApprovalLog>().AnyAsync(l => l.InstanceId == contested && l.Action == ApprovalAction.Reassign));
    }
}
