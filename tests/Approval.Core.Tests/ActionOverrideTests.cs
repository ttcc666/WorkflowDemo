using Approval.Core.Abstractions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class ActionOverrideTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private sealed class First : IApprovalLifecycleHandler
    {
        public Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct) => throw new InvalidOperationException("replaced handler must not run");
    }
    private sealed class Last : IApprovalLifecycleHandler
    {
        public Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct) =>
            Task.FromResult(new ApprovalStartPreparation("last", instance.FormDataJson));
    }
    [Fact]
    public async Task Last_registered_lifecycle_replaces_previous_handler()
    {
        await using var host = new TestHost(build: b => b.AddLifecycleHandler<First>().AddLifecycleHandler<Last>());
        await host.PublishAsync("f", Flow);
        var instance = await host.StartAsync("f", "B1", "alice");
        Assert.Equal("last", (await host.GetInstanceAsync(instance.Id)).Title);
    }
    private sealed class Events : IApprovalEventHandler
    {
        public List<ApprovalEvent> Calls { get; } = [];
        public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
        {
            if (approvalEvent.EventType != "Submit") return Task.CompletedTask;
            Calls.Add(approvalEvent);
            if (Calls.Count == 1) throw new InvalidOperationException("retry event");
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Reliable_event_retries_with_same_id_and_business_identity()
    {
        var events = new Events();
        await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalEventHandler>(events));
        await host.PublishAsync("f", Flow); await host.StartAsync("f", "B1", "alice");
        var queued = Assert.Single(await host.OutboxAsync(), m => m.Description == "Submit");
        await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync(), m => m.Id == queued.Id);
        Assert.Equal(OutboxStatus.Pending, failed.Status); Assert.Equal(1, failed.Attempts);
        Assert.Contains("retry event", failed.LastError);
        host.Clock.Advance(TimeSpan.FromHours(1)); await host.DispatchOutboxAsync();
        var completed = Assert.Single(await host.OutboxAsync(), m => m.Id == queued.Id);
        Assert.Equal(OutboxStatus.Succeeded, completed.Status); Assert.Equal(2, completed.Attempts);
        Assert.Equal(2, events.Calls.Count);
        Assert.All(events.Calls, call => { Assert.Equal(queued.Id, call.Id); Assert.Equal("B1", call.BusinessKey); });
    }
}
