using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task Governance_ConcurrentDraftsAndPublishCounter_DoNotOverwriteOrDuplicateVersions()
    {
        var faults = new AtomicFaults(); await using var first = await AtomicHostAsync(faults);
        await using var second = await AtomicHostAsync(faults, initializeDatabase: false);
        async Task<bool> Save(ServiceProvider host, string name)
        {
            try { await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("governance.draft", "doc", name, new { name }, ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = "doc" }),
                (fresh, ct) => fresh.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("doc", name, AtomicFlow, 0, ct: ct))); return true; }
            catch (ApprovalException ex) when (ex.Code == ApprovalErrorCode.Conflict) { return false; }
        }
        var saves = await Task.WhenAll(Save(first, "first"), Save(second, "second")); Assert.Single(saves, x => x);
        async Task<bool> Publish(ServiceProvider host, string key)
        {
            try { await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("governance.publish", "doc", key, new { revision = 1 }, ApprovalPermissions.DefinitionPublish, new() { DefinitionKey = "doc" }),
                (fresh, ct) => fresh.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("doc", 1, key, ct))); return true; }
            catch (ApprovalException ex) when (ex.Code == ApprovalErrorCode.Conflict) { return false; }
        }
        var published = await Task.WhenAll(Publish(first, "publish-a"), Publish(second, "publish-b")); Assert.Single(published, x => x);
        await AtomicRun(first, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(new[] { 1, 2 }, await db.Set<ApprovalDefinition>().Where(d => d.Key == "doc").OrderBy(d => d.Version).Select(d => d.Version).ToArrayAsync());
            var state = await db.Set<ApprovalDefinitionManagement>().SingleAsync(m => m.Key == "doc");
            Assert.Equal(2, state.Revision); Assert.Equal(2, state.LastVersion);
            Assert.Equal(2, await db.Set<ApprovalDefinitionAudit>().CountAsync()); Assert.Equal(2, await db.Set<ApprovalOperation>().CountAsync()); return true;
        });
    }

    [Fact]
    public async Task Governance_DisabledFlowKeepsOriginalInstanceAndTenantDraftsIndependent()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var id = await AtomicStart(host, faults, "original");
        await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("governance.disable", "doc", "disable", new { enabled = false }, ApprovalPermissions.DefinitionDisable, new() { DefinitionKey = "doc" }),
            (fresh, ct) => fresh.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, 0, "retired", ct)));
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => AtomicStart(host, faults, "new", "new"));
        Assert.Equal(ApprovalErrorCode.InvalidOperation, denied.Code);
        Assert.Equal(id, await AtomicStart(host, faults, "original"));
        await AtomicRun(host, "bob", async sp =>
        {
            var task = await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>().SingleAsync(t => t.InstanceId == id && t.Status == ApprovalTaskStatus.Pending);
            return await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("governance.approve", id.ToString(), "approve", new { task.Id }, ApprovalPermissions.TaskApprove, new() { TaskId = task.Id }),
                async (fresh, ct) => { await fresh.GetRequiredService<ApprovalEngine>().ApproveAsync(task.Id, "bob", ct: ct); return true; });
        });
        await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("governance.draft", "draft", "same-key", new { draft = true }, ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = "draft" }),
            (fresh, ct) => fresh.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("draft", "Draft", AtomicFlow, 0, ct: ct)));
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(InstanceStatus.Approved, (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == id)).Status);
            Assert.Equal("doc", (await db.Set<ApprovalDefinition>().SingleAsync(d => d.Key == "doc")).Key);
            Assert.Equal(2, await db.Set<ApprovalDefinitionAudit>().CountAsync()); return true;
        });
        await AtomicRun(host, "alice", async sp => { var state = await sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("draft"); Assert.Equal("Draft", state!.Name); Assert.True(state.IsEnabled); return true; });
    }

    [Fact]
    public async Task Governance_OuterRollbackRemovesDraftPublicationAuditAndOperation()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync<int>(
            new("governance.rollback", "new-flow", "rollback", new { }, ApprovalPermissions.DefinitionDesign, new() { DefinitionKey = "new-flow" }),
            async (fresh, ct) =>
            {
                var service = fresh.GetRequiredService<ApprovalDefinitionService>();
                var draft = await service.SaveDesignDraftAsync("new-flow", "New", AtomicFlow, 0, ct: ct);
                await service.PublishDesignDraftAsync("new-flow", draft.Revision, "first", ct);
                throw new InvalidOperationException("host rollback");
            })));
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.False(await db.Set<ApprovalDefinition>().AnyAsync(d => d.Key == "new-flow"));
            Assert.Equal("doc", Assert.Single(await db.Set<ApprovalDefinitionManagement>().ToListAsync()).Key); Assert.Empty(await db.Set<ApprovalDefinitionAudit>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync()); return true;
        });
    }
}
