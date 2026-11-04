using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class ApprovalLogConflictTests
{
    private sealed class Business(TestDbContext db) : IApprovalBusinessHandler
    {
        public string BusinessType => RecordingBusinessHandler.Type;
        public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) => WriteAsync(ct);
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => WriteAsync(ct);
        private Task WriteAsync(CancellationToken ct)
        {
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString("N") });
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Engine_LogSequenceCollisionReturnsConflictAndRollsBackWholeScope(string decision)
    {
        await using var host = new TestHost(build: b => b.AddBusinessHandler<Business>());
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var instance = await host.StartAsync("doc", "one", "alice");
        // 显式历史存储夹具让日志领先计数器，独立触发SQLite的日志唯一约束分支。
        // 两审批操作的真实竞争另外由跨Provider同步测试覆盖，不替代ConcurrencyStamp测试。
        await host.SeedStorageAsync(async db =>
        {
            db.Add(new ApprovalLog { Id = Guid.NewGuid(), InstanceId = instance.Id, Sequence = instance.LogSequence + 1,
                Action = ApprovalAction.Remind, OperatorId = "existing", CreatedAt = host.Clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync();
        });
        var before = await host.GetInstanceAsync(instance.Id);
        var outbox = (await host.OutboxAsync()).Select(m => m.Id).Order().ToArray();
        ApprovalException failure;
        await using (var scope = host.NewScope())
        {
            var sp = scope.ServiceProvider;
            var engine = sp.GetRequiredService<ApprovalEngine>();
            failure = await Assert.ThrowsAsync<ApprovalException>(() => decision == "approve"
                ? engine.ApproveAsync(Assert.Single(instance.Tasks).Id, "bob")
                : engine.RejectAsync(Assert.Single(instance.Tasks).Id, "bob", "reject"));
            Assert.Equal(ApprovalErrorCode.Conflict, failure.Code);
            var update = Assert.IsType<DbUpdateException>(failure.InnerException);
            var database = Assert.IsType<SqliteException>(update.InnerException);
            Assert.Equal(2067, database.SqliteExtendedErrorCode);
            Assert.True(ApprovalDatabaseErrors.LogSequence(update, sp.GetRequiredService<TestDbContext>(), instance.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(_ => Task.CompletedTask));
            await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<TestDbContext>().SaveChangesAsync());
        }
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(before.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status);
        Assert.Equal(before.Logs.Select(l => l.Id), saved.Logs.Select(l => l.Id));
        Assert.Equal(outbox, (await host.OutboxAsync()).Select(m => m.Id).Order());
        Assert.Empty(await host.MessagesAsync());
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync()));

        host.Identity.Add("admin", "Admin");
        var propagated = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("admin", sp =>
            sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync<int>(
                new("existing.conflict", "one", "conflict", null, ApprovalPermissions.DefinitionReadSensitive),
                (_, _) => Task.FromException<int>(failure))));
        Assert.Same(failure, propagated);
    }

    [Fact]
    public async Task Engine_UnrelatedBusinessUniqueViolationRemainsDatabaseException()
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var instance = await host.StartAsync("doc", "one", "alice");
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "duplicate" }); await db.SaveChangesAsync();
        });
        var beforeOutbox = (await host.OutboxAsync()).Count;
        await using (var scope = host.NewScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "duplicate" });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => sp.GetRequiredService<ApprovalEngine>()
                .ApproveAsync(Assert.Single(instance.Tasks).Id, "bob"));
            Assert.Equal(2067, Assert.IsType<SqliteException>(failure.InnerException).SqliteExtendedErrorCode);
            Assert.False(ApprovalDatabaseErrors.LogSequence(failure, db, instance.Id));
        }
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(instance.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status);
        Assert.Equal(instance.Logs.Count, saved.Logs.Count);
        Assert.Equal(beforeOutbox, (await host.OutboxAsync()).Count);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync()));
    }
}
