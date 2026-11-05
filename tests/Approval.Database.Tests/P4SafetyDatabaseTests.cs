using System.Data.Common;
using System.Text.Json;
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
    [Theory]
    [InlineData("retry")]
    [InlineData("reassign")]
    [InlineData("bulk")]
    [InlineData("terminate")]
    public async Task ManagementCommands_ConcurrentSameKeyCommitOneResult(string command)
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults, configure: o => o.Outbox.EnableDispatcher = false);
        var instanceId = await AtomicStart(host, faults, "start");
        var taskId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>().Select(t => t.Id).SingleAsync());
        var messageId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Select(m => m.Id).SingleAsync());
        var permission = command switch { "retry" => ApprovalPermissions.OutboxRetry, "reassign" => ApprovalPermissions.TaskReassign, _ => ApprovalPermissions.InstanceTerminate };
        var resource = command switch { "retry" => new ApprovalResource { OutboxId = messageId }, "reassign" => new() { TaskId = taskId }, _ => new() { InstanceId = instanceId } };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        async Task<string> Execute()
        {
            if (Interlocked.Increment(ref entered) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return await AtomicRun(host, "alice", async sp =>
            {
                if (command == "bulk") return JsonSerializer.Serialize(await sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "carol", "alice", "handover", "management"));
                return await sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new("p4." + command, instanceId.ToString(), "management",
                    new { taskId, instanceId, messageId, reason = "handover" }, permission, resource), async (fresh, ct) =>
                {
                    if (command == "retry") await fresh.GetRequiredService<ApprovalOutboxService>().RetryAsync(messageId, ct);
                    else if (command == "reassign") await fresh.GetRequiredService<ApprovalEngine>().ReassignAsync(taskId, "alice", "carol", "handover", ct);
                    else await fresh.GetRequiredService<ApprovalEngine>().TerminateAsync(instanceId, "alice", "handover", ct);
                    return "committed";
                });
            });
        }
        var results = await Task.WhenAll(Execute(), Execute()); Assert.Single(results.Distinct());
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(command == "bulk" ? 4 : 2, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(command is "reassign" or "bulk" ? 2 : 1, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(command is "reassign" or "bulk" ? 1 : 0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Reassign));
            Assert.Equal(command == "terminate" ? 1 : 0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Terminate));
            Assert.Equal(command == "terminate" ? 1 : 0, await db.Set<ApprovalOutboxMessage>().CountAsync(m => m.Description == "Completed"));
            Assert.Equal(command is "reassign" or "bulk" ? 1 : 0, await db.Set<ApprovalOutboxMessage>().CountAsync(m => m.Description == "Reassign"));
            Assert.Equal(command is "reassign" or "bulk" ? 2 : 1, await db.Set<ApprovalOutboxMessage>().CountAsync(m => m.Description == "TaskCreated"));
            Assert.Equal(command == "terminate" ? 1 : 0, await db.Set<ApprovalOutboxMessage>().CountAsync(m => m.Description == "Terminate"));
            Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); // no duplicated host-start side effect
            var message = await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == messageId);
            Assert.Equal(command == "retry" ? 1L : 0L, message.ConcurrencyStamp); Assert.Equal(0, message.Attempts);
            return true;
        });
    }

    private sealed class RetryConflict : SaveChangesInterceptor
    {
        public bool Enabled, Fired;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && !Fired && eventData.Context!.ChangeTracker.Entries<ApprovalOutboxMessage>().Any(e => e.State == EntityState.Modified))
            { Fired = true; throw new DbUpdateConcurrencyException("controlled stale retry"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RetryWinner(RetryConflict conflict, Func<Task> commitWinner) : DbTransactionInterceptor
    {
        private bool _committed;
        public override async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (conflict.Fired && !_committed) { _committed = true; await commitWinner(); }
        }
    }
    [Fact]
    public async Task OutboxRetry_OptimisticConflictDoesNotOverwriteWinnerOrCommitOperation()
    {
        var faults = new AtomicFaults(); var conflict = new RetryConflict(); Guid messageId = default;
        var completion = Timestamp.AddHours(1);
        var winner = new RetryWinner(conflict, async () =>
        {
            await using var db = fixture.CreateStorage(); var message = await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == messageId);
            message.Status = OutboxStatus.Succeeded; message.Attempts = 4; message.CompletedAt = completion; message.ConcurrencyStamp = 9;
            await db.SaveChangesAsync();
        });
        await using var host = await AtomicHostAsync(faults, configureServices: s => s.ConfigureDbContext<AtomicContractContext>(o => o.AddInterceptors(conflict, winner)));
        await AtomicStart(host, faults, "start");
        messageId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalOutboxMessage>().Where(m => m.Description == "Submit").Select(m => m.Id).SingleAsync());
        conflict.Enabled = true;
        var error = await Assert.ThrowsAsync<ApprovalException>(() => AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("outbox.retry", messageId.ToString(), "retry", new { messageId }, ApprovalPermissions.OutboxRetry, new() { OutboxId = messageId }),
            async (fresh, ct) => { await fresh.GetRequiredService<ApprovalOutboxService>().RetryAsync(messageId, ct); return true; })));
        Assert.Equal(ApprovalErrorCode.Conflict, error.Code); Assert.True(conflict.Fired);
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>(); var message = await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == messageId);
            Assert.Equal((OutboxStatus.Succeeded, 4, 9L, completion), (message.Status, message.Attempts, message.ConcurrencyStamp, message.CompletedAt));
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync()); return true;
        });
        Assert.Equal(0, faults.ActionCalls);
    }
}
