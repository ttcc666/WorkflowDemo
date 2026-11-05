using System.Data.Common;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Core.Tests;

public sealed class BulkReassignmentConflictTests
{
    private sealed class Scope : IApprovalQueryScope
    {
        public Guid Hidden;
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.Id != Hidden));
    }

    private sealed class Conflict : SaveChangesInterceptor
    {
        public Guid InstanceId;
        public int Failures;
        public Func<Task>? AfterRollback;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Failures == 0 && eventData.Context!.ChangeTracker.Entries<ApprovalLog>()
                .Any(e => e.State == EntityState.Added && e.Entity.InstanceId == InstanceId && e.Entity.Action == ApprovalAction.Reassign))
            { Failures++; throw new DbUpdateConcurrencyException("controlled item conflict"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Rollback(Conflict conflict) : DbTransactionInterceptor
    {
        public override async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (conflict.Failures > 0 && conflict.AfterRollback is { } callback)
            { conflict.AfterRollback = null; await callback(); }
        }
    }

    [Theory]
    [InlineData(InstanceStatus.Running, false)]
    [InlineData(InstanceStatus.Returned, false)]
    [InlineData(InstanceStatus.Approved, false)]
    [InlineData(InstanceStatus.Rejected, false)]
    [InlineData(InstanceStatus.Terminated, false)]
    [InlineData(InstanceStatus.Withdrawn, false)]
    [InlineData(InstanceStatus.Running, true)]
    public async Task BulkReassignmentConflictReportsOnlyOpenUserTasksAndKeepsCommittedInstances(InstanceStatus afterConflict, bool hidden)
    {
        var conflict = new Conflict(); var scope = new Scope();
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton<IApprovalQueryScope>(scope);
            b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(conflict, new Rollback(conflict)));
        });
        host.Identity.Add("admin", "Admin");
        await host.PublishAsync("handover", Flows.SingleNode("Any", "User", "bob"));
        var successful = await host.StartAsync("handover", "first", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var contested = await host.StartAsync("handover", "second", "alice"); conflict.InstanceId = contested.Id;
        var expected = contested.Tasks.Select(t => t.Id).ToList();
        await host.SeedStorageAsync(async db =>
        {
            foreach (var status in Enum.GetValues<ApprovalTaskStatus>())
            {
                if (status == ApprovalTaskStatus.Pending) continue;
                var id = Guid.NewGuid();
                db.Add(new ApprovalTask { Id = id, InstanceId = contested.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = status, CreatedAt = host.Clock.GetUtcNow().UtcDateTime });
                if (status is ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended) expected.Add(id);
            }
            db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = contested.Id, NodeId = "review", NodeName = "Role",
                AssigneeKind = AssigneeKind.Role, AssigneeValue = "bob", Status = ApprovalTaskStatus.Pending });
            db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = contested.Id, NodeId = "review", NodeName = "Other",
                AssigneeKind = AssigneeKind.User, AssigneeValue = "other", Status = ApprovalTaskStatus.Pending });
            await db.SaveChangesAsync();
        });
        conflict.AfterRollback = async () =>
        {
            await host.SeedStorageAsync(async db =>
            {
                var instance = await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == contested.Id);
                instance.Status = afterConflict; await db.SaveChangesAsync();
            });
            if (hidden) scope.Hidden = contested.Id;
        };
        Task<ReassignResult> Run() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalEngine>()
            .ReassignUserAsync("bob", "carol", "admin", "handover", "stable-batch"));
        if (hidden)
        {
            // The first invocation also checks current read access after committing its summary.
            Assert.Equal(ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<ApprovalException>(() => Run())).Code);
            scope.Hidden = Guid.Empty;
        }
        var result = await Run();
        if (hidden)
        {
            scope.Hidden = contested.Id;
            Assert.Equal(ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<ApprovalException>(() => Run())).Code);
            scope.Hidden = Guid.Empty; // Restore read access; replay must return the original summary without retrying the conflicted item.
        }
        var replay = await Run();
        Assert.Equal(1, conflict.Failures); Assert.Equal(1, result.Count); Assert.Equal(result.Count, replay.Count);
        var active = afterConflict is InstanceStatus.Running or InstanceStatus.Returned;
        Assert.Equal(active && !hidden ? expected.Order() : Enumerable.Empty<Guid>(), result.Skipped.Select(t => t.TaskId).Order());
        Assert.Equal(result.Skipped, replay.Skipped);
        Assert.All(result.Skipped, s => { Assert.Equal(contested.Id, s.InstanceId); Assert.Contains("已被他人更新", s.Reason); });
        var saved = await host.GetInstanceAsync(successful.Id);
        Assert.Equal("carol", Assert.Single(saved.Tasks, t => t.Status == ApprovalTaskStatus.Pending).AssigneeValue);
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Reassign);
        var rolledBack = await host.GetInstanceAsync(contested.Id);
        Assert.DoesNotContain(rolledBack.Tasks, t => t.AssigneeValue == "carol");
        Assert.DoesNotContain(rolledBack.Logs, l => l.Action == ApprovalAction.Reassign);
    }
}
