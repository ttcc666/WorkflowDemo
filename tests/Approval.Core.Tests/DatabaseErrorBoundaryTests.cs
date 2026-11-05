using System.Data.Common;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Tests;

public sealed class DatabaseErrorBoundaryTests
{
    [Theory]
    [InlineData("unique", 2067)]
    [InlineData("foreign-key", 787)]
    [InlineData("sql", 1)]
    public async Task Executor_HostDatabaseErrorPreservesOriginalAndRollsBackWholeCommand(string kind, int sqliteCode)
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "duplicate" });
            await db.SaveChangesAsync();
        });
        Exception? original = null;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.RunAsAsync("alice", sp =>
            sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("host.failure", "doc", kind, new { kind }, ApprovalPermissions.InstanceStart, new() { DefinitionKey = "doc" }),
                async (fresh, ct) =>
                {
                    var db = fresh.GetRequiredService<TestDbContext>();
                    await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, kind, "alice"), ct);
                    db.Add(new TestRecord { Id = Guid.NewGuid(), Name = kind == "unique" ? "duplicate" : "new",
                        ParentId = kind == "foreign-key" ? Guid.NewGuid() : null });
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        if (kind == "sql") await db.Database.ExecuteSqlRawAsync("SELECT MissingHostColumn FROM TestRecord", ct);
                    }
                    catch (Exception failure) when (failure is DbUpdateException or DbException)
                    { original = failure; throw; }
                    return true;
                })));
        Assert.NotNull(original);
        Assert.Same(original, error);
        var provider = kind == "sql" ? Assert.IsType<SqliteException>(error)
            : Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(error).InnerException);
        Assert.Equal(sqliteCode, provider.SqliteExtendedErrorCode);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal("duplicate", Assert.Single(await db.Set<TestRecord>().ToListAsync()).Name);
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalBusinessSlot>().ToListAsync());
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("publish")]
    public async Task Governance_HostUniqueFailureRemainsDatabaseErrorAndDoesNotSaveGovernance(string operation)
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "duplicate" }); await db.SaveChangesAsync();
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.RunAsAsync("admin", sp =>
            sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("governance.failure", "doc", operation, null, operation == "draft" ? ApprovalPermissions.DefinitionDesign : ApprovalPermissions.DefinitionPublish),
                async (fresh, ct) =>
                {
                    fresh.GetRequiredService<TestDbContext>().Add(new TestRecord { Id = Guid.NewGuid(), Name = "duplicate" });
                    var definitions = fresh.GetRequiredService<ApprovalDefinitionService>();
                    var flow = Flows.SingleNode("Any", "User", "bob");
                    if (operation == "draft") await definitions.SaveDesignDraftAsync("doc", "Doc", flow, 0, ct: ct);
                    else await definitions.PublishAsync("doc", "Doc", flow, ct);
                    return true;
                })));
        Assert.Equal(2067, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Single(await db.Set<TestRecord>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalDefinition>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalDefinitionManagement>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalDefinitionAudit>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
        });
    }
}
