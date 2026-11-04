using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class ReadOnlyConcurrencyTests
{
    private static string Flow(string? action = "Remind")
    {
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
        if (action is not null) flow.Nodes["review"].Timeout = new TimeoutPolicy
        { Minutes = 10, Action = Enum.Parse<TimeoutAction>(action), RepeatMinutes = 5, MaxReminders = 2 };
        return flow.ToJson();
    }

    [Theory]
    [InlineData("bob", true)]
    [InlineData("other", false)]
    public async Task CanHandle_DoesNotChangeStampWhenHostSaves(string user, bool expected)
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow());
        var instance = await host.StartAsync("doc", "one", "alice");
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(expected, await sp.GetRequiredService<ApprovalEngine>().CanHandleAsync(Assert.Single(instance.Tasks).Id, user));
            var tracked = Assert.Single(db.ChangeTracker.Entries<ApprovalInstance>()).Entity;
            Assert.Equal(instance.ConcurrencyStamp, tracked.ConcurrencyStamp);
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "host-save" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(instance.ConcurrencyStamp, (await host.GetInstanceAsync(instance.Id)).ConcurrencyStamp);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync()));
    }

    [Fact]
    public async Task ReturnTargets_DoesNotChangeStampWhenHostSaves()
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow());
        var instance = await host.StartAsync("doc", "one", "alice");
        await host.RunAsync(async sp =>
        {
            var targets = await sp.GetRequiredService<ApprovalEngine>().GetReturnTargetsAsync(Assert.Single(instance.Tasks).Id, "bob");
            Assert.True(Assert.Single(targets).IsInitiator);
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "host-save" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(instance.ConcurrencyStamp, (await host.GetInstanceAsync(instance.Id)).ConcurrencyStamp);
    }

    [Theory]
    [InlineData("not-due")]
    [InlineData("handled")]
    [InlineData("not-running")]
    [InlineData("waiting")]
    [InlineData("unscheduled")]
    public async Task TimeoutNoop_DoesNotChangeStampWhenHostUnitCommits(string state)
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow());
        var instance = await host.StartAsync("doc", "one", "alice");
        var taskId = Assert.Single(instance.Tasks).Id;
        if (state == "handled") await host.ApproveAsync(taskId, "bob");
        if (state is "not-running" or "waiting" or "unscheduled")
            await host.SeedStorageAsync(async db =>
            {
                var stored = await db.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
                if (state == "not-running") stored.Status = InstanceStatus.Approved;
                if (state == "waiting") Assert.Single(stored.Tasks).Status = ApprovalTaskStatus.Waiting;
                if (state == "unscheduled") Assert.Single(stored.Tasks).NextRemindAt = null;
                await db.SaveChangesAsync();
            });
        if (state != "not-due") host.Clock.Advance(TimeSpan.FromMinutes(10));
        var before = await host.GetInstanceAsync(instance.Id);
        var outbox = (await host.OutboxAsync()).Count;
        await host.RunAsync(sp => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
        {
            Assert.False(await sp.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(taskId, ct));
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(before.ConcurrencyStamp, Assert.Single(db.ChangeTracker.Entries<ApprovalInstance>()).Entity.ConcurrencyStamp);
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "host-save" });
        }));
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(before.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.Equal(before.Status, saved.Status);
        Assert.Equal(Assert.Single(before.Tasks).NextRemindAt, Assert.Single(saved.Tasks).NextRemindAt);
        Assert.Equal(before.Logs.Count, saved.Logs.Count);
        Assert.Equal(outbox, (await host.OutboxAsync()).Count);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync()));
    }

    [Theory]
    [InlineData("Remind", InstanceStatus.Running, ApprovalAction.Remind)]
    [InlineData("AutoApprove", InstanceStatus.Approved, ApprovalAction.TimeoutApprove)]
    [InlineData("AutoReject", InstanceStatus.Rejected, ApprovalAction.TimeoutReject)]
    public async Task DueTimeout_ChangesStampAndPersistsDecision(string action, InstanceStatus status, ApprovalAction logAction)
    {
        await using var host = new TestHost(o => o.EnableAutomaticTimeoutDecisions = true);
        await host.PublishAsync("doc", Flow(action));
        var instance = await host.StartAsync("doc", "one", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(Assert.Single(instance.Tasks).Id)));
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Equal(status, saved.Status);
        Assert.Equal(1, Assert.Single(saved.Tasks).RemindCount);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, Assert.Single(saved.Tasks).LastRemindedAt);
        Assert.Contains(saved.Logs, l => l.Action == logAction);
        Assert.Contains(await host.OutboxAsync(), m => m.InstanceId == instance.Id && m.Description == logAction.ToString());
        if (action == "Remind")
        {
            Assert.Equal(host.Clock.GetUtcNow().UtcDateTime.AddMinutes(5), Assert.Single(saved.Tasks).NextRemindAt);
            Assert.Contains(await host.MessagesAsync(), m => m.UserId == "bob");
        }
        else Assert.Null(Assert.Single(saved.Tasks).NextRemindAt);
    }

    [Fact]
    public async Task TimeoutWithoutPolicy_ClearsLegacyReminderAndChangesStamp()
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow(null));
        var instance = await host.StartAsync("doc", "one", "alice");
        await host.SeedStorageAsync(async db =>
        {
            var task = await db.Set<ApprovalTask>().SingleAsync();
            task.NextRemindAt = host.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        });
        var outbox = (await host.OutboxAsync()).Count;
        Assert.False(await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(Assert.Single(instance.Tasks).Id)));
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Null(Assert.Single(saved.Tasks).NextRemindAt);
        Assert.Equal(0, Assert.Single(saved.Tasks).RemindCount);
        Assert.Equal(instance.Logs.Count, saved.Logs.Count);
        Assert.Equal(outbox, (await host.OutboxAsync()).Count);
    }

    [Fact]
    public async Task RealApproval_RejectsStaleTrackedInstanceAndPreservesWinner()
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow());
        var instance = await host.StartAsync("doc", "one", "alice");
        await using var stale = host.NewScope();
        await stale.ServiceProvider.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
        await host.ApproveAsync(Assert.Single(instance.Tasks).Id, "bob");
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => stale.ServiceProvider.GetRequiredService<ApprovalEngine>()
            .ApproveAsync(Assert.Single(instance.Tasks).Id, "bob"));
        Assert.Equal(ApprovalErrorCode.Conflict, failure.Code);
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Approve);
    }
}
