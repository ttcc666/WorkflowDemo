using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class StrictCommandBoundaryTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("too-long")]
    [InlineData("control")]
    public async Task CommandExecutor_InvalidKeyRejectsBeforeDelegateAndPersistence(string caseId)
    {
        var key = caseId switch { "missing" => null, "empty" => "", "whitespace" => " ", "too-long" => new string('k', 201), _ => "key\n" };
        await using var host = new TestHost();
        var calls = 0;
        Task<bool> Execute(IServiceProvider sp) => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("strict.write", "one", key, null, ApprovalPermissions.InstanceStart),
            (_, _) => { calls++; return Task.FromResult(true); });
        var userError = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", Execute));
        var systemError = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(Execute));
        Assert.Equal(ApprovalErrorCode.OperationIdRequired, userError.Code);
        Assert.Equal(ApprovalErrorCode.OperationIdRequired, systemError.Code);
        Assert.Equal(0, calls);
        await host.RunAsync(async sp => {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
        });
    }

    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
        """;
    [Fact]
    public async Task DefaultPolicy_DirectStartRequiresKey_AndKeyedStartReplays()
    {
        await using var host = new TestHost();
        await host.PublishAsync("strict", Flow);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp =>
            sp.GetRequiredService<ApprovalEngine>().StartAsync(new("strict", "business", "one", "alice"))));
        Assert.Equal(ApprovalErrorCode.OperationIdRequired, error.Code);
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().ToListAsync()));
        Task<ApprovalInstance> Start() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new("strict", "business", "one", "alice") { OperationId = "start" }));
        var first = await Start(); var replay = await Start();
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(Assert.Single(first.Tasks).Id, Assert.Single(replay.Tasks).Id);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
    }

    [Fact]
    public async Task StrictMode_PublicUnitCannotReplaceAKeyedCommand()
    {
        await using var host = new TestHost(); await host.PublishAsync("strict", Flow);
        var instance = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("strict", "business", "one", "alice") { OperationId = "start" }));
        var taskId = Assert.Single(instance.Tasks).Id;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("bob", sp =>
            sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
            { await sp.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, "bob", "missing key", ct); return true; })));
        Assert.Equal(ApprovalErrorCode.OperationIdRequired, error.Code);
        var after = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(after.Tasks).Status);
        Assert.DoesNotContain(after.Logs, l => l.Action == ApprovalAction.Approve);
    }
    private sealed class FailSecondItem : IApprovalPermissionProvider
    {
        public bool Enabled;
        private Guid? _first;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        {
            if (Enabled && request.Permission == ApprovalPermissions.TaskReassignBulk && request.Resource.InstanceId is { } id)
            {
                _first ??= id;
                if (id != _first) throw new IOException("second item unavailable");
            }
            return Task.FromResult(true);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion(bool interrupt)
    {
        var permissions = new FailSecondItem();
        await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(permissions));
        host.Identity.Add("admin", "Admin"); host.Identity.Add("alice"); host.Identity.Add("bob"); host.Identity.Add("carol");
        await host.PublishAsync("strict", Flow);
        foreach (var name in new[] { "one", "two" })
            await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("strict", "business", name, "alice") { OperationId = name }));
        Task<ReassignResult> Run(string reason = "handover") => host.RunAsAsync("admin", sp =>
            sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "admin", reason, "batch"));
        if (interrupt)
        {
            permissions.Enabled = true;
            var error = await Assert.ThrowsAsync<ApprovalException>(() => Run()); Assert.Equal(ApprovalErrorCode.AuthorizationUnavailable, error.Code);
            await host.RunAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<TestDbContext>().Set<ApprovalTask>().CountAsync(t => t.AssigneeValue == "carol")));
            permissions.Enabled = false;
        }
        var first = await Run(); var replay = await Run();
        Assert.Equal(2, first.Count); Assert.Empty(first.Skipped); Assert.Equal(first.Count, replay.Count); Assert.Empty(replay.Skipped);
        Assert.Equal(ApprovalErrorCode.Conflict, (await Assert.ThrowsAsync<ApprovalException>(() => Run("different"))).Code);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(2, await db.Set<ApprovalTask>().CountAsync(t => t.AssigneeValue == "carol"));
            Assert.Equal(2, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Reassign));
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.plan"));
            Assert.Equal(2, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.item"));
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.result"));
        });
        var missing = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("carol", "bob", "admin", null, null!)));
        Assert.Equal(ApprovalErrorCode.OperationIdRequired, missing.Code);
    }
}
