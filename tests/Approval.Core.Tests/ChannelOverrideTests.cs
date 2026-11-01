using Approval.Core.Domain;
using Approval.Core.Notifications;

namespace Approval.Core.Tests;

public sealed class ChannelOverrideTests
{
    [Fact]
    public async Task Last_registered_channel_overrides_earlier_channels_in_metadata_and_retries()
    {
        var first = new OverrideChannel("Test", "first");
        var last = new OverrideChannel("tEsT", "last") { FailNext = true };
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton<IApprovalChannel>(first);
            b.Services.AddSingleton<IApprovalChannel>(last);
        });
        await host.PublishAsync("channel-override", """
        {
          "startNodeId": "start",
          "nodes": {
            "start": { "type": "Start", "name": "提交", "next": "end" },
            "end": { "type": "End", "name": "完成", "notifications": [
              { "events": ["Completed"], "recipients": [{ "type": "Initiator", "value": "" }],
                "channels": ["TEST"], "content": "{form.marker}" }
            ] }
          }
        }
        """);
        var metadata = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Metadata));
        Assert.Equal("last", Assert.Single(metadata.Channels, c => c.Name.Equals("Test", StringComparison.OrdinalIgnoreCase)).DisplayName);

        var instance = await host.StartAsync("channel-override", "B1", "alice", new { marker = "payload" });
        var queued = Assert.Single(await host.OutboxAsync());
        await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync());
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("retry selected channel", failed.LastError);
        Assert.Single(last.Attempts);
        Assert.Empty(first.Attempts);
        Assert.Empty(host.Channel.Sent);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.DispatchOutboxAsync();
        var completed = Assert.Single(await host.OutboxAsync());
        Assert.Equal(OutboxStatus.Succeeded, completed.Status);
        Assert.Equal(2, completed.Attempts);
        Assert.Equal(2, last.Attempts.Count);
        Assert.All(last.Attempts, message =>
        {
            Assert.Equal(queued.Id, message.Id);
            Assert.Equal(instance.Id, message.InstanceId);
            Assert.Equal("payload", message.Content);
            Assert.Equal(new[] { "alice" }, message.Recipients);
        });
        Assert.Empty(first.Attempts);
        Assert.Empty(host.Channel.Sent);
    }

    private sealed class OverrideChannel(string type, string label) : IApprovalChannel
    {
        public string Name => type;
        public string DisplayName => label;
        public bool FailNext { get; set; }
        public List<ChannelMessage> Attempts { get; } = [];

        public Task SendAsync(ChannelMessage message, CancellationToken ct)
        {
            Attempts.Add(message);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("retry selected channel");
            }
            return Task.CompletedTask;
        }
    }
}
