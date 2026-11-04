using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Core.Tests;

public sealed class OperationTests
{
    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NextStart_DoesNotReuseTaskIdFromReadOrNoopTimeout(bool readReturnTargets)
    {
        await using var host = new TestHost();
        await host.PublishAsync("doc", Flow);
        await host.PublishAsync("auto", Flows.SingleNode("Any", "Role", "Nobody", emptyPolicy: "AutoApprove"));
        var original = await host.StartAsync("doc", "original", "alice");
        var taskId = Assert.Single(original.Tasks).Id;
        var nextId = await host.RunAsync(async sp =>
        {
            var engine = sp.GetRequiredService<ApprovalEngine>();
            if (readReturnTargets) await engine.GetReturnTargetsAsync(taskId, "bob");
            else Assert.False(await engine.HandleTimeoutAsync(taskId));
            return (await engine.StartAsync(new("auto", RecordingBusinessHandler.Type, "next", "alice"))).Id;
        });
        var next = await host.GetInstanceAsync(nextId);
        Assert.Equal(InstanceStatus.Approved, next.Status);
        Assert.Null(Assert.Single(next.Logs, l => l.Action == ApprovalAction.AutoApprove).TaskId);
        var unchanged = await host.GetInstanceAsync(original.Id);
        Assert.Equal(InstanceStatus.Running, unchanged.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(unchanged.Tasks).Status);
    }

