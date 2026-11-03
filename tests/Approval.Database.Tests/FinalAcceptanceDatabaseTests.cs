using Approval.Core;
using Approval.Core.Actions;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

internal sealed class FinalSyncAction(AtomicContractContext db, AtomicFaults faults) : IApprovalActionHandler
{
    public ApprovalActionDescriptor Descriptor => new("FinalSync", "Final synchronous contract", []);
    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
    {
        db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), TenantId = db.TenantId, Value = 20 });
        if (faults.FailCallback) throw new InvalidOperationException("controlled synchronous action failure");
        return Task.FromResult(new ApprovalActionResult());
    }
}
public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task FinalAcceptance_SynchronousActionFailureRollsBackHostApprovalOperationAndOutbox()
    {
        var faults = new AtomicFaults { FailCallback = true };
        await using var host = await AtomicHostAsync(faults, configureServices: s => s.AddScoped<IApprovalActionHandler, FinalSyncAction>());
        var flow = AtomicFlow.Replace("\"next\":\"review\"", "\"next\":\"sync\"")
            .Replace("\"review\":{", "\"sync\":{\"type\":\"Action\",\"name\":\"Sync\",\"actions\":[{\"type\":\"FinalSync\",\"mode\":\"Sync\"}],\"next\":\"review\"},\"review\":{");
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            db.Add(new ApprovalDefinition { Id = Guid.NewGuid(), Key = "sync", Name = "Sync", Version = 1, IsPublished = true, DefinitionJson = flow, CreatedAt = Timestamp });
            await db.SaveChangesAsync(); return true;
        });
        Task<Guid> Start() => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("final.sync.start", "one", "sync-key", new { amount = 1 }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "sync" }),
            async (fresh, ct) =>
            {
                var db = fresh.GetRequiredService<AtomicContractContext>();
                db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), TenantId = db.TenantId, Value = 1 });
                return (await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("sync", AtomicBusiness.Type, "one", "alice"), ct)).Id;
            }));
        var failure = await Assert.ThrowsAsync<ApprovalException>(Start);
        Assert.Equal("controlled synchronous action failure", Assert.IsType<InvalidOperationException>(failure.InnerException).Message);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(0, await db.Set<AtomicHostRecord>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().CountAsync());
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
            Assert.Equal(1, await db.Set<ApprovalOutboxMessage>().CountAsync());
            return true;
        });
    }
    [Fact]
    public async Task FinalAcceptance_BulkReassignmentReportsPerInstancePermissionAndKeepsOtherTenant()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var allowed = await AtomicStart(host, faults, "first", "first");
        var blocked = await AtomicStart(host, faults, "second", "second");
        var foreign = await AtomicStart(host, faults, "first", "first", tenant: "a");
        var permissions = (AtomicPermissions)host.GetRequiredService<IApprovalPermissionProvider>();
        permissions.Rule = request => request.Permission != ApprovalPermissions.TaskReassign || request.Resource.InstanceId != blocked;
        var result = await AtomicRun(host, "admin", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "charlie", "admin", "handover"));
        Assert.Equal(1, result.Count); Assert.Equal(blocked, Assert.Single(result.Skipped).InstanceId);
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
