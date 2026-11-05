using Approval.Forms;
using System.Collections.Concurrent;
using System.Data.Common;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed class AtomicHostRecord
{
    public Guid Id { get; set; }
    public int Value { get; set; }
    public Guid? ParentId { get; set; }
}
public sealed class AtomicContractContext(DbContextOptions<AtomicContractContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    { base.OnConfiguring(options); options.UseApproval(); }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureApproval().ConfigureApprovalForms();
        model.Entity<AtomicHostRecord>(b =>
        {
            b.ToTable("AtomicHostRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<string>().HasMaxLength(36).IsUnicode(false);
            b.Property(r => r.ParentId).HasConversion<string>().HasMaxLength(36).IsUnicode(false);
            b.HasOne<AtomicHostRecord>().WithMany().HasForeignKey(r => r.ParentId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
internal sealed class AtomicResolver : IApprovalContextResolver
{
    public string User { get; set; } = "alice";
    public ApprovalExecutionIdentity Resolve() => new(User);
}
internal sealed class AtomicPermissions : IApprovalPermissionProvider
{
    public Func<ApprovalPermissionRequest, bool> Rule { get; set; } = _ => true;
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(Rule(request));
}
internal sealed class AtomicIdentity : IApprovalIdentityProvider, IApprovalUserStatusProvider
{
    public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(!string.IsNullOrWhiteSpace(userId));
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
internal sealed class AtomicTransientException : Exception { }
internal sealed class AtomicStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new AtomicStrategy(dependencies);
    private sealed class AtomicStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 6, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is AtomicTransientException) return true;
                var type = current.GetType();
                if (type.GetProperty("SqliteErrorCode")?.GetValue(current) is int code && code is 5 or 6) return true;
                if (type.GetProperty("Number")?.GetValue(current) is int number && number is 1205 or 1213) return true;
                if (type.GetProperty("SqlState")?.GetValue(current) is "40001" or "40P01") return true;
            }
            return false;
        }
    }
}
internal sealed class AtomicFaults : DbTransactionInterceptor
{
    public int BeforeCommit, AfterCommit, BusinessCalls, ActionCalls;
    public bool BlockVerification, FailVerificationAfterCommit, FailCallback, AsyncSaveThenFail, IgnoreActionCancellation;
    public readonly ConcurrentBag<Guid> Contexts = [];
    public readonly ConcurrentBag<Guid> OutboxIds = [];
    public TaskCompletionSource? Entered, Release;
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref BeforeCommit, 0) == 1) throw new AtomicTransientException();
        return ValueTask.FromResult(result);
    }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref AfterCommit, 0) == 1) { BlockVerification = FailVerificationAfterCommit; throw new AtomicTransientException(); }
        return Task.CompletedTask;
    }
}
internal sealed class AtomicVerificationFault(AtomicFaults faults) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        if (faults.BlockVerification && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal)) throw new InvalidOperationException("verification connection unavailable");
        return ValueTask.FromResult(result);
    }
}
internal sealed class AtomicBusiness(AtomicContractContext db, AtomicFaults faults) : IApprovalBusinessHandler
{
    public const string Type = "Atomic";
    public string BusinessType => Type;
    public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct)
    { db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 10 }); if (faults.FailCallback) throw new InvalidOperationException("business callback failed"); return Task.CompletedTask; }
    public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
}
internal sealed class AtomicAction(AtomicContractContext db, AtomicFaults faults) : IApprovalEventHandler
{
    public async Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        if (approvalEvent.EventType != "Submit") return;
        var number = Interlocked.Increment(ref faults.ActionCalls);
        if (number == 1 && faults.Entered is not null)
        {
            faults.Entered.TrySetResult();
            if (faults.IgnoreActionCancellation) await faults.Release!.Task;
            else await faults.Release!.Task.WaitAsync(ct);
        }
        db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 20 });
        await db.SaveChangesAsync(ct);
        if (faults.AsyncSaveThenFail) throw new InvalidOperationException("async action failed after saving");
    }
}

