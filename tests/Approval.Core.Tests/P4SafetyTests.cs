using System.Data.Common;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Definitions;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Core.Tests;

public sealed class P4SafetyTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"timeout":{"minutes":60},"next":"end"},"end":{"type":"End"}}}
        """;
    private const string Parallel = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"fork"},
        "fork":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
        "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
        "b":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
        "join":{"type":"Join","next":"end"},"end":{"type":"End"}}}
        """;

    private sealed class Permissions : IApprovalPermissionProvider
    {
        public Func<ApprovalPermissionRequest, bool> Rule = _ => true;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(Rule(request));
    }
    private sealed class Scope : IApprovalQueryScope
    {
        public Guid Hidden;
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.Id != Hidden));
    }
    private sealed class Access : IApprovalAccessPolicy
    {
        public bool Allow = true;
        public Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstanceContext instance, CancellationToken ct) =>
            Task.FromResult(Allow ? ApprovalAccess.Full : ApprovalAccess.None);
    }
    private sealed class Status : IApprovalUserStatusProvider, IApprovalUserDirectory
    {
        public string? Inactive;
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(!string.IsNullOrWhiteSpace(userId) && userId != Inactive);
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Select(id => new ApprovalUser(id, id, []) { IsActive = id != Inactive }).ToArray());
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class ItemFailure : SaveChangesInterceptor
    {
        public Guid InstanceId;
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ApprovalLog>().Any(e => e.State == EntityState.Added
                && e.Entity.InstanceId == InstanceId && e.Entity.Action == ApprovalAction.Reassign))
                throw new IOException("controlled second instance save failure");
            return ValueTask.FromResult(result);
        }
    }
    private static TestHost Host(Permissions permissions, Scope? scope = null, Access? access = null, ItemFailure? failure = null, Status? status = null,
        DbTransactionInterceptor? transaction = null) => new(
        o => { o.EnableBackgroundServices = false; o.Outbox.EnableDispatcher = false; }, b =>
        {
            b.Services.AddSingleton<IApprovalPermissionProvider>(permissions);
            if (scope is not null) b.Services.AddSingleton<IApprovalQueryScope>(scope);
            if (access is not null) b.Services.AddSingleton<IApprovalAccessPolicy>(access);
            if (failure is not null) b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(failure));
            if (transaction is not null) b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(transaction));
            if (status is not null) { b.Services.AddSingleton<IApprovalUserStatusProvider>(status); b.Services.AddSingleton<IApprovalUserDirectory>(status); }
        });

    private static Task<ReassignResult> Bulk(TestHost host, string key = "batch", string actor = "admin", string? reason = "handover") =>
        host.RunAsAsync(actor, sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "admin", reason, key));

    private static Task<bool> Command(TestHost host, string permission, ApprovalResource resource,
        Func<IServiceProvider, CancellationToken, Task> action, string key = "command", string actor = "admin") => host.RunAsAsync(actor, sp =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("p4." + permission, JsonSerializer.Serialize(resource), key,
            new { resource, reason = "handover" }, permission, resource), async (attempt, ct) => { await action(attempt, ct); return true; }));

    // Read fresh untracked rows, including original operation results and host records, after the failed scope is disposed.
    private static Task<string> Snapshot(TestHost host, Guid? instanceId = null) => host.RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<TestDbContext>();
        return JsonSerializer.Serialize(new
        {
            Definitions = await db.Set<ApprovalDefinition>().AsNoTracking().OrderBy(d => d.Id).ToListAsync(),
            Instances = await db.Set<ApprovalInstance>().AsNoTracking().Where(i => instanceId == null || i.Id == instanceId).OrderBy(i => i.Id).ToListAsync(),
            Tasks = await db.Set<ApprovalTask>().AsNoTracking().Where(t => instanceId == null || t.InstanceId == instanceId).OrderBy(t => t.Id).ToListAsync(),
            Logs = await db.Set<ApprovalLog>().AsNoTracking().Where(l => instanceId == null || l.InstanceId == instanceId).OrderBy(l => l.Id).ToListAsync(),
            Outbox = await db.Set<ApprovalOutboxMessage>().AsNoTracking().Where(m => instanceId == null || m.InstanceId == instanceId).OrderBy(m => m.Id).ToListAsync(),
            Slots = await db.Set<ApprovalBusinessSlot>().AsNoTracking().Where(s => instanceId == null || s.InstanceId == instanceId).OrderBy(s => s.Id).ToListAsync(),
            Operations = instanceId == null ? await db.Set<ApprovalOperation>().AsNoTracking().OrderBy(o => o.Id).ToListAsync() : [],
            Host = await db.Set<TestRecord>().AsNoTracking().OrderBy(r => r.Id).ToListAsync()
        });
    });

    [Theory]
    [InlineData("read-permission")]
    [InlineData("query-scope")]
    [InlineData("access-policy")]
    [InlineData("bulk-permission")]
    public async Task BulkSummaryReplay_RevokedResourceAccessRejectsWithoutWrites(string revocation)
    {
        var permissions = new Permissions(); var scope = new Scope(); var access = new Access();
        await using var host = Host(permissions, scope, access);
        await host.PublishAsync("p4", Flow);
        var instance = await host.StartAsync("p4", "one", "alice");
        // A skipped result includes task IDs, instance ID, title and node; it requires current resource access too.
        permissions.Rule = r => r.Permission != ApprovalPermissions.TaskReassign;
        var result = await Bulk(host);
        Assert.Equal(0, result.Count); Assert.Equal(instance.Id, Assert.Single(result.Skipped).InstanceId);
        var before = await Snapshot(host);
        switch (revocation)
        {
            case "read-permission": permissions.Rule = r => r.Permission != ApprovalPermissions.InstanceRead; break;
            case "query-scope": scope.Hidden = instance.Id; break;
            case "access-policy": access.Allow = false; break;
            default: permissions.Rule = r => r.Permission != ApprovalPermissions.TaskReassignBulk; break;
        }
        var error = await Assert.ThrowsAsync<ApprovalException>(() => Bulk(host));
        Assert.Equal(revocation is "query-scope" or "access-policy" ? ApprovalErrorCode.NotFound : ApprovalErrorCode.Forbidden, error.Code);
        Assert.Equal(before, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
    }

    private sealed class SummaryReplayBarrier : DbCommandInterceptor
    {
        public AsyncLocal<string?> Request { get; } = new();
        public HashSet<string> InitialChecks { get; } = [];
        public TaskCompletionSource AAtFinalLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BAtFinalLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseA { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseB { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BFinalRecordRead;
        private string? _summaryLookup;

        private bool IsSummaryLookup(DbCommand command) => command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal)
            && _summaryLookup is not null && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, _summaryLookup));

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            var request = Request.Value;
            if (request is not ("A" or "B") || !command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal)) return result;
            if (command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal))
            {
                _summaryLookup ??= command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<string>().Single();
                Assert.True(IsSummaryLookup(command));
                Assert.True(InitialChecks.Add(request));
            }
            else if (IsSummaryLookup(command))
            {
                if (request == "A") { AAtFinalLookup.TrySetResult(); await ReleaseA.Task.WaitAsync(ct); }
                else { BAtFinalLookup.TrySetResult(); await ReleaseB.Task.WaitAsync(ct); }
            }
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken ct = default)
        {
            if (Request.Value == "B" && IsSummaryLookup(command) && !command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal))
                BFinalRecordRead = true;
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("read-permission")]
    [InlineData("query-scope")]
    [InlineData("access-policy")]
    public async Task BulkSummaryConcurrentReplay_RevokedResourceAccessRejectsWithoutLeaks(string revocation)
    {
        var barrier = new SummaryReplayBarrier(); var permissions = new Permissions(); var scope = new Scope(); var access = new Access();
        await using var host = new TestHost(o => { o.EnableBackgroundServices = false; o.Outbox.EnableDispatcher = false; }, b =>
        {
            b.Services.AddSingleton<IApprovalPermissionProvider>(permissions); b.Services.AddSingleton<IApprovalQueryScope>(scope);
            b.Services.AddSingleton<IApprovalAccessPolicy>(access); b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(barrier));
        });
        await host.PublishAsync("p4", Flow); var successful = await host.StartAsync("p4", "successful", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(1)); var skipped = await host.StartAsync("p4", "confidential-skipped", "alice");
        var bItemReads = 0; var bTaskWrites = 0; var bFinalReplayAuthorizations = 0;
        permissions.Rule = r =>
        {
            if (barrier.Request.Value == "B")
            {
                if (r.Permission == ApprovalPermissions.InstanceRead && r.Resource.InstanceId == successful.Id) bItemReads++;
                if (r.Permission == ApprovalPermissions.TaskReassign) bTaskWrites++;
                if (barrier.BFinalRecordRead && r.Permission == ApprovalPermissions.TaskReassignBulk && r.Resource.InstanceId is null)
                    bFinalReplayAuthorizations++;
            }
            return r.Permission != ApprovalPermissions.TaskReassign || r.Resource.TaskId != skipped.Tasks.Single().Id;
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<ReassignResult> Run(string request)
        {
            barrier.Request.Value = request;
            return await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalEngine>()
                .ReassignUserAsync("bob", "carol", "admin", "handover", "concurrent-summary", cancellation.Token));
        }
        var a = Run("A"); Task<ReassignResult>? b = null;
        try
        {
            // A has committed plan/items but is paused before its final ExecuteAsync lookup/transaction.
            await barrier.AAtFinalLookup.Task.WaitAsync(cancellation.Token);
            b = Run("B");
            await barrier.BAtFinalLookup.Task.WaitAsync(cancellation.Token);
            Assert.Equal(new[] { "A", "B" }, barrier.InitialChecks.Order());
            Assert.True(bItemReads > 0); Assert.Equal(0, bTaskWrites); Assert.False(barrier.BFinalRecordRead);
            Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>()
                .CountAsync(o => o.Kind == "task.reassign-bulk.result")));

            barrier.ReleaseA.TrySetResult(); var winner = await a;
            Assert.Equal(1, winner.Count); var originalSkip = Assert.Single(winner.Skipped);
            Assert.Equal((skipped.Id, skipped.Tasks.Single().Id, skipped.Title), (originalSkip.InstanceId, originalSkip.TaskId, originalSkip.Title));
            Assert.Contains("权限", originalSkip.Reason);
            var committed = await Snapshot(host);
            await host.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<TestDbContext>();
                Assert.Equal(4, await db.Set<ApprovalOperation>().CountAsync());
                var summary = await db.Set<ApprovalOperation>().SingleAsync(o => o.Kind == "task.reassign-bulk.result");
                Assert.Contains(skipped.Id.ToString(), summary.ResultJson); Assert.Contains(originalSkip.TaskId.ToString(), summary.ResultJson);
                Assert.Single(await db.Set<ApprovalLog>().Where(l => l.Action == ApprovalAction.Reassign).ToListAsync());
            });
            // B's item replays have finished; A's result is durable, but B has not read that result yet.
            if (revocation == "read-permission")
                permissions.Rule = r =>
                {
                    if (barrier.Request.Value == "B" && barrier.BFinalRecordRead && r.Permission == ApprovalPermissions.TaskReassignBulk && r.Resource.InstanceId is null)
                        bFinalReplayAuthorizations++;
                    return r.Permission != ApprovalPermissions.InstanceRead || r.Resource.InstanceId != skipped.Id;
                };
            else if (revocation == "query-scope") scope.Hidden = skipped.Id;
            else access.Allow = false;
            barrier.ReleaseB.TrySetResult();
            var error = await Assert.ThrowsAsync<ApprovalException>(() => b);
            Assert.Equal(revocation == "read-permission" ? ApprovalErrorCode.Forbidden : ApprovalErrorCode.NotFound, error.Code);
            Assert.True(barrier.BFinalRecordRead); Assert.True(bFinalReplayAuthorizations > 0);
            Assert.Equal(committed, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
            Assert.DoesNotContain(skipped.Id.ToString(), error.Message); Assert.DoesNotContain(originalSkip.TaskId.ToString(), error.Message);
            Assert.DoesNotContain(originalSkip.Title!, error.Message); Assert.DoesNotContain(originalSkip.Reason, error.Message);

            permissions.Rule = _ => true; scope.Hidden = Guid.Empty; access.Allow = true;
            var recovered = await Bulk(host, "concurrent-summary");
            Assert.Equal(winner.Count, recovered.Count); Assert.Equal(winner.Skipped, recovered.Skipped);
            Assert.Equal(committed, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
        }
        finally
        {
            barrier.ReleaseA.TrySetResult(); barrier.ReleaseB.TrySetResult(); cancellation.Cancel();
            try { await Task.WhenAll(b is null ? [a] : new[] { a, b }); }
            catch (ApprovalException) { /* Expected denial was asserted above. */ }
            catch (OperationCanceledException) { /* Release paused requests before disposing their shared connection. */ }
        }
    }

    private sealed class SummaryCommitted(Action revoke) : DbTransactionInterceptor
    {
        public bool Fired;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<ApprovalOperation>().Any(e => e.Entity.Kind == "task.reassign-bulk.result"))
            { Fired = true; revoke(); }
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("read-permission")]
    [InlineData("query-scope")]
    [InlineData("access-policy")]
    public async Task BulkSummaryFirstCommit_RevokedResourceAccessRejectsAndOriginalKeyRecovers(string revocation)
    {
        var permissions = new Permissions(); var scope = new Scope(); var access = new Access(); Guid instanceId = default;
        var commit = new SummaryCommitted(() =>
        {
            if (revocation == "read-permission") permissions.Rule = r => r.Permission != ApprovalPermissions.InstanceRead;
            else if (revocation == "query-scope") scope.Hidden = instanceId;
            else access.Allow = false;
        });
        await using var host = Host(permissions, scope, access, transaction: commit);
        await host.PublishAsync("p4", Flow); var instance = await host.StartAsync("p4", "one", "alice"); instanceId = instance.Id;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => Bulk(host));
        Assert.True(commit.Fired);
        Assert.Equal(revocation == "read-permission" ? ApprovalErrorCode.Forbidden : ApprovalErrorCode.NotFound, error.Code);
        var committed = await Snapshot(host);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(3, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.result"));
            Assert.Single(await db.Set<ApprovalLog>().Where(l => l.Action == ApprovalAction.Reassign).ToListAsync());
            Assert.Equal(2, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal("carol", (await db.Set<ApprovalTask>().SingleAsync(t => t.Status == ApprovalTaskStatus.Pending)).AssigneeValue);
            Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
        });
        permissions.Rule = _ => true; scope.Hidden = Guid.Empty; access.Allow = true;
        var recovered = await Bulk(host); Assert.Equal(1, recovered.Count); Assert.Empty(recovered.Skipped);
        Assert.Equal(committed, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkSummary_SystemReadBypassPreservesWriteGrants(bool canReassign)
    {
        var permissions = new Permissions { Rule = r => r.Permission != ApprovalPermissions.InstanceRead };
        var scope = new Scope(); var access = new Access { Allow = false };
        await using var host = Host(permissions, scope, access);
        await host.PublishAsync("p4", Flow); var instance = await host.StartAsync("p4", "one", "alice");
        var grants = canReassign ? new[] { ApprovalPermissions.TaskReassignBulk, ApprovalPermissions.TaskReassign } : new[] { ApprovalPermissions.TaskReassignBulk };
        async Task<ReassignResult> Run(IReadOnlyCollection<string> allowed)
        {
            await using var request = host.NewScope(authorize: true);
            request.ServiceProvider.GetRequiredService<Approval.Core.Hosting.ApprovalExecutionContext>().InitializeSystem("handover-worker", allowed);
            return await request.ServiceProvider.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "handover-worker", "handover", "system-summary");
        }
        var result = await Run(grants); Assert.Equal(canReassign ? 1 : 0, result.Count);
        if (canReassign) Assert.Empty(result.Skipped); else Assert.Equal(instance.Tasks.Single().Id, Assert.Single(result.Skipped).TaskId);
        var committed = await Snapshot(host); scope.Hidden = instance.Id;
        var replay = await Run(grants); Assert.Equal(result.Count, replay.Count); Assert.Equal(result.Skipped, replay.Skipped);
        Assert.Equal(committed, await Snapshot(host));
        Assert.Equal(ApprovalErrorCode.Forbidden, (await Assert.ThrowsAsync<ApprovalException>(() => Run([ApprovalPermissions.TaskReassign]))).Code);
        Assert.Equal(committed, await Snapshot(host));
    }

    [Fact]
    public async Task BulkSummary_EmptyPlanDoesNotRequireInstanceRead()
    {
        await using var host = Host(new Permissions { Rule = r => r.Permission != ApprovalPermissions.InstanceRead }, access: new Access { Allow = false });
        var result = await Bulk(host); Assert.Equal(0, result.Count); Assert.Empty(result.Skipped);
        var committed = await Snapshot(host);
        var replay = await Bulk(host); Assert.Equal(0, replay.Count); Assert.Empty(replay.Skipped);
        Assert.Equal(2, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>().CountAsync()));
        Assert.Equal(committed, await Snapshot(host));
    }

    [Fact]
    public async Task OutboxReplay_QueryScopeRevokedRejectsWithoutRequeueOrWakeup()
    {
        var permissions = new Permissions(); var scope = new Scope();
        await using var host = Host(permissions, scope);
        await host.PublishAsync("p4", Flow); var instance = await host.StartAsync("p4", "one", "alice");
        var message = (await host.OutboxAsync()).First();
        Task<bool> Retry() => Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = message.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct));
        await Retry();
        var signal = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalOutboxSignal>()));
        while (await signal.WaitAsync(TimeSpan.Zero, default)) { }
        var before = await Snapshot(host); scope.Hidden = instance.Id;
        Assert.Equal(ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<ApprovalException>(() => Retry())).Code);
        Assert.Equal(before, await Snapshot(host)); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkPartialSaveFailure_OriginalPlanResumesWithCurrentPerTaskAuthorization(bool denyRemaining)
    {
        var permissions = new Permissions(); var failure = new ItemFailure();
        await using var host = Host(permissions, failure: failure);
        await host.PublishAsync("p4", Flow);
        var first = await host.StartAsync("p4", "first", "alice"); host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await host.StartAsync("p4", "second", "alice"); failure.InstanceId = second.Id; failure.Enabled = true;
        var secondBefore = await Snapshot(host, second.Id);
        await Assert.ThrowsAsync<IOException>(() => Bulk(host));
        Assert.Equal(secondBefore, await Snapshot(host, second.Id));
        Assert.Equal("carol", Assert.Single((await host.GetInstanceAsync(first.Id)).Tasks, t => t.Status == ApprovalTaskStatus.Pending).AssigneeValue);
        var firstCommitted = await Snapshot(host, first.Id);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(new[] { "task.reassign-bulk.item", "task.reassign-bulk.plan" },
                await db.Set<ApprovalOperation>().Select(o => o.Kind).OrderBy(k => k).ToListAsync());
        });
        host.Clock.Advance(TimeSpan.FromMinutes(1)); var added = await host.StartAsync("p4", "new-after-interruption", "alice");
        var addedBefore = await Snapshot(host, added.Id);
        failure.Enabled = false;
        permissions.Rule = r => !denyRemaining || r.Permission != ApprovalPermissions.TaskReassign || r.Resource.TaskId != second.Tasks.Single().Id;
        var resumed = await Bulk(host);
        Assert.Equal(denyRemaining ? 1 : 2, resumed.Count);
        if (denyRemaining)
        {
            var skipped = Assert.Single(resumed.Skipped); Assert.Equal(second.Id, skipped.InstanceId);
            Assert.Equal(second.Tasks.Single().Id, skipped.TaskId); Assert.Contains("权限", skipped.Reason);
            Assert.Equal(secondBefore, await Snapshot(host, second.Id));
        }
        else Assert.Empty(resumed.Skipped);
        Assert.Equal(firstCommitted, await Snapshot(host, first.Id)); Assert.Equal(addedBefore, await Snapshot(host, added.Id));
        var committed = await Snapshot(host);
        var replay = await Bulk(host); Assert.Equal(resumed.Count, replay.Count); Assert.Equal(resumed.Skipped, replay.Skipped);
        Assert.Equal(committed, await Snapshot(host));
        Assert.Equal(4, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>().CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkGrant_PerTaskDenialWithinSameInstancePreservesRejectedTasks(bool denyAll)
    {
        var permissions = new Permissions(); await using var host = Host(permissions);
        await host.PublishAsync("p4", Parallel); var instance = await host.StartAsync("p4", "one", "alice");
        Assert.Equal(2, instance.Tasks.Count);
        var denied = instance.Tasks.Single(t => t.NodeId == "b");
        var deniedJson = JsonSerializer.Serialize(denied);
        var beforeMessages = await host.OutboxAsync();
        permissions.Rule = r => r.Permission != ApprovalPermissions.TaskReassign || !denyAll && r.Resource.TaskId != denied.Id;
        var result = await Bulk(host);
        Assert.Equal(denyAll ? 0 : 1, result.Count); Assert.Equal(denyAll ? 2 : 1, result.Skipped.Count);
        Assert.Contains(result.Skipped, s => s.TaskId == denied.Id && s.InstanceId == instance.Id && s.Reason.Contains("权限"));
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(deniedJson, JsonSerializer.Serialize(saved.Tasks.Single(t => t.Id == denied.Id)));
        Assert.Equal(denyAll ? 2 : 3, saved.Tasks.Count);
        Assert.Equal(denyAll ? 0 : 1, saved.Logs.Count(l => l.Action == ApprovalAction.Reassign));
        Assert.DoesNotContain(saved.Logs, l => l.Action == ApprovalAction.Reassign && l.TaskId == denied.Id);
        Assert.DoesNotContain((await host.OutboxAsync()).ExceptBy(beforeMessages.Select(m => m.Id), m => m.Id), m => m.PayloadJson.Contains(denied.Id.ToString()));
        Assert.Empty(host.Handler.Calls);
    }

    [Theory]
    [InlineData("reassign")]
    [InlineData("terminate")]
    [InlineData("bulk")]
    public async Task ManagementCore_ForgedOperatorRejectsWithoutWrites(string command)
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow);
        var instance = await host.StartAsync("p4", "one", "alice"); var task = Assert.Single(instance.Tasks);
        var before = await Snapshot(host);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => command == "bulk" ? Bulk(host, actor: "alice") :
            Command(host, command == "reassign" ? ApprovalPermissions.TaskReassign : ApprovalPermissions.InstanceTerminate,
                new() { InstanceId = instance.Id }, (sp, ct) => command == "reassign"
                    ? sp.GetRequiredService<ApprovalEngine>().ReassignAsync(task.Id, "admin", "carol", "handover", ct)
                    : sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", "stop", ct), actor: "alice"));
        Assert.Equal(ApprovalErrorCode.Forbidden, error.Code); Assert.Equal(before, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("reassign")]
    [InlineData("terminate")]
    public async Task ManagementCore_MissingKeyAndNakedUnitRejectWithoutWrites(string command)
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow);
        var instance = await host.StartAsync("p4", "one", "alice"); var message = (await host.OutboxAsync()).First();
        var before = await Snapshot(host);
        foreach (var outerUnit in new[] { false, true })
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", async sp =>
            {
                Task Run(CancellationToken ct) => command switch
                {
                    "retry" => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct),
                    "reassign" => sp.GetRequiredService<ApprovalEngine>().ReassignAsync(instance.Tasks.Single().Id, "admin", "carol", ct: ct),
                    _ => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", "stop", ct)
                };
                if (outerUnit) await sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(Run); else await Run(default);
                return true;
            }));
            Assert.Equal(ApprovalErrorCode.OperationIdRequired, error.Code); Assert.Equal(before, await Snapshot(host));
        }
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("raw-transaction")]
    [InlineData("command-executor")]
    public async Task BulkCore_OuterBoundaryRejectsAndPreservesHostTracker(string boundary)
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow); await host.StartAsync("p4", "one", "alice");
        var before = await Snapshot(host);
        if (boundary == "command-executor")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Command(host, ApprovalPermissions.TaskReassignBulk, new() { TargetUserId = "bob" },
                async (sp, ct) => { await sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "admin", "handover", "bulk", ct); }));
        else
            await host.RunAsAsync("admin", async sp =>
            {
                var db = sp.GetRequiredService<TestDbContext>();
                var record = new TestRecord { Id = Guid.NewGuid(), Name = "uncommitted host work" }; db.Add(record);
                await using var transaction = boundary == "raw-transaction" ? await db.Database.BeginTransactionAsync() : null;
                await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "admin", "handover", "bulk"));
                Assert.Equal(EntityState.Added, db.Entry(record).State); Assert.Equal("uncommitted host work", record.Name);
                return true;
            });
        Assert.Equal(before, await Snapshot(host));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("canceled")]
    [InlineData("transferred")]
    [InlineData("ended-instance")]
    [InlineData("resubmit")]
    [InlineData("self")]
    [InlineData("duplicate")]
    [InlineData("inactive")]
    [InlineData("unqualified")]
    [InlineData("qualified-reason")]
    public async Task ReassignCore_InvalidStateOrAssigneeRejectsWithoutLeaks(string caseId)
    {
        var status = new Status(); await using var host = Host(new Permissions(), status: status);
        var flow = FlowDefinition.Parse(Flow);
        if (caseId == "duplicate") { flow.Nodes["review"].Mode = ApproveMode.All; flow.Nodes["review"].Approvers[0] = new("User", "bob,carol"); }
        if (caseId is "unqualified" or "qualified-reason")
        {
            flow.Nodes["review"].Approvers[0] = new("Role", "Reviewer");
            flow.Nodes["review"].RequireQualifiedAssignee = true; host.Identity.Add("bob", "Reviewer");
        }
        await host.PublishAsync("p4", flow.ToJson()); var instance = await host.StartAsync("p4", "one", "alice");
        var taskId = instance.Tasks.Single(t => t.AssigneeValue is "bob" or "Reviewer").Id;
        switch (caseId)
        {
            case "approved": await host.ApproveAsync(taskId, "bob"); break;
            case "canceled": await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().WithdrawAsync(instance.Id, "alice")); break;
            case "transferred": await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().TransferAsync(taskId, "bob", "other")); break;
            case "ended-instance":
                await host.SeedStorageAsync(async db => { (await db.Set<ApprovalInstance>().SingleAsync()).Status = InstanceStatus.Terminated; await db.SaveChangesAsync(); }); break;
            case "resubmit":
                await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(taskId, "bob", "revise", "start"));
                taskId = (await host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.Kind == ApprovalTaskKind.Resubmit).Id; break;
            case "qualified-reason": host.Identity.Add("carol", "Reviewer"); break;
            case "inactive": status.Inactive = "carol"; break;
        }
        var before = await Snapshot(host); var callbacks = host.Handler.Calls.Count;
        var target = caseId == "self" ? "alice" : "carol";
        var error = await Assert.ThrowsAsync<ApprovalException>(() => Command(host, ApprovalPermissions.TaskReassign, new() { TaskId = taskId },
            (sp, ct) => sp.GetRequiredService<ApprovalEngine>().ReassignAsync(taskId, "admin", target, caseId == "qualified-reason" ? " " : "handover", ct)));
        var expected = caseId switch
        {
            "approved" or "canceled" or "ended-instance" => "流程已结束", "transferred" => "任务已处理",
            "resubmit" => "退回发起人", "self" => "发起人", "duplicate" => "已是该节点的审批人",
            "inactive" => "停用", "unqualified" => "资格", _ => "必须填写原因"
        };
        Assert.Contains(expected, error.Message);
        Assert.Equal(before, await Snapshot(host)); Assert.Equal(callbacks, host.Handler.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReassignCore_WaitingOrSuspendedKeepsScheduleAndDefersNotification(bool suspended)
    {
        await using var host = Host(new Permissions()); var flow = FlowDefinition.Parse(Flow);
        flow.Nodes["review"].Notifications.Add(new() { Events = [NotifyEvent.Transferred], Recipients = [new("Assignees", "")], Channels = ["Test"] });
        if (!suspended) { flow.Nodes["review"].Mode = ApproveMode.Sequential; flow.Nodes["review"].Approvers[0] = new("User", "other,bob"); }
        await host.PublishAsync("p4", flow.ToJson()); var instance = await host.StartAsync("p4", "one", "alice");
        if (suspended) await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().AddSignAsync(instance.Tasks.Single().Id, "bob", ["other"], AddSignMode.Before));
        var original = (await host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "bob");
        Assert.Equal(suspended ? ApprovalTaskStatus.Suspended : ApprovalTaskStatus.Waiting, original.Status);
        if (suspended) Assert.NotNull(original.DueAt); else Assert.Null(original.DueAt);
        var messages = await host.OutboxAsync();
        await Command(host, ApprovalPermissions.TaskReassign, new() { TaskId = original.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalEngine>().ReassignAsync(original.Id, "admin", "carol", "handover", ct));
        var saved = await host.GetInstanceAsync(instance.Id); var target = saved.Tasks.Single(t => t.AssigneeValue == "carol");
        Assert.Equal((original.Status, original.DueAt, original.NextRemindAt, original.Order, original.ParentTaskId, original.Kind),
            (target.Status, target.DueAt, target.NextRemindAt, target.Order, target.ParentTaskId, target.Kind));
        Assert.Equal(ApprovalTaskStatus.Transferred, saved.Tasks.Single(t => t.Id == original.Id).Status);
        var created = Assert.Single((await host.OutboxAsync()).ExceptBy(messages.Select(m => m.Id), m => m.Id));
        Assert.Equal("Reassign", created.Description); Assert.Contains(original.Id.ToString(), created.PayloadJson);
        Assert.Empty(await host.NotificationOutboxAsync());
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Reassign && l.TaskId == original.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminateCore_RunningOrReturnedCancelsTasksAndReleasesSlot(bool returned)
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Parallel);
        var instance = await host.StartAsync("p4", "one", "alice");
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().AddSignAsync(instance.Tasks.First().Id, "bob", ["other"], AddSignMode.Before));
        if (returned)
            await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(instance.Tasks.Single(t => t.NodeId == "b").Id, "bob", "revise", "start"));
        var current = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(returned ? InstanceStatus.Returned : InstanceStatus.Running, current.Status);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().CountAsync()));
        var before = await Snapshot(host);
        await Assert.ThrowsAsync<ApprovalException>(() => Command(host, ApprovalPermissions.InstanceTerminate, new() { InstanceId = instance.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", " ", ct), key: "empty-reason"));
        Assert.Equal(before, await Snapshot(host));
        await Command(host, ApprovalPermissions.InstanceTerminate, new() { InstanceId = instance.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", "business canceled", ct));
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Terminated, saved.Status); Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, saved.CompletedAt); Assert.Null(saved.StateJson);
        Assert.DoesNotContain(saved.Tasks, t => t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended);
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().CountAsync()));
        var log = Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Terminate);
        Assert.Equal(("admin", "business canceled"), (log.OperatorId, log.Comment));
        Assert.Equal(("Rejected", "one"), Assert.Single(host.Handler.Calls));
        Assert.Single(await host.OutboxAsync(), m => m.InstanceId == instance.Id && m.Description == "Completed");
        var committed = await Snapshot(host);
        await Command(host, ApprovalPermissions.InstanceTerminate, new() { InstanceId = instance.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", "business canceled", ct));
        Assert.Equal(committed, await Snapshot(host)); Assert.Single(host.Handler.Calls);
    }

    [Fact]
    public async Task RetryCore_OuterRollbackPreservesFailedStateAndOnlyCommitWakes()
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow); await host.StartAsync("p4", "one", "alice");
        var message = (await host.OutboxAsync()).First();
        await host.SeedStorageAsync(async db =>
        {
            var saved = await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == message.Id);
            saved.Status = OutboxStatus.Failed; saved.Attempts = 3; saved.CompletedAt = host.Clock.GetUtcNow().UtcDateTime;
            saved.LastError = "original failure"; saved.ConcurrencyStamp = 7; await db.SaveChangesAsync();
        });
        var before = await Snapshot(host); var signal = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalOutboxSignal>()));
        while (await signal.WaitAsync(TimeSpan.Zero, default)) { }
        await Assert.ThrowsAsync<IOException>(() => Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = message.Id }, async (sp, ct) =>
        {
            await sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct);
            Assert.False(await signal.WaitAsync(TimeSpan.Zero, default)); throw new IOException("outer rollback");
        }));
        Assert.Equal(before, await Snapshot(host)); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default));
        await Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = message.Id }, async (sp, ct) =>
        { await sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default)); });
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, default)); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default));
        var queued = (await host.OutboxAsync()).Single(m => m.Id == message.Id);
        Assert.Equal((OutboxStatus.Pending, 0, 8L, "original failure"), (queued.Status, queued.Attempts, queued.ConcurrencyStamp, queued.LastError)); Assert.Null(queued.CompletedAt);
        var committed = await Snapshot(host);
        await Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = message.Id }, (sp, ct) => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct));
        Assert.Equal(committed, await Snapshot(host)); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default));
        await host.SeedStorageAsync(async db => { (await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == message.Id)).Status = OutboxStatus.Succeeded; await db.SaveChangesAsync(); });
        var succeeded = await Snapshot(host);
        await Assert.ThrowsAsync<ApprovalException>(() => Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = message.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id, ct), key: "new-intent"));
        Assert.Equal(succeeded, await Snapshot(host)); Assert.False(await signal.WaitAsync(TimeSpan.Zero, default));
    }

    private sealed class FailingTermination(TestDbContext db) : IApprovalBusinessHandler
    {
        public string BusinessType => RecordingBusinessHandler.Type;
        public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
        public Task OnTerminatedAsync(ApprovalInstanceContext instance, CancellationToken ct)
        {
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "termination callback" });
            throw new IOException("termination callback failed");
        }
    }
    [Fact]
    public async Task TerminateCore_CallbackFailureRollsBackHostAndApprovalAndInvalidatesScope()
    {
        await using var host = new TestHost(o => o.Outbox.EnableDispatcher = false, b =>
        { b.Services.AddScoped<IApprovalBusinessHandler, FailingTermination>(); b.Services.AddSingleton<IApprovalPermissionProvider>(new Permissions()); });
        await host.PublishAsync("p4", Parallel); var instance = await host.StartAsync("p4", "one", "alice");
        var before = await Snapshot(host);
        await Assert.ThrowsAsync<IOException>(() => Command(host, ApprovalPermissions.InstanceTerminate, new() { InstanceId = instance.Id }, async (sp, ct) =>
        {
            var failure = await Assert.ThrowsAsync<IOException>(() => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.Id, "admin", "stop", ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<TestDbContext>().SaveChangesAsync(ct));
            throw failure;
        }));
        Assert.Equal(before, await Snapshot(host));
    }

    [Theory]
    [InlineData("invalid-definition")]
    [InlineData("missing-node")]
    [InlineData("business-slots")]
    public async Task DiagnosticsCore_CorruptResourcesAreReadOnlyAndExposeActualFindings(string corruption)
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow);
        var instance = await host.StartAsync("p4", "one", "alice");
        if (corruption == "business-slots") await host.StartAsync("p4", "stale-slot", "alice");
        await host.SeedStorageAsync(async db =>
        {
            if (corruption == "invalid-definition") (await db.Set<ApprovalDefinition>().SingleAsync()).DefinitionJson = "{}";
            else if (corruption == "missing-node") (await db.Set<ApprovalTask>().SingleAsync()).NodeId = "missing";
            else
            {
                db.Remove(await db.Set<ApprovalBusinessSlot>().SingleAsync(s => s.InstanceId == instance.Id));
                (await db.Set<ApprovalInstance>().SingleAsync(i => i.BusinessKey == "stale-slot")).Status = InstanceStatus.Approved;
            }
            await db.SaveChangesAsync();
        });
        var before = await Snapshot(host);
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.True(report.DataAvailable);
        if (corruption == "business-slots") Assert.Equal((1, 1), (report.MissingBusinessSlots, report.StaleBusinessSlots));
        else Assert.Equal(corruption == "invalid-definition" ? "invalid_definition" : "missing_node", Assert.Single(report.Findings).Code);
        Assert.Equal(before, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
    }

    [Theory]
    [InlineData("read-permission")]
    [InlineData("query-scope")]
    [InlineData("access-policy")]
    public async Task BulkPartialReplay_RevokedCommittedItemStopsBeforeRemainingInstance(string revocation)
    {
        var permissions = new Permissions(); var scope = new Scope(); var access = new Access(); var failure = new ItemFailure();
        await using var host = Host(permissions, scope, access, failure);
        await host.PublishAsync("p4", Flow); var first = await host.StartAsync("p4", "first", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(1)); var second = await host.StartAsync("p4", "second", "alice");
        failure.InstanceId = second.Id; failure.Enabled = true;
        await Assert.ThrowsAsync<IOException>(() => Bulk(host)); failure.Enabled = false;
        var partial = await Snapshot(host);
        if (revocation == "read-permission") permissions.Rule = r => r.Permission != ApprovalPermissions.InstanceRead;
        else if (revocation == "query-scope") scope.Hidden = first.Id;
        else access.Allow = false;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => Bulk(host));
        Assert.Equal(revocation == "read-permission" ? ApprovalErrorCode.Forbidden : ApprovalErrorCode.NotFound, error.Code);
        Assert.Equal(partial, await Snapshot(host));
        permissions.Rule = _ => true; scope.Hidden = Guid.Empty; access.Allow = true;
        Assert.Equal(2, (await Bulk(host)).Count);
        Assert.Single((await host.GetInstanceAsync(first.Id)).Logs, l => l.Action == ApprovalAction.Reassign);
        Assert.Single((await host.GetInstanceAsync(second.Id)).Logs, l => l.Action == ApprovalAction.Reassign);
    }

    private sealed class DeduplicatingEvents : IApprovalEventHandler
    {
        public List<Guid> Attempts { get; } = [];
        public HashSet<Guid> ExternalEffects { get; } = [];
        public Task HandleAsync(ApprovalEvent value, CancellationToken ct)
        {
            if (value.EventType != "Submit") return Task.CompletedTask;
            Attempts.Add(value.Id); ExternalEffects.Add(value.Id);
            if (Attempts.Count == 1) throw new IOException("external effect succeeded but acknowledgement lost");
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task OutboxAtLeastOnce_HostDeduplicatesExternalEffectByStableEventId()
    {
        var events = new DeduplicatingEvents();
        await using var host = new TestHost(o => { o.EnableBackgroundServices = false; o.Outbox.MaxAttempts = 1; }, b =>
        { b.Services.AddSingleton<IApprovalEventHandler>(events); b.Services.AddSingleton<IApprovalPermissionProvider>(new Permissions()); });
        await host.PublishAsync("p4", Flow); await host.StartAsync("p4", "one", "alice"); await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync(), m => m.Description == "Submit");
        Assert.Equal(OutboxStatus.Failed, failed.Status); Assert.Equal(failed.Id, Assert.Single(events.ExternalEffects));
        await Command(host, ApprovalPermissions.OutboxRetry, new() { OutboxId = failed.Id },
            (sp, ct) => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(failed.Id, ct));
        await host.DispatchOutboxAsync();
        Assert.Equal(new[] { failed.Id, failed.Id }, events.Attempts); Assert.Equal(failed.Id, Assert.Single(events.ExternalEffects));
        Assert.Equal(OutboxStatus.Succeeded, (await host.OutboxAsync()).Single(m => m.Id == failed.Id).Status);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>().CountAsync()));
    }

    [Fact]
    public async Task DiagnosticsCore_BackgroundFailureIsReportedWithoutRepairWrites()
    {
        await using var host = Host(new Permissions()); await host.PublishAsync("p4", Flow); await host.StartAsync("p4", "one", "alice");
        var before = await Snapshot(host);
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<ApprovalBackgroundRunner>()
            .RunAsync("probe", (_, _) => Task.FromException<int>(new IOException("controlled background failure")))));
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.False(Assert.Single(report.Heartbeats).Succeeded); Assert.True(report.DataAvailable);
        Assert.Equal(before, await Snapshot(host)); Assert.Empty(host.Handler.Calls);
    }
}
