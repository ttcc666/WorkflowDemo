using System.Data.Common;
using System.Net;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class OutboxRetryEndpointTests
{
    private const string OperationId = "outbox-retry-once";
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutboxRetry_CommitBoundaryControlsWakeupAndState(bool failCommit)
    {
        var barrier = new CommitBarrier(failCommit);
        var events = new FailingEvents();
        var clock = new ManualClock();
        await using var host = await ApiHost.StartAsync(
            build: b =>
            {
                b.Services.AddSingleton<IApprovalEventHandler>(events);
                b.Services.AddSingleton<TimeProvider>(clock);
                b.Services.ConfigureDbContext<ApiDbContext>(o => o.AddInterceptors(barrier));
            },
            workflow: o => { o.Outbox.EnableDispatcher = true; o.Outbox.MaxAttempts = 1; });
        await host.PublishAsync("retry", Flow);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new("retry", DocHandler.Type, "one", "alice")));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalOutboxService>().ProcessDueAsync());
        var messageId = await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>()
            .Set<ApprovalOutboxMessage>().Where(m => m.InstanceId == instance.Id).OrderBy(m => m.Id).Select(m => m.Id).FirstAsync());
        var original = await ReadStateAsync(host, messageId);
        Assert.Equal(OutboxStatus.Failed, original.Status);
        Assert.Equal(1, original.Attempts);
        Assert.NotNull(original.CompletedAt);
        Assert.Contains("delivery failed", original.LastError);
        var originalOperations = await OperationCountAsync(host);
        var originalDeliveries = events.Calls;
        var signal = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalOutboxSignal>()));
        while (await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)) { }
        clock.Now += TimeSpan.FromMinutes(1);

        async Task<HttpResponseMessage> RetryAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/approval/outbox/{messageId}/retry");
            request.Headers.Add(HeaderCurrentUser.Header, "admin");
            request.Headers.Add("Idempotency-Key", OperationId);
            return await host.Client.SendAsync(request);
        }

        barrier.Enabled = true;
        var pending = RetryAsync();
        bool signaledBeforeCommit;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            signaledBeforeCommit = await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);
        }
        finally
        {
            barrier.Release.TrySetResult();
            // Finish the HTTP request before disposing the shared connection, even if the barrier times out.
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
        }
        using var response = await pending;
        Assert.False(signaledBeforeCommit);

        if (failCommit)
        {
            await response.ProblemAsync(500);
            Assert.Equal(original, await ReadStateAsync(host, messageId));
            Assert.Equal(originalOperations, await OperationCountAsync(host));
            Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
            Assert.Equal(originalDeliveries, events.Calls);

            // A rolled-back attempt leaves the original key available for recovery.
            barrier.Enabled = false;
            using var recovered = await RetryAsync();
            Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
        }
        else Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var committed = await ReadStateAsync(host, messageId);
        Assert.Equal(OutboxStatus.Pending, committed.Status);
        Assert.Equal(0, committed.Attempts);
        Assert.Null(committed.CompletedAt);
        Assert.Equal(original.ConcurrencyStamp + 1, committed.ConcurrencyStamp);
        Assert.Equal(original.LastError, committed.LastError);
        Assert.Equal(clock.Now.UtcDateTime, committed.NextAttemptAt);
        Assert.Equal(originalOperations + 1, await OperationCountAsync(host));
        await host.RunAsync(async sp =>
        {
            var operation = await sp.GetRequiredService<ApiDbContext>().Set<ApprovalOperation>()
                .AsNoTracking().SingleAsync(o => o.RequestKey == OperationId);
            Assert.Equal("admin", operation.ActorId);
            return true;
        });
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(originalDeliveries, events.Calls);

        using var replay = await RetryAsync();
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(committed, await ReadStateAsync(host, messageId));
        Assert.Equal(originalOperations + 1, await OperationCountAsync(host));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(originalDeliveries, events.Calls);
    }

    private sealed record MessageState(OutboxStatus Status, int Attempts, long ConcurrencyStamp,
        DateTime NextAttemptAt, DateTime? CompletedAt, string? LastError);

    private static Task<MessageState> ReadStateAsync(ApiHost host, Guid id) => host.RunAsync(async sp =>
    {
        var message = await sp.GetRequiredService<ApiDbContext>().Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
        return new MessageState(message.Status, message.Attempts, message.ConcurrencyStamp,
            message.NextAttemptAt, message.CompletedAt, message.LastError);
    });

    private static Task<int> OperationCountAsync(ApiHost host) => host.RunAsync(sp =>
        sp.GetRequiredService<ApiDbContext>().Set<ApprovalOperation>().CountAsync());

    private sealed class FailingEvents : IApprovalEventHandler
    {
        public int Calls;
        public Task HandleAsync(ApprovalEvent value, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("delivery failed"); }
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CommitBarrier(bool failCommit) : DbTransactionInterceptor
    {
        public bool Enabled;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return result;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (failCommit) throw new InvalidOperationException("commit failed");
            return result;
        }
    }
}
