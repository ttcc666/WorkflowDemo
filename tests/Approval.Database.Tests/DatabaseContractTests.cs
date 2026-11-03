using System.Text.Json;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Approval.Database.Tests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public string Provider { get; } = Environment.GetEnvironmentVariable("APPROVAL_TEST_PROVIDER") ?? "Sqlite";
    public static string SqlitePath { get; } = Path.Combine(Path.GetTempPath(), $"approval-contract-{Guid.NewGuid():N}.db");

    public ContractDbContext Create(string tenant = "database-tests") => Provider switch
    {
        "MySql" => new MySqlContractContext(),
        "SqlServer" => new SqlServerContractContext(),
        "PostgreSql" => new PostgreSqlContractContext(),
        "Sqlite" => new SqliteContractContext(),
        _ => throw new InvalidOperationException($"Unsupported test provider: {Provider}")
    };

    public async Task InitializeAsync()
    {
        if (Provider != "Sqlite" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION")))
            throw new InvalidOperationException("Explicit database provider requires APPROVAL_TEST_CONNECTION; tests never skip unavailable databases.");
        await using var db = Create();
        if (Provider != "Sqlite" && !System.Text.RegularExpressions.Regex.IsMatch(
                db.Database.GetDbConnection().Database, "^approval_test_[a-zA-Z0-9_]+$"))
            throw new InvalidOperationException("Migration roundtrip tests require an approval_test_ disposable database.");
        if (Provider == "Sqlite" && Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION") is not null)
            throw new InvalidOperationException("SQLite contract tests use only their own temporary file.");
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        var report = Environment.GetEnvironmentVariable("APPROVAL_TEST_REPORT_DIRECTORY");
        if (report is not null)
        {
            Directory.CreateDirectory(report);
            await File.WriteAllTextAsync(Path.Combine(report, "migration.sql"), db.GetService<IMigrator>().GenerateScript());
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = Provider switch
            {
                "MySql" => "SELECT @@transaction_isolation",
                "SqlServer" => "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID",
                "PostgreSql" => "SHOW transaction_isolation",
                _ => "PRAGMA read_uncommitted"
            };
            var isolation = Convert.ToString(await command.ExecuteScalarAsync());
            await File.WriteAllTextAsync(Path.Combine(report, "database.json"), JsonSerializer.Serialize(new
            {
                provider = db.Database.ProviderName,
                providerVersion = System.Reflection.Assembly.Load(db.Database.ProviderName!).GetName().Version?.ToString(),
                serverVersion = db.Database.GetDbConnection().ServerVersion,
                defaultIsolation = isolation,
                migrations = await db.Database.GetAppliedMigrationsAsync()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public Task DisposeAsync()
    {
        // External databases are owned and removed by verify-databases.ps1, never by tests.
        if (Provider == "Sqlite" && Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION") is null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(SqlitePath + suffix);
        }
        return Task.CompletedTask;
    }
}

public sealed partial class DatabaseContractTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private async Task ClearDataAsync()
    {
        await using var db = fixture.Create(); await db.Database.OpenConnectionAsync();
        foreach (var table in new[] { "CarbonCopyReads", "MaintenanceAudits", "MaintenanceJobs", "RetentionHolds", "FileDeletions", "DefinitionAudits", "DefinitionManagement", "Operations", "BusinessSlots", "Files", "Archives", "Logs", "Tasks", "Messages", "Outbox", "CarbonCopies", "Instances", "Drafts", "Definitions" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            var name = "Approval" + table;
            command.CommandText = fixture.Provider == "MySql" ? $"DELETE FROM `{name}`" : $"DELETE FROM \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }
    }
    private static void AddInstance(DbContext db, ApprovalInstance instance)
    {
        var definition = Definition(); definition.Id = instance.DefinitionId;
        db.AddRange(definition, instance);
    }
    private static readonly DateTime Timestamp = new(2026, 11, 2, 3, 4, 5, DateTimeKind.Utc);
    private static ApprovalDefinition Definition(string? key = null) => new()
    {
        Id = Guid.NewGuid(), Key = key ?? Guid.NewGuid().ToString("N"), Version = 1,
        Name = "采购审批😀", DefinitionJson = "{\"说明\":\"" + new string('界', 8000) + "\"}",
        IsPublished = true, CreatedAt = Timestamp
    };

    private static ApprovalInstance Instance() => new()
    {
        Id = Guid.NewGuid(), DefinitionId = Guid.NewGuid(), DefinitionKey = "采购", DefinitionVersion = 1,
        BusinessType = "Order", BusinessKey = Guid.NewGuid().ToString("N"), InitiatorId = "用户甲",
        Status = InstanceStatus.Running, CreatedAt = Timestamp, FormDataJson = "{\"数量\":12}"
    };

    [Fact]
    public async Task Migrations_RollBackAndReapply_WithoutModelDrift()
    {
        await ClearDataAsync();
        await using var db = fixture.Create();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_InitialApproval", Assert.Single(migrations));
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Migration.InitialDatabase);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        await db.Database.MigrateAsync(); // A second upgrade must be a no-op.
        Assert.Equal(migrations, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(0, await db.Set<ApprovalDefinition>().CountAsync());
    }

    [Fact]
    public async Task Model_RoundTripsUnicodeLongJsonUtcNullableAndEnums()
    {
        var definition = Definition();
        var instance = Instance();
        instance.DefinitionId = definition.Id;
        await using (var write = fixture.Create())
        {
            write.AddRange(definition, instance);
            await write.SaveChangesAsync();
        }
        await using var read = fixture.Create();
        var actual = await read.Set<ApprovalDefinition>().SingleAsync(x => x.Id == definition.Id);
        Assert.Equal(definition.Key, actual.Key);
        Assert.Equal("采购审批😀", actual.Name);
        Assert.Equal(definition.DefinitionJson, actual.DefinitionJson);
        Assert.True(actual.IsPublished);
        Assert.Equal(Timestamp, actual.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, actual.CreatedAt.Kind);
        var loaded = await read.Set<ApprovalInstance>().SingleAsync(x => x.Id == instance.Id);
        Assert.Equal(InstanceStatus.Running, loaded.Status);
        Assert.Equal("{\"数量\":12}", loaded.FormDataJson);
        Assert.Null(loaded.CompletedAt);
    }

    [Fact]
    public async Task UniqueDefinitionVersion_RejectsDuplicateWithoutLosingWinner()
    {
        var winner = Definition();
        await using (var db = fixture.Create()) { db.Add(winner); await db.SaveChangesAsync(); }
        await using (var duplicate = fixture.Create())
        {
            duplicate.Add(Definition(winner.Key));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        await using var verify = fixture.Create();
        Assert.Equal(winner.Id, (await verify.Set<ApprovalDefinition>().SingleAsync(x => x.Key == winner.Key)).Id);
    }

    [Fact]
    public async Task ConcurrencyStamp_RejectsStaleWriter()
    {
        var instance = Instance();
        await using (var db = fixture.Create()) { AddInstance(db, instance); await db.SaveChangesAsync(); }
        await using var first = fixture.Create();
        await using var second = fixture.Create();
        var a = await first.Set<ApprovalInstance>().SingleAsync(x => x.Id == instance.Id);
        var b = await second.Set<ApprovalInstance>().SingleAsync(x => x.Id == instance.Id);
        a.Status = InstanceStatus.Approved; a.ConcurrencyStamp++;
        await first.SaveChangesAsync();
        b.Status = InstanceStatus.Rejected; b.ConcurrencyStamp++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = fixture.Create();
        var persisted = await verify.Set<ApprovalInstance>().SingleAsync(x => x.Id == instance.Id);
        Assert.Equal(InstanceStatus.Approved, persisted.Status);
        Assert.Equal(1, persisted.ConcurrencyStamp);
    }

    [Fact]
    public async Task Transaction_RollsBackApprovalAndOutboxTogether()
    {
        var instance = Instance();
        var messageId = Guid.NewGuid();
        await using (var db = fixture.Create())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            AddInstance(db, instance);
            db.Add(new ApprovalOutboxMessage
            {
                Id = messageId, InstanceId = instance.Id, Target = "webhook", PayloadJson = "{}",
                CreatedAt = Timestamp, NextAttemptAt = Timestamp, Status = OutboxStatus.Pending
            });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using var verify = fixture.Create();
        Assert.False(await verify.Set<ApprovalInstance>().AnyAsync(x => x.Id == instance.Id));
        Assert.False(await verify.Set<ApprovalOutboxMessage>().AnyAsync(x => x.Id == messageId));
    }

    [Fact]
    public async Task Savepoint_RollsBackInnerWriteAndCommitsOuterWrite()
    {
        var outer = Definition(); var inner = Definition();
        await using (var db = fixture.Create())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            Assert.True(transaction.SupportsSavepoints);
            db.Add(outer); await db.SaveChangesAsync();
            await transaction.CreateSavepointAsync("inner_operation");
            db.Add(inner); await db.SaveChangesAsync();
            await transaction.RollbackToSavepointAsync("inner_operation");
            await transaction.CommitAsync();
        }
        await using var verify = fixture.Create();
        Assert.True(await verify.Set<ApprovalDefinition>().AnyAsync(x => x.Id == outer.Id));
        Assert.False(await verify.Set<ApprovalDefinition>().AnyAsync(x => x.Id == inner.Id));
    }

    [Fact]
    public async Task Queries_TranslateIncludePagingContainsAndBulkUpdates()
    {
        var instance = Instance();
        instance.Tasks.Add(new ApprovalTask
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "审核",
            AssigneeKind = AssigneeKind.User, AssigneeValue = "用户乙", Status = ApprovalTaskStatus.Pending, CreatedAt = Timestamp
        });
        await using (var db = fixture.Create()) { AddInstance(db, instance); await db.SaveChangesAsync(); }
        await using var read = fixture.Create();
        Guid[] ids = [instance.Id];
        var query = read.Set<ApprovalInstance>().Where(x => ids.Contains(x.Id));
        Assert.Equal(1, await query.CountAsync());
        var loaded = Assert.Single(await query.Include(x => x.Tasks).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(0).Take(10).ToListAsync());
        Assert.Equal("用户乙", Assert.Single(loaded.Tasks).AssigneeValue);
        Assert.Equal(1, await query.ExecuteUpdateAsync(x => x.SetProperty(i => i.Title, "批量更新")));
        await using var verify = fixture.Create();
        Assert.Equal("批量更新", await verify.Set<ApprovalInstance>().Where(x => x.Id == instance.Id).Select(x => x.Title).SingleAsync());
        Assert.Equal(1, await verify.Set<ApprovalTask>().Where(x => x.InstanceId == instance.Id).ExecuteDeleteAsync());
        Assert.Empty(await verify.Set<ApprovalTask>().Where(x => x.InstanceId == instance.Id).ToListAsync());
    }
}