    [Fact]
    public async Task HostUnitOfWork_StartThenApproveUsesFlushedTasksAndCommitsTogether()
    {
        var saves = new SaveCounter();
        await using var host = new TestHost(build: b => b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(saves)));
        await host.PublishAsync("doc", Flow);
        saves.Count = 0;
        var id = await host.RunAsync(sp => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var engine = sp.GetRequiredService<ApprovalEngine>();
            var instance = await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
            var taskId = Assert.Single(instance.Tasks).Id;
            Assert.Equal(1, saves.Count);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Equal(taskId, await db.Set<ApprovalTask>().AsNoTracking().Select(t => t.Id).SingleAsync(token));
            await engine.ApproveAsync(taskId, "bob", "approved in same transaction", token);
            Assert.Equal(2, saves.Count);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Empty(host.Events.TaskEvents);
            return instance.Id;
        }));
        Assert.Equal(3, saves.Count);
        var saved = await host.GetInstanceAsync(id);
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        var task = Assert.Single(saved.Tasks);
        Assert.Equal(ApprovalTaskStatus.Approved, task.Status);
        Assert.Equal("bob", task.HandledBy);
        Assert.Equal(task.Id, Assert.Single(saved.Logs, l => l.Action == ApprovalAction.Approve).TaskId);
        await host.RunAsync(async sp => Assert.Empty(await sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().ToListAsync()));
    }

    [Theory]
    [InlineData(199, true)]
    [InlineData(200, false)]
    public async Task AutoStepBudget_IsPerCommandAndOverflowRollsBack(int conditions, bool succeeds)
    {
        var nodes = new Dictionary<string, object>
        {
            ["start"] = new { type = "Start", next = "c0" },
            ["end"] = new { type = "End" }
        };
        for (var i = 0; i < conditions; i++)
            nodes["c" + i] = new { type = "Condition", branches = new[] { new { next = i + 1 < conditions ? "c" + (i + 1) : "end" } } };
        await using var host = new TestHost();
        await host.PublishAsync("long", JsonSerializer.Serialize(new { startNodeId = "start", nodes }));
        async Task Run() => await host.RunAsync(sp => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
        {
            var engine = sp.GetRequiredService<ApprovalEngine>();
            await engine.StartAsync(new("long", RecordingBusinessHandler.Type, "one", "alice"), token);
            await engine.StartAsync(new("long", RecordingBusinessHandler.Type, "two", "alice"), token);
        }));
        if (succeeds)
        {
            await Run();
            await host.RunAsync(async sp =>
            {
                var instances = await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().AsNoTracking().Include(i => i.Logs).ToListAsync();
                Assert.Equal(2, instances.Count);
                Assert.All(instances, i =>
                {
                    Assert.Equal(InstanceStatus.Approved, i.Status);
                    Assert.Equal(200, i.Logs.Count(l => l.Action == ApprovalAction.NodeEntered));
                });
            });
        }
        else
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(Run);
            Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
            await host.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<TestDbContext>();
                Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
                Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
                Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            });
        }
    }

    [Fact]
    public async Task BulkReassign_LogsEachOriginalTaskAndPreservesParallelState()
    {
        await using var host = new TestHost();
        host.Identity.Add("admin", "Admin");
        await host.PublishAsync("parallel", """
            {"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"fork"},
              "fork":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
              "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
              "b":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
              "join":{"type":"Join","next":"end"},"end":{"type":"End"}}}
            """);
        var instance = await host.StartAsync("parallel", "one", "alice");
        Assert.Equal(2, instance.Tasks.Count);
        var originalTasks = instance.Tasks.ToDictionary(t => t.NodeId, t => t.Id);
        var stateBefore = instance.StateJson;
        var result = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "admin", "handover"));
        Assert.Equal(2, result.Count);
        Assert.Empty(result.Skipped);
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(stateBefore, saved.StateJson);
        var logs = saved.Logs.Where(l => l.Action == ApprovalAction.Reassign).ToList();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal(originalTasks[l.NodeId!], l.TaskId));
        Assert.All(saved.Tasks.Where(t => originalTasks.Values.Contains(t.Id)), t => Assert.Equal(ApprovalTaskStatus.Transferred, t.Status));
        var replacements = saved.Tasks.Where(t => !originalTasks.Values.Contains(t.Id)).ToList();
        Assert.Equal(2, replacements.Count);
        Assert.All(replacements, t =>
        {
            Assert.Equal("carol", t.AssigneeValue);
            Assert.Equal(ApprovalTaskStatus.Pending, t.Status);
        });
    }

    [Fact]
    public async Task KeyedCoreStart_ReplaysOriginalVersionAndRejectsDifferentContent()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "one", "alice") { OperationId = "intent-1", FormData = new { amount = 1 } };
        var first = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        await host.PublishAsync("doc", Flow.Replace("Review", "New review"));
        var replay = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        Assert.Equal(first.Id, replay.Id); Assert.Equal(first.DefinitionId, replay.DefinitionId); Assert.Equal(1, replay.DefinitionVersion);
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { FormData = new { amount = 2 } })));
        Assert.Equal(ApprovalErrorCode.Conflict, failure.Code);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
        });
    }

    [Fact]
    public async Task HostUnitOfWork_CommitsMultipleEngineCallsAndNotifiesOnlyAfterCommit()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var unit = sp.GetRequiredService<IApprovalUnitOfWork>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            await unit.ExecuteAsync(async token =>
            {
                db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "host" });
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "two", "alice"), token);
                Assert.Empty(host.Events.TaskEvents);
            });
            await host.DispatchOutboxAsync();
            Assert.Equal(2, host.Events.TaskEvents.Count());
        });
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(1, await db.Set<TestRecord>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(2, await db.Set<ApprovalBusinessSlot>().CountAsync());
        });
    }

    [Fact]
    public async Task OuterRollback_RemovesBusinessWorkflowAndSlots_WithoutClearingHostTrackerOrNotifying()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var unit = sp.GetRequiredService<IApprovalUnitOfWork>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            var business = new TestRecord { Id = Guid.NewGuid(), Name = "host" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(async token =>
            {
                db.Add(business);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                await engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "two", "alice"), token);
                throw new InvalidOperationException("host rollback");
            }));
            Assert.Contains(db.ChangeTracker.Entries<TestRecord>(), e => ReferenceEquals(e.Entity, business));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        Assert.Empty(host.Events.TaskEvents);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(0, await db.Set<TestRecord>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
        });
    }

    [Fact]
    public async Task RawExternalTransaction_AndBulkInsideHostUnit_AreRejectedBeforeMutations()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var engine = sp.GetRequiredService<ApprovalEngine>();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice")));
                Assert.False(db.ChangeTracker.HasChanges()); await transaction.RollbackAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(
                token => engine.ReassignUserAsync("bob", "charlie", "admin", "handover", token)));
        });
        Assert.Empty(host.Events.TaskEvents);
    }

    [Fact]
    public async Task BusinessSlot_IsRetainedForRunningInstanceAndReleasedAtFinalCompletion()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        var first = await host.StartAsync("doc", "one", "alice");
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("doc", "one", "alice"));
        await host.ApproveMyTaskAsync("bob");
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<ApprovalBusinessSlot>().CountAsync()));
        var second = await host.StartAsync("doc", "one", "alice"); Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task ParallelBusinessMode_RemainsExplicitAndDoesNotCreateSlots()
    {
        await using var host = new TestHost(o => o.SingleRunningInstancePerBusiness = false); await host.PublishAsync("doc", Flow);
        await host.StartAsync("doc", "same", "alice"); await host.StartAsync("doc", "same", "alice");
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Equal(2, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync()); });
    }

    [Fact]
    public async Task StrictMode_RequiresClientKeyBeforeCreatingAnything()
    {
        await using var host = new TestHost(o => o.RequireOperationId = true); await host.PublishAsync("doc", Flow);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("doc", "one", "alice")); Assert.Equal(ApprovalErrorCode.OperationIdRequired, error.Code);
        var result = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice") { OperationId = "stable" }));
        Assert.Equal("one", result.BusinessKey);
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBack_AndAfterCommitCannotUndoSuccess()
    {
        await using var host = new TestHost(); await host.PublishAsync("doc", Flow);
        using var before = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RunAsync(sp =>
            sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
            {
                await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                before.Cancel(); token.ThrowIfCancellationRequested();
            }, before.Token)));
        Assert.Empty(host.Events.TaskEvents);
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
        using var after = new CancellationTokenSource();
        var id = await host.RunAsync(sp =>
        {
            var unit = sp.GetRequiredService<IApprovalUnitOfWork>();
            return unit.ExecuteAsync(async token =>
            {
                var result = await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", RecordingBusinessHandler.Type, "one", "alice"), token);
                unit.AfterCommit(() => { after.Cancel(); return Task.CompletedTask; });
                return result.Id;
            }, after.Token);
        });
        Assert.True(after.IsCancellationRequested);
        Assert.Equal(id, (await host.GetInstanceAsync(id)).Id);
        await host.DispatchOutboxAsync();
        Assert.Single(host.Events.TaskEvents);
    }

}
