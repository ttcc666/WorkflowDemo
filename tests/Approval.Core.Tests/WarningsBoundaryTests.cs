using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Tests;

public sealed class WarningsBoundaryTests
{
    private static FlowDefinition Flow() => FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));

    private sealed class Extension(Func<IReadOnlyList<string>> warnings) : IApprovalDefinitionExtension
    {
        public IReadOnlySet<string>? UserFields(FlowDefinition definition) => null;
        public void Validate(FlowDefinition definition, List<string> errors) { }
        public IReadOnlyList<string> Warnings(FlowDefinition definition) => warnings();
        public string NormalizeSimulationData(FlowDefinition definition, string dataJson) => dataJson;
    }

    private static TestHost Host(Func<IServiceProvider, IReadOnlyList<string>> warnings) => new(build: b =>
        b.Services.AddScoped<IApprovalDefinitionExtension>(sp => new Extension(() => warnings(sp))));

    [Fact]
    public async Task Warnings_CanReadHostDataAndReturnWarningsWithoutWriting()
    {
        await using var host = Host(sp => [sp.GetRequiredService<TestDbContext>().Set<TestRecord>().Single().Name]);
        host.Identity.Add("admin", "Admin");
        await host.RunAsAsync("admin", async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "host warning" });
            await db.SaveChangesAsync();
            var definitions = sp.GetRequiredService<ApprovalDefinitionService>();
            var flow = Flow();
            Assert.Empty(await definitions.ValidateAsync(flow));
            Assert.Equal(["host warning"], definitions.Warnings(flow));
            return true;
        });
        await AssertDatabaseAsync(host, "host warning");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warnings_RejectHostSaveChangesAndPreserveDatabase(bool asyncSave)
    {
        await using var host = Host(sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "unexpected" });
            if (asyncSave) db.SaveChangesAsync().GetAwaiter().GetResult();
            else db.SaveChanges();
            return [];
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsAsync("alice", sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Warnings(Flow()))));
        Assert.Contains("不得自行 SaveChanges", error.Message);
        await AssertDatabaseAsync(host);
    }

    [Fact]
    public async Task Warnings_RejectCommitAndLeaveOuterTransactionAvailableForRollback()
    {
        await using var host = Host(sp =>
        {
            sp.GetRequiredService<TestDbContext>().Database.CurrentTransaction!.Commit();
            return [];
        });
        await host.RunAsAsync("alice", async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Add(new TestRecord { Name = "uncommitted" });
            await db.SaveChangesAsync();

            var definitions = sp.GetRequiredService<ApprovalDefinitionService>();
            var error = Assert.Throws<InvalidOperationException>(() => definitions.Warnings(Flow()));
            Assert.Contains("不得自行提交数据库事务", error.Message);
            Assert.Same(transaction, db.Database.CurrentTransaction);
            Assert.Equal("uncommitted", (await db.Set<TestRecord>().SingleAsync()).Name);
            await transaction.RollbackAsync();
            return true;
        });
        await AssertDatabaseAsync(host);
    }

    [Fact]
    public async Task Warnings_RejectKeyedCommandReentryBeforeInvokingDelegate()
    {
        var entered = false;
        await using var host = Host(sp =>
        {
            sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("warnings.test", "doc", "unexpected", null, ApprovalPermissions.DefinitionPublish),
                async (fresh, ct) =>
                {
                    entered = true;
                    fresh.GetRequiredService<TestDbContext>().Add(new TestRecord { Name = "unexpected" });
                    return await fresh.GetRequiredService<ApprovalDefinitionService>().PublishAsync("unexpected", "Unexpected", Flow(), ct);
                }).GetAwaiter().GetResult();
            return [];
        });
        host.Identity.Add("admin", "Admin");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsAsync("admin", sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Warnings(Flow()))));
        Assert.Contains("扩展不能重入审批写操作", error.Message);
        Assert.False(entered);
        await AssertDatabaseAsync(host);
    }

    [Fact]
    public async Task Warnings_RejectBulkUpdateOfProtectedDefinition()
    {
        await using var host = Host(sp =>
        {
            sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinition>()
                .ExecuteUpdate(set => set.SetProperty(d => d.Name, "unexpected"));
            return [];
        });
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsAsync("alice", sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Warnings(Flow()))));
        Assert.Contains("审批表禁止通过批量操作或原生 SQL 写入", error.Message);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.NotEqual("unexpected", (await db.Set<ApprovalDefinition>().SingleAsync()).Name);
        });
    }

    private static Task AssertDatabaseAsync(TestHost host, params string[] records) => host.RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<TestDbContext>();
        Assert.Equal(records, await db.Set<TestRecord>().Select(r => r.Name).ToArrayAsync());
        Assert.Empty(await db.Set<ApprovalDefinition>().ToListAsync());
        Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
        Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync());
        Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
        Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
    });
}
