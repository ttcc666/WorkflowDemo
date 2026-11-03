using Approval.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace Approval.Core.Tests;

public sealed class TenantBackfillValidationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        foreach (var table in new[] { "Definitions", "Instances", "Tasks", "Logs", "CarbonCopies", "Drafts", "Files", "Messages", "Outbox" })
        {
            var extra = table switch
            {
                "Definitions" => ", [Key] TEXT NOT NULL, Version INTEGER NOT NULL",
                "Instances" => ", DefinitionId TEXT, BusinessType TEXT, BusinessKey TEXT, Status TEXT",
                "Tasks" or "Logs" or "CarbonCopies" or "Messages" or "Outbox" => ", InstanceId TEXT",
                "Files" => ", InstanceId TEXT, DraftId TEXT", _ => ""
            };
            await Execute($"CREATE TABLE Approval{table} (Id TEXT PRIMARY KEY, TenantId TEXT NULL{extra})");
        }
    }
    public async Task DisposeAsync() => await _connection.DisposeAsync();
    private async Task Execute(string sql, params (string Key, object Value)[] parameters)
    {
        await using var command = _connection.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        await command.ExecuteNonQueryAsync();
    }
    private async Task Definition(Guid id, string key = "shared") => await Execute(
        "INSERT INTO ApprovalDefinitions (Id, [Key], Version) VALUES (@id, @key, 1)", ("@id", id), ("@key", key));

    [Fact]
    public async Task PotentialNormalizationCollisions_AreReportedWithoutRewritingKeys()
    {
        await Definition(Guid.NewGuid(), "Order"); await Definition(Guid.NewGuid(), " order ");
        var tool = new ApprovalTenantBackfill(_connection, "Sqlite");
        var preview = await tool.PreviewAsync([], "a");
        Assert.True(preview.Ready); Assert.Single(preview.PotentialKeyCollisions); Assert.Equal(0, preview.Changed);
        var applied = await tool.ApplyAsync([], "a");
        Assert.Equal(2, applied.Changed); Assert.Single(applied.PotentialKeyCollisions);
        await using var command = _connection.CreateCommand(); command.CommandText = "SELECT [Key], TenantId FROM ApprovalDefinitions ORDER BY [Key]";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(" order ", reader.GetString(0)); Assert.Equal("a", reader.GetString(1));
        Assert.True(await reader.ReadAsync()); Assert.Equal("Order", reader.GetString(0)); Assert.Equal("a", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task ParallelBusinessSetting_IsExplicitAndNeverTerminatesLegacyInstances()
    {
        var definition = Guid.NewGuid(); await Definition(definition);
        for (var index = 0; index < 2; index++)
            await Execute("INSERT INTO ApprovalInstances (Id, DefinitionId, BusinessType, BusinessKey, Status) VALUES (@id,@definition,'Doc','same','Running')", ("@id", Guid.NewGuid()), ("@definition", definition));
        var strict = await new ApprovalTenantBackfill(_connection, "Sqlite").ApplyAsync([], "a");
        Assert.False(strict.Ready); Assert.Equal(0, strict.Changed);
        Assert.Contains(strict.Problems, p => p.Contains("重复进行中", StringComparison.Ordinal));
        var applied = await new ApprovalTenantBackfill(_connection, "Sqlite", singleRunningInstancePerBusiness: false).ApplyAsync([], "a");
        Assert.True(applied.Ready); Assert.Equal(3, applied.Changed);
        await using var command = _connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM ApprovalInstances WHERE Status='Running' AND TenantId='a'";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task OrphanAndUnknownMappings_BlockTheEntireBackfill()
    {
        var definition = Guid.NewGuid(); var file = Guid.NewGuid(); await Definition(definition);
        await Execute("INSERT INTO ApprovalFiles (Id, InstanceId) VALUES (@id,@orphan)", ("@id", file), ("@orphan", Guid.NewGuid()));
        var report = await new ApprovalTenantBackfill(_connection, "Sqlite").ApplyAsync([
            new("Definitions", definition, "a"), new("Files", file, "a"), new("Drafts", Guid.NewGuid(), "a")]);
        Assert.False(report.Ready); Assert.Equal(0, report.Changed);
        Assert.Contains(report.Problems, p => p.Contains("孤立", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Contains("不存在", StringComparison.Ordinal));
        await using var command = _connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM ApprovalDefinitions WHERE TenantId IS NULL";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
}
