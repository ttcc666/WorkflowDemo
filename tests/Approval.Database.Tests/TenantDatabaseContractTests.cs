using System.Data.Common;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task TenantIsolation_ModelReuseForeignIdsBulkWritesAndConstraints()
    {
        var ids = new Dictionary<string, Guid>();
        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            await using var db = fixture.Create(tenant);
            var definition = Definition("same-definition"); var instance = Instance(); instance.DefinitionId = definition.Id;
            instance.BusinessKey = "same-business";
            var draft = new ApprovalDraft { Id = Guid.NewGuid(), DefinitionKey = definition.Key, UserId = "same-user" };
            instance.Tasks.Add(new() { Id = Guid.NewGuid(), InstanceId = instance.Id, AssigneeValue = "same-user", NodeId = "review", NodeName = "Review", Status = ApprovalTaskStatus.Pending });
            instance.Logs.Add(new() { Id = Guid.NewGuid(), InstanceId = instance.Id, OperatorId = "same-user", Sequence = 1 });
            db.AddRange(definition, instance, draft,
                new ApprovalCarbonCopy { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "cc", AssigneeValue = "same-user" },
                new ApprovalFile { Id = Guid.NewGuid(), InstanceId = instance.Id, DraftId = draft.Id, FileName = "private.txt", ContentType = "text/plain", Sha256 = "00", UploadedBy = "same-user", StorageKey = "key" },
                new ApprovalMessage { Id = Guid.NewGuid(), InstanceId = instance.Id, UserId = "same-user", Title = "private", Content = tenant },
                new ApprovalOutboxMessage { Id = Guid.NewGuid(), InstanceId = instance.Id, Target = "Test", PayloadJson = "{}", NextAttemptAt = Timestamp });
            await db.SaveChangesAsync(); ids[tenant] = instance.Id;
        }
        await using var a = fixture.Create("tenant-a"); await using var b = fixture.Create("tenant-b");
        Assert.Same(a.Model, b.Model);
        foreach (var db in new[] { a, b })
        {
            Assert.Equal(1, await db.Set<ApprovalDefinition>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalCarbonCopy>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalDraft>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalFile>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalMessage>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOutboxMessage>().CountAsync());
        }
        Assert.Null(await a.Set<ApprovalInstance>().FindAsync(ids["tenant-b"]));
        Assert.Equal(ids["tenant-a"], (await a.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync()).Id);
        Assert.Equal(1, await a.Set<ApprovalMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true)));
        Assert.Equal(1, await a.Set<ApprovalCarbonCopy>().ExecuteDeleteAsync());
        await Assert.ThrowsAsync<ApprovalException>(() => a.Set<ApprovalMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.TenantId, "tenant-b")));
        Assert.False((await b.Set<ApprovalMessage>().SingleAsync()).IsRead);
        Assert.Equal(1, await b.Set<ApprovalCarbonCopy>().CountAsync());
        await using (var bad = fixture.Create("tenant-a"))
        {
            var invalid = Instance(); invalid.DefinitionId = (await b.Set<ApprovalDefinition>().SingleAsync()).Id;
            bad.Add(invalid);
            await Assert.ThrowsAsync<DbUpdateException>(() => bad.SaveChangesAsync());
        }
        await using (var forged = fixture.Create("tenant-a"))
        {
            var instance = Instance(); instance.Id = ids["tenant-b"]; instance.TenantId = "tenant-a";
            forged.Remove(instance);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => forged.SaveChangesAsync());
        }
        Assert.Equal(1, await b.Set<ApprovalInstance>().CountAsync());
    }

    private static readonly string LegacyFlow = """
        {"startNodeId":"start","nodes":{"start":{"name":"Start","type":"Start","next":"review"},
        "review":{"name":"Review","type":"Approval","mode":"Any","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"name":"End","type":"End"}}}
        """;

    [Fact]
    public async Task LegacyUpgrade_RequiresMappingPreservesIdsAndContinuesPendingApproval()
    {
        await ClearDataAsync();
        await using var maintenance = fixture.Create();
        var migrator = maintenance.GetService<IMigrator>();
        var migrations = maintenance.Database.GetMigrations().ToArray();
        await migrator.MigrateAsync(migrations[0]);
        var definition = Guid.NewGuid(); var instance = Guid.NewGuid(); var task = Guid.NewGuid();
        await maintenance.Database.OpenConnectionAsync();
        var connection = maintenance.Database.GetDbConnection();
        string Q(string name) => fixture.Provider == "MySql" ? $"`{name}`" : $"\"{name}\"";
        async Task Insert(string table, Dictionary<string, object?> values)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO {Q(table)} ({string.Join(",", values.Keys.Select(Q))}) VALUES ({string.Join(",", values.Keys.Select((_, i) => "@p" + i))})";
            var index = 0;
            foreach (var value in values.Values)
            {
                var parameter = command.CreateParameter(); parameter.ParameterName = "@p" + index++; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }
        await Insert("ApprovalDefinitions", new() { ["Id"] = definition, ["Key"] = "legacy", ["Version"] = 1, ["Name"] = "Legacy", ["DefinitionJson"] = LegacyFlow, ["IsPublished"] = true, ["CreatedAt"] = Timestamp });
        await Insert("ApprovalInstances", new()
        {
            ["Id"] = instance, ["DefinitionId"] = definition, ["DefinitionKey"] = "legacy", ["DefinitionVersion"] = 1,
            ["BusinessType"] = "Doc", ["BusinessKey"] = "legacy-business", ["InitiatorId"] = "alice", ["Status"] = "Running",
            ["CurrentNodeId"] = "review", ["Round"] = 0, ["FormDataJson"] = "{}", ["CreatedAt"] = Timestamp, ["ConcurrencyStamp"] = 0L, ["LogSequence"] = 0
        });
        await Insert("ApprovalTasks", new()
        {
            ["Id"] = task, ["InstanceId"] = instance, ["NodeId"] = "review", ["NodeName"] = "Review", ["AssigneeKind"] = "User", ["AssigneeValue"] = "bob",
            ["Status"] = "Pending", ["Kind"] = "Approval", ["Round"] = 0, ["Order"] = 0, ["CreatedAt"] = Timestamp, ["RemindCount"] = 0
        });
        await migrator.MigrateAsync(migrations[1]);
        var backfill = new ApprovalTenantBackfill(connection, fixture.Provider);
        var missing = await backfill.PreviewAsync([]);
        Assert.False(missing.Ready); Assert.Equal(3, missing.Problems.Count);
        await Assert.ThrowsAnyAsync<DbException>(() => migrator.MigrateAsync());
        var crossTenant = await backfill.ApplyAsync([new("Definitions", definition, "a"), new("Instances", instance, "b"), new("Tasks", task, "b")]);
        Assert.False(crossTenant.Ready); Assert.Equal(0, crossTenant.Changed);
        Assert.Contains(crossTenant.Problems, p => p.Contains("跨租户", StringComparison.Ordinal));
        var preview = await backfill.PreviewAsync([], "legacy"); Assert.True(preview.Ready); Assert.Equal(0, preview.Changed);
        var applied = await backfill.ApplyAsync([], "legacy"); Assert.True(applied.Ready); Assert.Equal(3, applied.Changed);
        Assert.Equal(0, (await backfill.ApplyAsync([], "legacy")).Changed);
        Assert.False((await backfill.ApplyAsync([], "another-tenant")).Ready);
        await migrator.MigrateAsync();
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped<IApprovalIdentityProvider, ContractIdentity>();
        services.AddScoped<IApprovalContextResolver, LegacyActor>();
        void Configure(ApprovalOptions o) { o.FixedTenantId = "legacy"; o.EnableBackgroundServices = false; }
        switch (fixture.Provider)
        {
            case "MySql": services.AddScoped<MySqlContractContext>(); services.AddApprovalWorkflow<MySqlContractContext>(Configure); break;
            case "SqlServer": services.AddScoped<SqlServerContractContext>(); services.AddApprovalWorkflow<SqlServerContractContext>(Configure); break;
            case "PostgreSql": services.AddScoped<PostgreSqlContractContext>(); services.AddApprovalWorkflow<PostgreSqlContractContext>(Configure); break;
            default: services.AddScoped<SqliteContractContext>(); services.AddApprovalWorkflow<SqliteContractContext>(Configure); break;
        }
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().ApproveAsync(task, "bob");
        await using var verify = fixture.Create("legacy");
        var result = await verify.Set<ApprovalInstance>().SingleAsync();
        Assert.Equal(instance, result.Id); Assert.Equal(definition, result.DefinitionId); Assert.Equal(InstanceStatus.Approved, result.Status);
        Assert.Equal(ApprovalTaskStatus.Approved, (await verify.Set<ApprovalTask>().SingleAsync(t => t.Id == task)).Status);
        await using var other = fixture.Create("tenant-a"); Assert.Empty(await other.Set<ApprovalInstance>().ToListAsync());
    }

    private sealed class LegacyActor : IApprovalContextResolver
    { public ApprovalExecutionIdentity Resolve() => new("legacy", "bob"); }

    private sealed class ContractIdentity : IApprovalIdentityProvider
    {
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
