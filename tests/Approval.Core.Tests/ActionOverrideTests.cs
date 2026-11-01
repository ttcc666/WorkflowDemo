using Approval.Core.Actions;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class ActionOverrideTests
{
    [Theory]
    [InlineData(ActionMode.Sync)]
    [InlineData(ActionMode.Async)]
    public async Task Last_registered_action_overrides_built_in_in_both_modes(ActionMode mode)
    {
        var first = new RecordingHttpOverride("Http", "first");
        var last = new RecordingHttpOverride("hTtP", "last");
        await using var host = CreateHost(first, last);
        await host.PublishAsync("override", Flow(mode));

        await host.StartAsync("override", "B1", "alice", new { marker = "payload" });
        Assert.Equal(mode == ActionMode.Sync ? 1 : 0, last.Calls.Count);
        await host.DispatchOutboxAsync();

        var call = Assert.Single(last.Calls);
        Assert.Equal(mode == ActionMode.Async, call.IsAsync);
        Assert.Equal("payload", call.Get("value"));
        Assert.Empty(first.Calls);
        Assert.Empty(host.Http.Requests);
        var metadata = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Metadata));
        Assert.Equal("last", Assert.Single(metadata.Actions, a => a.Type.Equals("Http", StringComparison.OrdinalIgnoreCase)).DisplayName);
    }

    [Fact]
    public async Task Overridden_async_action_retries_with_the_same_idempotency_key()
    {
        var first = new RecordingHttpOverride("Http", "first");
        var last = new RecordingHttpOverride("HTTP", "last") { FailNext = true };
        await using var host = CreateHost(first, last);
        await host.PublishAsync("override", Flow(ActionMode.Async));
        await host.StartAsync("override", "B1", "alice", new { marker = "retry-payload" });
        Assert.Empty(last.Calls);
        var queued = Assert.Single(await host.OutboxAsync());

        await host.DispatchOutboxAsync();
        var failedAttempt = Assert.Single(await host.OutboxAsync());
        Assert.Equal(OutboxStatus.Pending, failedAttempt.Status);
        Assert.Equal(1, failedAttempt.Attempts);
        Assert.Contains("retry override", failedAttempt.LastError);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.DispatchOutboxAsync();

        var completed = Assert.Single(await host.OutboxAsync());
        Assert.Equal(OutboxStatus.Succeeded, completed.Status);
        Assert.Equal(2, completed.Attempts);
        Assert.Equal(2, last.Calls.Count);
        Assert.All(last.Calls, call =>
        {
            Assert.True(call.IsAsync);
            Assert.Equal(queued.Id, call.IdempotencyKey);
            Assert.Equal("retry-payload", call.Get("value"));
        });
        Assert.Empty(first.Calls);
        Assert.Empty(host.Http.Requests);
    }

    private static TestHost CreateHost(params RecordingHttpOverride[] handlers) => new(build: b =>
    {
        foreach (var handler in handlers) b.Services.AddSingleton<IApprovalActionHandler>(handler);
    });

    private static string Flow(ActionMode mode) => $$"""
    {
      "startNodeId": "start",
      "nodes": {
        "start": { "name": "提交", "type": "Start", "next": "action" },
        "action": { "name": "动作", "type": "Action", "next": "end", "actions": [
          { "type": "HTTP", "mode": "{{mode}}", "parameters": {
            "url": "https://api.example.com/action", "value": "{form.marker}" } }
        ] },
        "end": { "name": "结束", "type": "End" }
      }
    }
    """;

    private sealed class RecordingHttpOverride(string type, string name) : IApprovalActionHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new(type, name, [new ActionParameter("value", "值")]);
        public List<ApprovalActionContext> Calls { get; } = [];
        public bool FailNext { get; set; }

        public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
        {
            Calls.Add(context);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("retry override");
            }
            return Task.FromResult(ApprovalActionResult.Empty);
        }
    }
}
