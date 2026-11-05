using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class CompetingIdentity : IApprovalIdentityProvider
    {
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(role == "Reviewers" && userId is "bob" or "carol");
        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(role == "Reviewers" ? ["bob", "carol"] : []);
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(userId is "bob" or "carol" ? ["Reviewers"] : []);
    }

    private sealed class Writes : DbCommandInterceptor
    {
        public ConcurrentDictionary<Guid, ConcurrentQueue<string>> Commands { get; } = new();
        public bool Synchronize;
        private readonly object _sync = new();
        private Guid? _firstWriter;
        private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Committed(Guid context)
        {
            lock (_sync) if (_firstWriter == context) _committed.TrySetResult();
        }
        private async Task BeforeWrite(DbCommand command, CommandEventData data)
        {
            if (!Regex.IsMatch(command.CommandText, @"\b(?:INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return;
            var context = data.Context!.ContextId.InstanceId;
            Commands.GetOrAdd(context, _ => new()).Enqueue(command.CommandText);
            if (!Synchronize) return;
            bool first;
            lock (_sync) { _firstWriter ??= context; first = _firstWriter == context; }
            if (!first) await _committed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { await BeforeWrite(command, data); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        { await BeforeWrite(command, data); return result; }
    }

    private sealed record Attempt(string Actor, Guid Context, Guid Marker, ApprovalException? Error);

    [Fact]
    public async Task SameTask_SynchronizedApprovalsCommitExactlyOneDecisionAndRollbackLoser()
    {
        var writes = new Writes();
        await using var host = await AtomicHostAsync(new AtomicFaults(), configure: o => o.NotificationTargets.Add(new("Audit", "Audit")),
            configureServices: services =>
            {
                services.AddSingleton<IApprovalIdentityProvider, CompetingIdentity>();
                services.ConfigureDbContext<AtomicContractContext>(o => o.ReplaceService<IExecutionStrategyFactory, ReadOnlyStrategyFactory>().AddInterceptors(writes));
            });
        await using (var storage = fixture.CreateStorage())
        {
            var definition = await storage.Set<ApprovalDefinition>().SingleAsync();
            var flow = FlowDefinition.Parse(definition.DefinitionJson);
            flow.Nodes["review"].Approvers = [new("Role", "Reviewers")];
            flow.Nodes["review"].Notifications = [new() { Events = [NotifyEvent.Approved], Recipients = [new("Initiator", "")],
                Channels = ["InApp", "Audit"], Title = "Decision notice", Content = "Decision committed" }];
            definition.DefinitionJson = flow.ToJson(); await storage.SaveChangesAsync();
        }
        var instance = await StartReadOnlyInstance(host);
        var taskId = Assert.Single(instance.Tasks).Id;
        Guid[] originalOutbox;
        await using (var storage = fixture.CreateStorage()) originalOutbox = await storage.Set<ApprovalOutboxMessage>().Select(m => m.Id).ToArrayAsync();
        writes.Commands.Clear();
        // 两方都先读旧状态并通过校验。首个真实写命令取得提交资格，另一方等其提交后写入；
        // 避免日志多行INSERT的行锁顺序引入随机死锁。SQLite自身还会在BEGIN串行化写入。
        writes.Synchronize = true;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var committedCallbacks = new ConcurrentBag<string>();
        async Task<Attempt> Approve(string actor)
        {
            await using var scope = host.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            // Keep both stale graphs in their original contexts; keyed user races are covered by ParallelConcurrencyTests.
            sp.GetRequiredService<ApprovalExecutionContext>().InitializeSystem(actor, [ApprovalPermissions.SessionRead, ApprovalPermissions.TaskRead, ApprovalPermissions.TaskApprove]);
            var db = sp.GetRequiredService<AtomicContractContext>();
            var engine = sp.GetRequiredService<ApprovalEngine>();
            Assert.True(await engine.CanHandleAsync(taskId, actor));
            var tracked = Assert.Single(db.ChangeTracker.Entries<ApprovalInstance>()).Entity;
            Assert.Equal(instance.ConcurrencyStamp, tracked.ConcurrencyStamp);
            Assert.Equal(instance.LogSequence, tracked.LogSequence);
            Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(tracked.Tasks).Status);
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var marker = Guid.NewGuid();
            try
            {
                await sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
                {
                    db.Add(new AtomicHostRecord { Id = marker, Value = actor == "bob" ? 77 : 88 });
                    sp.GetRequiredService<IApprovalUnitOfWork>().AfterCommit(() =>
                    { committedCallbacks.Add(actor); writes.Committed(db.ContextId.InstanceId); return Task.CompletedTask; });
                    await engine.ApproveAsync(taskId, actor, ct: ct);
                });
                return new(actor, db.ContextId.InstanceId, marker, null);
            }
            catch (ApprovalException error)
            {
                Assert.Equal(ApprovalErrorCode.Conflict, error.Code);
                Assert.IsAssignableFrom<DbUpdateException>(error.InnerException);
                Assert.IsNotType<ApprovalException>(error.InnerException);
                await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(_ => Task.CompletedTask));
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                return new(actor, db.ContextId.InstanceId, marker, error);
            }
        }
        var attempts = await Task.WhenAll(Task.Run(() => Approve("bob")), Task.Run(() => Approve("carol")));
        Assert.Equal(2, arrived); Assert.NotEqual(attempts[0].Context, attempts[1].Context);
        var winner = Assert.Single(attempts, a => a.Error is null);
        var loser = Assert.Single(attempts, a => a.Error is not null);
        Assert.Equal(winner.Actor, Assert.Single(committedCallbacks));
        await using var verify = fixture.CreateStorage();
        var saved = await verify.Set<ApprovalInstance>().Include(i => i.Tasks).Include(i => i.Logs).SingleAsync();
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Equal(instance.LogSequence + 3, saved.LogSequence);
        var decisionLogs = saved.Logs.Where(l => l.Sequence > instance.LogSequence).OrderBy(l => l.Sequence).ToList();
        Assert.Equal(new[] { ApprovalAction.Approve, ApprovalAction.NodeEntered, ApprovalAction.Complete }, decisionLogs.Select(l => l.Action));
        Assert.Equal(new[] { instance.LogSequence + 1, instance.LogSequence + 2, instance.LogSequence + 3 }, decisionLogs.Select(l => l.Sequence));
        Assert.Equal("end", decisionLogs[1].NodeId);
        var task = Assert.Single(saved.Tasks);
        Assert.Equal(ApprovalTaskStatus.Approved, task.Status); Assert.Equal(winner.Actor, task.HandledBy);
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Approve);
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Complete);
        Assert.Equal(saved.Logs.Count, saved.Logs.Select(l => l.Sequence).Distinct().Count());
        Assert.Equal(0, await verify.Set<ApprovalBusinessSlot>().CountAsync());
        var messages = await verify.Set<ApprovalMessage>().ToListAsync();
        var message = Assert.Single(messages); Assert.Equal(NotifyEvent.Approved, message.Event); Assert.Equal("alice", message.UserId);
        var outbox = await verify.Set<ApprovalOutboxMessage>().ToListAsync();
        var decisionOutbox = outbox.Where(m => !originalOutbox.Contains(m.Id)).ToList();
        Assert.Equal(5, decisionOutbox.Count);
        foreach (var description in new[] { "Approve", "Complete", "NodeEntered", "Completed", "Decision notice" })
            Assert.Single(decisionOutbox, m => m.Description == description);
        Assert.Single(decisionOutbox, m => m.PayloadJson.Contains("\"eventType\":\"NotificationRequested\"", StringComparison.Ordinal));
        await using var businessScope = host.CreateAsyncScope();
        var business = await businessScope.ServiceProvider.GetRequiredService<AtomicContractContext>().Set<AtomicHostRecord>().ToListAsync();
        Assert.Single(business, r => r.Value == 10); Assert.Single(business, r => r.Id == winner.Marker);
        Assert.DoesNotContain(business, r => r.Id == loser.Marker); Assert.Equal(2, business.Count);
        await WriteConflictEvidence("same-task", new { provider = fixture.Provider, attempts = attempts.Select(a => new
            { a.Actor, a.Context, success = a.Error is null, errorCode = a.Error?.Code.ToString(), database = DescribeDatabaseError(a.Error) }),
            saved.ConcurrencyStamp, saved.LogSequence, status = saved.Status.ToString(), handledBy = task.HandledBy,
            logs = saved.Logs.OrderBy(l => l.Sequence).Select(l => new { l.Sequence, action = l.Action.ToString() }),
            outbox = decisionOutbox.Select(m => m.Description), messages = messages.Count, businessRecords = business.Count,
            callbacks = committedCallbacks.Count, writes = attempts.Select(a => new { a.Context, commands = writes.Commands.GetValueOrDefault(a.Context)?.ToArray() }) });
    }

    [Fact]
    public async Task OriginalStaleApproval_PreservesCommittedWinnerWithoutLeakingDatabaseException()
    {
        await using var host = await ReadOnlyHostAsync();
        var instance = await StartReadOnlyInstance(host); var taskId = Assert.Single(instance.Tasks).Id;
        await using var stale = host.CreateAsyncScope();
        var sp = stale.ServiceProvider;
        sp.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("bob", [ApprovalPermissions.TaskApprove]);
        await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
        await ApproveReadOnlyInstance(host, taskId);
        ApprovalInstance winner; Guid[] outbox;
        await using (var storage = fixture.CreateStorage())
        {
            winner = await storage.Set<ApprovalInstance>().Include(i => i.Logs).SingleAsync();
            outbox = await storage.Set<ApprovalOutboxMessage>().Select(m => m.Id).ToArrayAsync();
        }
        var error = await Assert.ThrowsAsync<ApprovalException>(() => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, "bob"));
        Assert.Equal(ApprovalErrorCode.Conflict, error.Code); Assert.IsAssignableFrom<DbUpdateException>(error.InnerException);
        await using var verify = fixture.CreateStorage();
        var saved = await verify.Set<ApprovalInstance>().Include(i => i.Tasks).Include(i => i.Logs).SingleAsync();
        Assert.Equal(InstanceStatus.Approved, saved.Status); Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Equal(ApprovalTaskStatus.Approved, Assert.Single(saved.Tasks).Status);
        Assert.Equal(winner.Logs.Select(l => l.Id).Order(), saved.Logs.Select(l => l.Id).Order());
        Assert.Equal(outbox.Order(), (await verify.Set<ApprovalOutboxMessage>().Select(m => m.Id).ToListAsync()).Order());
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Approve);
        await using var businessScope = host.CreateAsyncScope();
        Assert.Equal(1, await businessScope.ServiceProvider.GetRequiredService<AtomicContractContext>().Set<AtomicHostRecord>().CountAsync(r => r.Value == 10));
        await WriteConflictEvidence("original-stale", new { provider = fixture.Provider, success = 1, conflicts = 1, database = DescribeDatabaseError(error), saved.ConcurrencyStamp });
    }

    [Theory]
    [InlineData("sequence", true)]
    [InlineData("log-primary", false)]
    [InlineData("business-slot", false)]
    [InlineData("foreign-key", false)]
    public async Task RealProviderConstraints_IdentifyOnlyApprovalLogSequence(string constraint, bool expected)
    {
        await ClearDataAsync();
        await using var db = fixture.CreateStorage();
        var definition = Definition("constraints"); var first = Instance(); var second = Instance();
        first.DefinitionId = definition.Id; second.DefinitionId = definition.Id;
        var logId = Guid.NewGuid();
        db.AddRange(definition, first, second, new ApprovalLog { Id = logId, InstanceId = first.Id, Sequence = 1,
            Action = ApprovalAction.Remind, OperatorId = "existing", CreatedAt = Timestamp });
        if (constraint == "business-slot") db.Add(new ApprovalBusinessSlot { Id = Guid.NewGuid(), InstanceId = first.Id,
            LookupHash = "duplicate", BusinessType = "Order", BusinessKey = "one" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var target = constraint == "foreign-key" ? Guid.NewGuid() : constraint == "business-slot" ? second.Id : first.Id;
        db.Add(new ApprovalLog { Id = constraint == "log-primary" ? logId : Guid.NewGuid(),
            InstanceId = target, Sequence = constraint == "log-primary" ? 2 : 1,
            Action = ApprovalAction.Approve, OperatorId = "attempt", CreatedAt = Timestamp });
        if (constraint == "business-slot") db.Add(new ApprovalBusinessSlot { Id = Guid.NewGuid(), InstanceId = second.Id,
            LookupHash = "duplicate", BusinessType = "Order", BusinessKey = "two" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(expected, IsLogSequence(error, db, target));
        Assert.Equal(expected, IsLogSequence(error, db, null)); // UnitOfWork 使用的实际实例归属推断路径。
        Assert.Equal(constraint != "foreign-key", IsUnique(error));
        Assert.Equal(constraint == "business-slot", OwnedConflict("BusinessSlotConflict", error, db));
        Assert.False(OwnedConflict("OperationConflict", error, db));
        Assert.False(OwnedConflict("GovernanceConflict", error, db));
        Assert.NotNull(DescribeDatabaseError(error));
        await using var verify = fixture.CreateStorage();
        Assert.Equal(1, await verify.Set<ApprovalLog>().CountAsync());
        Assert.Equal(constraint == "business-slot" ? 1 : 0, await verify.Set<ApprovalBusinessSlot>().CountAsync());
        await WriteConflictEvidence("identify-" + constraint, new { provider = fixture.Provider, matched = expected, database = DescribeDatabaseError(error),
            entries = error.Entries.Select(e => e.Metadata.ClrType.Name) });
    }

    [Fact]
    public async Task RealProviderSqlError_IsNotApprovalLogConflict()
    {
        await using var db = fixture.CreateStorage();
        var sql = fixture.Provider == "MySql" ? "SELECT Approval_Column_That_Does_Not_Exist FROM `ApprovalLogs`"
            : "SELECT Approval_Column_That_Does_Not_Exist FROM \"ApprovalLogs\"";
        var error = await Assert.ThrowsAnyAsync<DbException>(() => db.Database.ExecuteSqlRawAsync(sql));
        var log = new ApprovalLog { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), OperatorId = "probe" };
        var update = new DbUpdateException("SQL error", error, [db.Entry(log)]);
        Assert.False(IsLogSequence(update, db, log.InstanceId)); Assert.False(IsUnique(update));
        Assert.False(OwnedConflict("OperationConflict", update, db));
        Assert.False(OwnedConflict("BusinessSlotConflict", update, db));
        Assert.False(OwnedConflict("GovernanceConflict", update, db));
        await WriteConflictEvidence("identify-sql", new { provider = fixture.Provider, matched = false, database = DescribeDatabaseError(error) });
    }

    [Fact]
    public async Task RealProviderConnectionFailure_IsNotApprovalLogConflict()
    {
        await using var db = fixture.CreateStorage();
        await using var broken = (DbConnection)Activator.CreateInstance(db.Database.GetDbConnection().GetType())!;
        if (fixture.Provider == "Sqlite")
            broken.ConnectionString = "Data Source=" + Path.Combine(Path.GetTempPath(), "approval-missing-" + Guid.NewGuid().ToString("N"), "missing.db");
        else
        {
            var settings = new DbConnectionStringBuilder { ConnectionString = db.Database.GetConnectionString()! };
            settings["Database"] = "approval_missing_" + Guid.NewGuid().ToString("N");
            broken.ConnectionString = settings.ConnectionString;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var error = await Assert.ThrowsAnyAsync<DbException>(() => broken.OpenAsync(timeout.Token));
        var log = new ApprovalLog { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), OperatorId = "probe" };
        var update = new DbUpdateException("Connection error", error, [db.Entry(log)]);
        Assert.False(IsLogSequence(update, db, log.InstanceId)); Assert.False(IsUnique(update));
        await WriteConflictEvidence("identify-connection", new { provider = fixture.Provider, matched = false, database = DescribeDatabaseError(error) });
    }

    // 使用实际 Provider 异常验证内部识别器，不扩大 Core 的 friend/public API 边界。
    private static bool IsLogSequence(DbUpdateException error, DbContext db, Guid? instanceId) =>
        (bool)typeof(ApprovalEngine).Assembly.GetType("Approval.Core.Persistence.ApprovalDatabaseErrors")!
            .GetMethod("LogSequence")!.Invoke(null, [error, db, instanceId])!;
    private static bool IsUnique(Exception error) =>
        (bool)typeof(ApprovalEngine).Assembly.GetType("Approval.Core.Persistence.ApprovalDatabaseErrors")!
            .GetMethod("Unique")!.Invoke(null, [error])!;

    private async Task WriteConflictEvidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("APPROVAL_TEST_REPORT_DIRECTORY") is { } directory)
            await File.WriteAllTextAsync(Path.Combine(directory, "conflict-" + name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object? DescribeDatabaseError(Exception? error)
    {
        for (var current = error; current is not null; current = current.InnerException)
            if (current is DbException)
                return new { type = current.GetType().FullName, number = current.GetType().GetProperty("Number")?.GetValue(current),
                    sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current), constraint = current.GetType().GetProperty("ConstraintName")?.GetValue(current),
                    table = current.GetType().GetProperty("TableName")?.GetValue(current), sqlite = current.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(current),
                    current.Message };
        return new { type = error?.InnerException?.GetType().FullName };
    }
}
