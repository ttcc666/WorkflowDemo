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
    public async Task Warnings_ReadOnlyCallbackAllowsKeyedHostAndApprovalWrites()
    {
        await using var host = Host(_ => ["read-only warning"]);
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));

        var instanceId = await StartAndValidateAsync(host);

        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal("outer business", (await db.Set<TestRecord>().SingleAsync()).Name);
            Assert.Equal(instanceId, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
            Assert.Equal(instanceId, (await db.Set<ApprovalTask>().SingleAsync()).InstanceId);
            Assert.Equal(instanceId, (await db.Set<ApprovalOperation>().SingleAsync()).InstanceId);
            Assert.Contains(await db.Set<ApprovalLog>().ToListAsync(), l => l.Action == ApprovalAction.Submit);
            Assert.NotEmpty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warnings_CaughtSaveRejectionRollsBackEntireKeyedCommand(bool asyncSave)
    {
        InvalidOperationException? rejectedSave = null;
        await using var host = Host(sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "extension business" });
            try
            {
                if (asyncSave) db.SaveChangesAsync().GetAwaiter().GetResult();
                else db.SaveChanges();
            }
            catch (InvalidOperationException error)
            {
                rejectedSave = error;
            }
            return ["read-only warning"];
        });
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var delegateReturned = false;

        var commandError = await Record.ExceptionAsync(() => StartAndValidateAsync(host, () => delegateReturned = true));

        Assert.NotNull(rejectedSave);
        Assert.Contains("不得自行 SaveChanges", rejectedSave.Message);
        Assert.True(delegateReturned);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Empty(await db.Set<TestRecord>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalBusinessSlot>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            Assert.Single(await db.Set<ApprovalDefinition>().ToListAsync());
        });
        Assert.Contains("当前作用域", Assert.IsType<InvalidOperationException>(commandError).Message);
    }

    private static Task<Guid> StartAndValidateAsync(TestHost host, Action? beforeReturn = null) => host.RunAsAsync("alice", sp =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("warnings.start", "doc", "start-and-validate", null, ApprovalPermissions.InstanceStart,
                new() { DefinitionKey = "doc" }),
            async (fresh, ct) =>
            {
                var instance = await fresh.GetRequiredService<ApprovalEngine>()
                    .StartAsync(new("doc", RecordingBusinessHandler.Type, "warnings", "alice"), ct);
                var db = fresh.GetRequiredService<TestDbContext>();
                db.Add(new TestRecord { Name = "outer business" });
                await db.SaveChangesAsync(ct);
                Assert.Equal(["read-only warning"], fresh.GetRequiredService<ApprovalDefinitionService>().Warnings(Flow()));
                beforeReturn?.Invoke();
                return instance.Id;
            }));

    [Fact]
    public async Task Warnings_RejectOpeningTransactionAndInvalidateCallerScope()
    {
        await using var host = Host(sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "unexpected" });
            using var transaction = db.Database.BeginTransaction();
            return [];
        });
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var definitions = sp.GetRequiredService<ApprovalDefinitionService>();
            var error = Assert.Throws<InvalidOperationException>(() => definitions.Warnings(Flow()));
            Assert.Contains("不得自行开启数据库事务", error.Message);
            Assert.Null(db.Database.CurrentTransaction);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
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
            var failedCommit = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
            Assert.Contains("当前作用域已作废", failedCommit.Message);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warnings_RejectDirectEfWritesToHostTable(bool rawSql)
    {
        await using var host = Host(sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            if (rawSql) db.Database.ExecuteSqlRaw("DELETE FROM TestRecord");
            else db.Set<TestRecord>().ExecuteUpdate(set => set.SetProperty(r => r.Name, "unexpected"));
            return [];
        });
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "existing" });
            await db.SaveChangesAsync();
            var definitions = sp.GetRequiredService<ApprovalDefinitionService>();
            var error = Assert.Throws<InvalidOperationException>(() => definitions.Warnings(Flow()));
            Assert.Contains("只读扩展不得执行", error.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        await AssertDatabaseAsync(host, "existing");
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