public sealed partial class DatabaseContractTests
{
    private const string AtomicFlow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private async Task<ServiceProvider> AtomicHostAsync(AtomicFaults faults, bool initializeDatabase = true, Action<ApprovalOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        if (initializeDatabase)
        {
            await ClearDataAsync();
            await using (var schema = fixture.Create())
            {
                await schema.Database.OpenConnectionAsync(); await using var command = schema.Database.GetDbConnection().CreateCommand();
                var q = fixture.Provider == "MySql" ? '`' : '"';
                var table = $"{q}AtomicHostRecords{q}";
                var create = $"CREATE TABLE {table} ({q}Id{q} varchar(36) NOT NULL PRIMARY KEY, {q}Value{q} integer NOT NULL, " +
                    $"{q}ParentId{q} varchar(36) NULL, CONSTRAINT {q}FK_AtomicHostRecords_Parent{q} FOREIGN KEY ({q}ParentId{q}) REFERENCES {table} ({q}Id{q}))";
                command.CommandText = fixture.Provider == "SqlServer" ? $"IF OBJECT_ID('AtomicHostRecords', 'U') IS NULL {create}" : create.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS");
                await command.ExecuteNonQueryAsync(); command.CommandText = $"DELETE FROM {table}"; await command.ExecuteNonQueryAsync();
                if (fixture.Provider == "Sqlite") { command.CommandText = "PRAGMA journal_mode=WAL"; await command.ExecuteScalarAsync(); }
            }
        }
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(faults);
        services.AddScoped<IApprovalContextResolver, AtomicResolver>();
        services.AddScoped<IApprovalIdentityProvider, AtomicIdentity>(); services.AddSingleton<IApprovalPermissionProvider, AtomicPermissions>();
        services.AddScoped<IApprovalUserStatusProvider, AtomicIdentity>();
        services.AddDbContext<AtomicContractContext>(options =>
        {
            var connection = Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION");
            switch (fixture.Provider)
            {
                case "MySql": options.UseMySQL(connection!); break;
                case "SqlServer": options.UseSqlServer(connection!); break;
                case "PostgreSql": options.UseNpgsql(connection!); break;
                default: options.UseSqlite($"Data Source={DatabaseFixture.SqlitePath};Default Timeout=10"); break;
            }
            options.ReplaceService<IExecutionStrategyFactory, AtomicStrategyFactory>(); options.AddInterceptors(faults, new AtomicVerificationFault(faults));
        });
        services.AddApprovalWorkflow<AtomicContractContext>(o => { o.EnableBackgroundServices = false; configure?.Invoke(o); })
            .AddBusinessHandler<AtomicBusiness>().AddEventHandler<AtomicAction>();
        configureServices?.Invoke(services);
        var provider = services.BuildServiceProvider();
        if (initializeDatabase)
        {
            // The fixture imports a historical definition through an explicit offline storage context.
            await using var db = fixture.CreateStorage();
            var definition = new ApprovalDefinition { Id = Guid.NewGuid(), Key = "doc", Name = "Atomic", Version = 1, DefinitionJson = AtomicFlow, IsPublished = true, CreatedAt = Timestamp };
            db.Add(definition);
            db.Add(new ApprovalDefinitionManagement { Id = Guid.NewGuid(), Key = "doc", Name = "Atomic", LastVersion = 1, ActiveDefinitionId = definition.Id, UpdatedAt = Timestamp, UpdatedBy = "admin" });
            await db.SaveChangesAsync();
        }
        return provider;
    }
    private static async Task<T> AtomicRun<T>(ServiceProvider host, string actor, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = host.CreateAsyncScope(); var resolver = (AtomicResolver)scope.ServiceProvider.GetRequiredService<IApprovalContextResolver>();
        resolver.User = actor; return await action(scope.ServiceProvider);
    }
    private static Task<Guid> AtomicStart(ServiceProvider host, AtomicFaults faults, string key, string business = "one", int amount = 1) =>
        AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("atomic.start", business, key, new { amount }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "doc", BusinessType = AtomicBusiness.Type, BusinessKey = business }),
            async (fresh, ct) =>
            {
                Interlocked.Increment(ref faults.BusinessCalls); var db = fresh.GetRequiredService<AtomicContractContext>(); faults.Contexts.Add(db.ContextId.InstanceId);
                db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = amount });
                var instance = await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", AtomicBusiness.Type, business, "alice"), ct);
                faults.OutboxIds.Add(await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Where(m => m.InstanceId == instance.Id).Select(m => m.Id).SingleAsync(ct));
                return instance.Id;
            }));

    [Fact]
    public async Task AtomicCommands_TwentyConcurrentIdenticalRequestsAcrossTwoHosts_HaveOneCommittedResult()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        await using var secondHost = await AtomicHostAsync(faults, initializeDatabase: false);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 20).Select(async index => { await gate.Task; return await AtomicStart(index % 2 == 0 ? host : secondHost, faults, "same"); }).ToArray();
        gate.SetResult(); var results = await Task.WhenAll(requests); Assert.Single(results.Distinct()); Assert.Equal(1, faults.BusinessCalls);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").CountAsync());
            return true; });
    }

    [Fact]
    public async Task AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        async Task<(Guid? Id, ApprovalErrorCode? Error)> Start(string key)
        { try { return (await AtomicStart(host, faults, key), null); } catch (ApprovalException ex) { return (null, ex.Code); } }
        var outcomes = await Task.WhenAll(Start("first"), Start("second")); Assert.Single(outcomes, r => r.Id is not null);
        Assert.Equal(ApprovalErrorCode.Conflict, Assert.Single(outcomes, r => r.Error is not null).Error);
        var winner = outcomes[0].Id is not null ? "first" : "second";
        var changed = await Assert.ThrowsAsync<ApprovalException>(() => AtomicStart(host, faults, winner, amount: 2)); Assert.Equal(ApprovalErrorCode.Conflict, changed.Code);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); return true; });
    }

    [Fact]
    public async Task AtomicCommands_ReplayStillRequiresCurrentReadAuthorization()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var a = await AtomicStart(host, faults, "same"); Assert.Equal(a, await AtomicStart(host, faults, "same"));
        ((AtomicPermissions)host.GetRequiredService<IApprovalPermissionProvider>()).Rule = r => r.Permission != ApprovalPermissions.InstanceRead;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => AtomicStart(host, faults, "same")); Assert.Equal(ApprovalErrorCode.Forbidden, error.Code);
        Assert.Equal(1, faults.BusinessCalls);
    }

    [Fact]
    public async Task AtomicCommands_BusinessIdentityPreservesCaseAndTrailingWhitespaceAcrossProviders()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var ids = new List<Guid>();
        foreach (var business in new[] { "Order", "order", "order " }) ids.Add(await AtomicStart(host, faults, "same", business));
        Assert.Equal(3, ids.Distinct().Count());
        await AtomicRun(host, "alice", async sp =>
        {
            var q = sp.GetRequiredService<ApprovalQueryService>();
            foreach (var (business, index) in new[] { "Order", "order", "order " }.Select((key, index) => (key, index)))
                Assert.Equal(ids[index], Assert.Single(await q.GetByBusinessAsync(AtomicBusiness.Type, business)).Id);
            return true;
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AtomicCommands_CommitFaultIsVerified_AndRetriesUseFreshContexts(bool afterCommit)
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        if (afterCommit) faults.AfterCommit = 1; else faults.BeforeCommit = 1;
        var id = await AtomicStart(host, faults, "fault"); Assert.Equal(id, await AtomicStart(host, faults, "fault"));
        Assert.Equal(afterCommit ? 1 : 2, faults.BusinessCalls); Assert.Equal(afterCommit ? 1 : 2, faults.Contexts.Distinct().Count());
        Assert.Single(faults.OutboxIds.Distinct());
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); return true; });
    }

    [Fact]
    public async Task AtomicCommands_UnavailableCommitVerification_ReturnsUnknownAndOriginalKeyRecovers()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults); faults.AfterCommit = 1; faults.FailVerificationAfterCommit = true;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => AtomicStart(host, faults, "unknown")); Assert.Equal(ApprovalErrorCode.CommitOutcomeUnknown, error.Code);
        faults.BlockVerification = false;
        var recovered = await AtomicStart(host, faults, "unknown"); Assert.Equal(1, faults.BusinessCalls);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(recovered, await db.Set<ApprovalInstance>().Select(i => i.Id).SingleAsync()); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); return true; });
    }

    [Fact]
    public async Task AtomicCommands_OuterRollbackUndoesHostAndMultipleEngineCalls_AndKeyCanRetry()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        var fail = true;
        Task<bool> Execute() => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("atomic.batch", "batch", "outer-rollback", new { count = 2 }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "doc" }),
            async (fresh, ct) =>
            {
                var db = fresh.GetRequiredService<AtomicContractContext>();
                db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 30 });
                var engine = fresh.GetRequiredService<ApprovalEngine>();
                await engine.StartAsync(new("doc", AtomicBusiness.Type, "one", "alice"), ct);
                await engine.StartAsync(new("doc", AtomicBusiness.Type, "two", "alice"), ct);
                if (fail) throw new InvalidOperationException("outer rollback");
                return true;
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(Execute);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(0, await db.Set<AtomicHostRecord>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").CountAsync());
            return true;
        });
        fail = false; Assert.True(await Execute()); Assert.True(await Execute());
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").CountAsync());
            return true;
        });
    }

    [Fact]
    public async Task AtomicCommands_CallbackFailureRollsBackAndSameKeyCanRetry()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults); var id = await AtomicStart(host, faults, "start");
        var task = await AtomicRun(host, "bob", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>().Select(t => t.Id).SingleAsync());
        Task<bool> Approve() => AtomicRun(host, "bob", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("atomic.approve", task.ToString(), "approve", new { task }, ApprovalPermissions.TaskApprove, new() { TaskId = task }),
            async (fresh, ct) => { await fresh.GetRequiredService<ApprovalEngine>().ApproveAsync(task, "bob", ct: ct); return true; }));
        faults.FailCallback = true; await Assert.ThrowsAsync<InvalidOperationException>(Approve);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == id)).Status); Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); return true; });
        faults.FailCallback = false; Assert.True(await Approve());
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(2, await db.Set<AtomicHostRecord>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync()); return true; });
    }

    private static async Task DispatchAtomic(ServiceProvider host, CancellationToken ct = default)
    {
        await using var scope = await host.GetRequiredService<IApprovalScopeFactory>().CreateAsync();
        await scope.ServiceProvider.GetRequiredService<ApprovalOutboxService>().ProcessDueAsync(ct);
    }
    [Fact]
    public async Task AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults); await AtomicStart(host, faults, "start"); faults.AsyncSaveThenFail = true;
        await DispatchAtomic(host);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync());
            var message = await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").SingleAsync(); Assert.Equal(OutboxStatus.Pending, message.Status); Assert.Equal(1, message.Attempts); return true; });
        faults.AsyncSaveThenFail = false;
        var messageId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Select(m => m.Id).SingleAsync());
        await AtomicRun(host, "alice", async sp => { await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("outbox.retry", messageId.ToString(), "retry", new { messageId }, ApprovalPermissions.OutboxRetry, new() { OutboxId = messageId }), async (fresh, ct) => { await fresh.GetRequiredService<ApprovalOutboxService>().RetryAsync(messageId, ct); return true; }); return true; });
        await DispatchAtomic(host);
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); Assert.Equal(2, await db.Set<AtomicHostRecord>().CountAsync()); var message = await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").SingleAsync(); Assert.Equal(messageId, message.Id); Assert.Equal(OutboxStatus.Succeeded, message.Status); return true; });
    }

    [Fact]
    public async Task AtomicOutbox_RetryDuringDeliveryIsRejected_AndConcurrentDispatchDoesNotDuplicate()
    {
        var faults = new AtomicFaults { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var host = await AtomicHostAsync(faults); await AtomicStart(host, faults, "start");
        var old = DispatchAtomic(host); await faults.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var messageId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Select(m => m.Id).SingleAsync());
        try
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(() => AtomicRun(host, "alice", async sp => { await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("outbox.retry", messageId.ToString(), "retry", null, ApprovalPermissions.OutboxRetry, new() { OutboxId = messageId }), async (fresh, ct) => { await fresh.GetRequiredService<ApprovalOutboxService>().RetryAsync(messageId, ct); return true; }); return true; }));
            Assert.Equal(ApprovalErrorCode.Conflict, error.Code);
            await DispatchAtomic(host);
            Assert.Equal(1, faults.ActionCalls);
        }
        finally { faults.Release.TrySetResult(); await old; }
        await AtomicRun(host, "alice", async sp => { var db = sp.GetRequiredService<AtomicContractContext>(); var message = await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").SingleAsync();
            Assert.Equal(OutboxStatus.Succeeded, message.Status); Assert.Equal(1, message.Attempts); Assert.Equal(2, await db.Set<AtomicHostRecord>().CountAsync()); return true; });
        await DispatchAtomic(host);
        Assert.Equal(1, faults.ActionCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicOutbox_CanceledDeliveryRemainsDue_AndCanResumeWithSameId(bool ignoreCancellation)
    {
        var faults = new AtomicFaults { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreActionCancellation = ignoreCancellation };
        await using var host = await AtomicHostAsync(faults); await AtomicStart(host, faults, "start");
        var messageId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Select(m => m.Id).SingleAsync());
        using var stop = new CancellationTokenSource();
        var dispatch = DispatchAtomic(host, stop.Token);
        try
        {
            await faults.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            stop.Cancel();
            if (ignoreCancellation)
            {
                Assert.False(dispatch.IsCompleted);
                await DispatchAtomic(host);
                Assert.Equal(1, faults.ActionCalls);
            }
            faults.Release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
        }
        finally
        {
            stop.Cancel(); faults.Release.TrySetResult();
            try { await dispatch; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            var message = await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").SingleAsync();
            Assert.Equal(messageId, message.Id); Assert.Equal(OutboxStatus.Pending, message.Status);
            Assert.Equal(0, message.Attempts); Assert.Null(message.CompletedAt);
            Assert.True(message.NextAttemptAt <= DateTime.UtcNow);
            Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync());
            return true;
        });
        await DispatchAtomic(host);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            var message = await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").SingleAsync();
            Assert.Equal(messageId, message.Id); Assert.Equal(OutboxStatus.Succeeded, message.Status);
            Assert.Equal(1, message.Attempts); Assert.Equal(2, await db.Set<AtomicHostRecord>().CountAsync());
            return true;
        });
    }
}
