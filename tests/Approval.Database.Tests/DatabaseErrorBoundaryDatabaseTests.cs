using System.Data.Common;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class ReservationRace : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads, _collisions;
        public int Collisions => Volatile.Read(ref _collisions);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal)
                && Interlocked.Increment(ref _reads) is var count && count <= 2)
            {
                if (count == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return result;
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData data, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal) && IsUnique(data.Exception))
                Interlocked.Increment(ref _collisions);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Executor_SynchronizedOperationReservationCollisionReplaysWinnerWithoutDuplicateEffects()
    {
        var race = new ReservationRace(); var faults = new AtomicFaults();
        await using var host = await AtomicHostAsync(faults, configureServices: services =>
            services.ConfigureDbContext<AtomicContractContext>(o => o.AddInterceptors(race)));
        var results = await Task.WhenAll(AtomicStart(host, faults, "reservation-race"), AtomicStart(host, faults, "reservation-race"));
        Assert.Equal(results[0], results[1]); Assert.True(race.Collisions >= 1, "The loser must actually hit the operation unique constraint.");
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(results[0], Assert.Single(await db.Set<ApprovalInstance>().ToListAsync()).Id);
            Assert.Single(await db.Set<AtomicHostRecord>().ToListAsync()); Assert.Single(await db.Set<ApprovalOperation>().ToListAsync());
            Assert.Single(await db.Set<ApprovalTask>().ToListAsync()); Assert.Single(await db.Set<ApprovalBusinessSlot>().ToListAsync());
            Assert.Single(await db.Set<ApprovalLog>().Where(l => l.Action == ApprovalAction.Submit).ToListAsync());
            Assert.Single(await db.Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").ToListAsync());
            return true;
        });
    }

    [Theory]
    [InlineData("operation", "OperationConflict")]
    [InlineData("business-slot", "BusinessSlotConflict")]
    [InlineData("slot-instance", "BusinessSlotConflict")]
    [InlineData("definition-version", "GovernanceConflict")]
    [InlineData("management", "GovernanceConflict")]
    [InlineData("audit", "GovernanceConflict")]
    [InlineData("log-primary", "none")]
    public async Task RealProviderUniqueConstraints_ClassifyOnlyOwnedBusinessIndexes(string kind, string expected)
    {
        await ClearDataAsync(); await using var db = fixture.CreateStorage();
        var definition = Definition("doc"); var first = Instance(); var second = Instance();
        first.DefinitionId = second.DefinitionId = definition.Id;
        var existingId = Guid.NewGuid();
        object Row(bool duplicate) => kind switch
        {
            "operation" => new ApprovalOperation { Id = Guid.NewGuid(), LookupHash = "same", ActorId = "alice",
                Kind = "test", Scope = "doc", RequestKey = "key", RequestHash = "hash" },
            "business-slot" or "slot-instance" => new ApprovalBusinessSlot { Id = Guid.NewGuid(),
                LookupHash = kind == "slot-instance" && duplicate ? "other" : "same", BusinessType = "Order", BusinessKey = "one",
                InstanceId = kind == "slot-instance" || !duplicate ? first.Id : second.Id },
            "definition-version" => Definition("same-version"),
            "management" => new ApprovalDefinitionManagement { Id = Guid.NewGuid(), Key = "doc", Name = "Doc", UpdatedBy = "alice" },
            "audit" => new ApprovalDefinitionAudit { Id = Guid.NewGuid(), Key = "doc", Revision = 1, ActorId = "alice" },
            _ => new ApprovalLog { Id = existingId, InstanceId = first.Id, Sequence = duplicate ? 2 : 1, OperatorId = "alice" }
        };
        db.AddRange(definition, first, second, Row(false)); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await ErrorBoundaryCounts(db);
        db.Add(Row(true));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        foreach (var name in new[] { "OperationConflict", "BusinessSlotConflict", "GovernanceConflict", "CarbonCopyReadConflict" })
            Assert.Equal(name == expected, OwnedConflict(name, error, db));
        await using var verify = fixture.CreateStorage(); Assert.Equal(before, await ErrorBoundaryCounts(verify));
        await WriteConflictEvidence("owned-" + kind, new { provider = fixture.Provider, kind, expected,
            error = DescribeDatabaseError(error), entries = error.Entries.Select(e => e.Metadata.ClrType.Name) });
    }

    [Theory]
    [InlineData("unique")]
    [InlineData("sql")]
    public async Task Executor_RealProviderHostErrorPreservesOriginalAndRollsBack(string kind)
    {
        await using var host = await AtomicHostAsync(new AtomicFaults());
        var originalId = Guid.NewGuid();
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            db.Add(new AtomicHostRecord { Id = originalId, Value = 7 }); await db.SaveChangesAsync(); return true;
        });
        Exception? original = null;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => AtomicRun(host, "alice", sp =>
            sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("host.error", "doc", kind, new { kind }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "doc" }),
                async (fresh, ct) =>
                {
                    var db = fresh.GetRequiredService<AtomicContractContext>();
                    await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", "Atomic", "failed", "alice"), ct);
                    try
                    {
                        db.Add(new AtomicHostRecord { Id = kind == "unique" ? originalId : Guid.NewGuid(), Value = 9 });
                        await db.SaveChangesAsync(ct);
                        if (kind == "sql") await db.Database.ExecuteSqlRawAsync("SELECT MissingHostColumn FROM AtomicHostRecords", ct);
                    }
                    catch (Exception failure) when (failure is DbUpdateException or DbException)
                    { original = failure; throw; }
                    return true;
                })));
        Assert.NotNull(original); Assert.Same(original, error);
        if (kind == "unique") Assert.IsType<DbUpdateException>(error); else Assert.IsAssignableFrom<DbException>(error);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            var row = Assert.Single(await db.Set<AtomicHostRecord>().ToListAsync()); Assert.Equal(originalId, row.Id); Assert.Equal(7, row.Value);
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync()); Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync()); Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync()); Assert.Empty(await db.Set<ApprovalBusinessSlot>().ToListAsync());
            return true;
        });
    }

    private static bool OwnedConflict(string name, DbUpdateException error, DbContext db) =>
        (bool)typeof(ApprovalEngine).Assembly.GetType("Approval.Core.Persistence.ApprovalDatabaseErrors")!
            .GetMethod(name)!.Invoke(null, [error, db])!;

    private static async Task<int[]> ErrorBoundaryCounts(DbContext db) =>
        [await db.Set<ApprovalOperation>().CountAsync(), await db.Set<ApprovalBusinessSlot>().CountAsync(),
            await db.Set<ApprovalDefinition>().CountAsync(), await db.Set<ApprovalDefinitionManagement>().CountAsync(),
            await db.Set<ApprovalDefinitionAudit>().CountAsync(), await db.Set<ApprovalLog>().CountAsync()];
}
