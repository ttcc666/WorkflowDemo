using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

internal sealed class FinalSyncAction(AtomicContractContext db, AtomicFaults faults) : IApprovalLifecycleHandler
{
    public Task OnStartingAsync(ApprovalInstance instance, CancellationToken ct)
    {
        db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 20 });
        if (faults.FailCallback) throw new InvalidOperationException("controlled synchronous action failure");
        return Task.CompletedTask;
    }
}
public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task FinalAcceptance_SynchronousActionFailureRollsBackHostApprovalOperationAndOutbox()
    {
        var faults = new AtomicFaults { FailCallback = true };
        await using var host = await AtomicHostAsync(faults, configureServices: s => s.AddScoped<IApprovalLifecycleHandler, FinalSyncAction>());
        var flow = AtomicFlow;
        await using (var db = fixture.CreateStorage())
        {
            var definition = new ApprovalDefinition { Id = Guid.NewGuid(), Key = "sync", Name = "Sync", Version = 1, DefinitionJson = flow, IsPublished = true, CreatedAt = Timestamp };
            db.Add(definition);
            db.Add(new ApprovalDefinitionManagement { Id = Guid.NewGuid(), Key = "sync", Name = "Sync", LastVersion = 1, ActiveDefinitionId = definition.Id, UpdatedAt = Timestamp, UpdatedBy = "admin" });
            await db.SaveChangesAsync();
        }
        Task<Guid> Start() => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("final.sync.start", "one", "sync-key", new { amount = 1 }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "sync" }),
            async (fresh, ct) =>
            {
                var db = fresh.GetRequiredService<AtomicContractContext>();
                db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 1 });
                return (await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("sync", AtomicBusiness.Type, "one", "alice"), ct)).Id;
            }));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(Start);
        Assert.Equal("controlled synchronous action failure", failure.Message);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(0, await db.Set<AtomicHostRecord>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").CountAsync());
            return true;
        });
        faults.FailCallback = false;
        var id = await Start(); Assert.Equal(id, await Start());
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(2, await db.Set<AtomicHostRecord>().CountAsync());
            Assert.Equal(id, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
            Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").CountAsync());
            return true;
        });
    }
    [Fact]
    public async Task FinalAcceptance_BulkReassignmentReportsPerInstancePermissionAndKeepsOtherTenant()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var allowed = await AtomicStart(host, faults, "first", "first");
        var blocked = await AtomicStart(host, faults, "second", "second");
        var foreign = await AtomicStart(host, faults, "foreign", "foreign");
        var permissions = (AtomicPermissions)host.GetRequiredService<IApprovalPermissionProvider>();
        permissions.Rule = request => request.Permission != ApprovalPermissions.TaskReassign || request.Resource.InstanceId != blocked && request.Resource.InstanceId != foreign;
        var result = await AtomicRun(host, "admin", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "charlie", "admin", "handover"));
        Assert.Equal(1, result.Count); Assert.Equal(new[] { blocked, foreign }.Order(), result.Skipped.Select(s => s.InstanceId).Order());
        await AtomicRun(host, "admin", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal("charlie", (await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == allowed && t.Status == ApprovalTaskStatus.Pending)).AssigneeValue);
            Assert.Equal("bob", (await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == blocked)).AssigneeValue);
            Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Reassign));
            return true;
        });
        await AtomicRun(host, "admin", async sp =>
        {
            var task = await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>().SingleAsync(t => t.InstanceId == foreign);
            Assert.Equal("bob", task.AssigneeValue); return true;
        }, tenant: "a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicRun(host, "admin", sp => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(ct =>
            sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "charlie", "admin", "handover", ct))));
    }
}
