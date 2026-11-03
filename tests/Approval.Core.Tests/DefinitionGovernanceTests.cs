using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class DefinitionGovernanceTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;

    [Fact]
    public async Task DraftConflict_DoesNotOverwriteOrAudit_AndPublishPreservesSource()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        var original = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("doc"));
        var draft = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("doc", "Changed", Flow.Replace("Review", "Changed"), 1, original!.Id));
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("doc", "Overwritten", Flow, 1)));
        Assert.Equal(ApprovalErrorCode.Conflict, error.Code);
        var published = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("doc", draft.Revision, "review updated"));
        Assert.Equal(2, published.Version); Assert.Equal("Changed", published.Name);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(FlowDefinitionJson(Flow), (await db.Set<ApprovalDefinition>().SingleAsync(d => d.Id == original!.Id)).DefinitionJson);
            var audit = await db.Set<ApprovalDefinitionAudit>().OrderBy(a => a.Revision).ToListAsync();
            Assert.Equal(3, audit.Count); Assert.Equal("admin", audit[^1].ActorId);
            Assert.Equal(original!.Id, audit[^1].SourceDefinitionId); Assert.Equal("review updated", audit[^1].ChangeNote);
            Assert.Equal(published.Id, audit[^1].DefinitionId); Assert.Equal(64, audit[^1].DefinitionHash!.Length);
        });
        var current = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        Assert.False(current!.HasDraftChanges);
        var disabled = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, current.Revision));
        Assert.False(disabled.HasDraftChanges); // Enable state changes do not create draft edits.
        var republished = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("doc", disabled.Revision, "publish while disabled"));
        Assert.Equal(3, republished.Version);
        current = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        Assert.False(current!.IsEnabled); Assert.False(current.HasDraftChanges); Assert.Equal(republished.Id, current.ActiveDefinitionId);
    }

    [Fact]
    public async Task DisabledDefinition_BlocksNewStartsButExistingTaskAndOriginalReplayContinue()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "one", "alice") { OperationId = "original" };
        var instance = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        var state = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, state!.Revision, "retired"));
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { BusinessKey = "two", OperationId = "new" })));
        Assert.Equal(ApprovalErrorCode.InvalidOperation, denied.Code);
        Assert.Equal(instance.Id, (await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request))).Id);
        await host.RunAsAsync("bob", async sp => { await sp.GetRequiredService<ApprovalEngine>().ApproveAsync(instance.Tasks.Single(t => t.Status == ApprovalTaskStatus.Pending).Id, "bob"); return true; });
        Assert.Equal(InstanceStatus.Approved, (await host.GetInstanceAsync(instance.Id)).Status);
        await host.RunAsync(async sp => Assert.Single(await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().ToListAsync()));
    }

    [Fact]
    public async Task SelectHistoricalVersion_ChangesNewStartsWithoutRewritingPublishedJson()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin"); await host.PublishAsync("doc", Flow);
        var first = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("doc"));
        await host.PublishAsync("doc", Flow.Replace("Review", "New review"));
        var state = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SelectVersionAsync("doc", first!.Id, state!.Revision, "rollback selection"));
        var instance = await host.StartAsync("doc", "selected", "alice");
        Assert.Equal(first!.Id, instance.DefinitionId); Assert.Equal(1, instance.DefinitionVersion);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ApprovalDefinition>().Where(d => d.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, "bulk rewrite")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ApprovalDefinitionAudit>().ExecuteDeleteAsync());
            Assert.Equal(FlowDefinitionJson(Flow), (await db.Set<ApprovalDefinition>().SingleAsync(d => d.Id == first.Id)).DefinitionJson);
        });
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var definition = await db.Set<ApprovalDefinition>().SingleAsync(d => d.Id == first.Id);
            definition.Name = "rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task HostRollback_RemovesDraftPublishAndAuditTogether()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsAsync("admin", async sp =>
        {
            await sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
            {
                var service = sp.GetRequiredService<ApprovalDefinitionService>();
                var draft = await service.SaveDesignDraftAsync("doc", "Doc", Flow, 0, ct: token);
                await service.PublishDesignDraftAsync("doc", draft.Revision, "first", token);
                throw new InvalidOperationException("rollback");
            });
            return true;
        }));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Empty(await db.Set<ApprovalDefinition>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalDefinitionManagement>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalDefinitionAudit>().ToListAsync());
        });
    }

    private static string FlowDefinitionJson(string json) => Approval.Core.Definitions.FlowDefinition.Parse(json).ToJson();

    [Fact]
    public async Task DisablePermission_DoesNotExposeStoredDesignDraftOrGrantSensitiveRead()
    {
        await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalPermissionProvider, DisableOnly>());
        await host.PublishAsync("doc", Flow);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("doc", "Doc", Flow.Replace("Review", "private-secret"), 1));
        var changed = await host.RunAsAsync("operator", sp => sp.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, 2));
        Assert.Null(changed.DraftJson); Assert.False(changed.IsEnabled); Assert.False(changed.Capabilities[ApprovalPermissions.DefinitionPublish]);
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("operator", sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc")));
        Assert.Equal(ApprovalErrorCode.Forbidden, denied.Code);
    }
    private sealed class DisableOnly : IApprovalPermissionProvider
    {
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(request.Permission == ApprovalPermissions.DefinitionDisable);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task InvalidDraftIdentity_LeavesNoManagementOrAudit(int keyLength, int nameLength)
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>()
            .SaveDesignDraftAsync(new string('k', keyLength), new string('n', nameLength), Flow, 0)));
        Assert.Equal(ApprovalErrorCode.InvalidOperation, failure.Code);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalDefinitionManagement>().ToListAsync()); Assert.Empty(await db.Set<ApprovalDefinitionAudit>().ToListAsync()); });
    }
    [Fact]
    public async Task DraftIdentityBoundary_AndMissingBodyAndLongNoteAreValidatedWithoutPartialPublication()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        var key = new string('k', 100); var name = new string('n', 200);
        var saved = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync(key, name, Flow, 0));
        Assert.Equal(key, saved.Key); Assert.Equal(name, saved.Name);
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("empty", "Empty", null!, 0)));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync(key, saved.Revision, new string('n', 1001))));
        var published = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync(key, saved.Revision, new string('n', 1000)));
        Assert.Equal(1, published.Version);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Single(await db.Set<ApprovalDefinitionManagement>().ToListAsync()); Assert.Equal(2, await db.Set<ApprovalDefinitionAudit>().CountAsync()); });
    }
}
