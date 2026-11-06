using System.Text.Json.Nodes;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class NotificationWorkflowActionTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"notify"},
          "notify":{"type":"Action","next":"end","actions":[
            {"id":"finance","type":"Notification","config":{
              "recipients":[{"type":"Initiator","value":""},{"type":"User","value":"alice"},{"type":"Role","value":"Finance"}],
              "channels":["InApp","Test","Test"],"title":"采购 {{business.key}}","content":"金额 {{form.amount}}"}}
          ]},"end":{"type":"End"}
        }}
        """;

    [Fact]
    public async Task Explicit_notification_uses_transactional_inbox_and_existing_event_route()
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        host.Identity.Add("alice");
        host.Identity.Add("bob", "Finance");
        await host.PublishAsync("notify", Definition);
        var instance = await host.StartAsync("notify", "PO-1", "alice", new { amount = 120 });
        var inbox = (await host.MessagesAsync()).OrderBy(m => m.UserId).ToArray();
        Assert.Equal(["alice", "bob"], inbox.Select(m => m.UserId));
        Assert.All(inbox, message =>
        {
            Assert.Equal(instance.Id, message.InstanceId);
            Assert.Equal(NotifyEvent.Action, message.Event);
            Assert.Equal("采购 PO-1", message.Title);
            Assert.Equal("金额 120", message.Content);
        });
        var outbox = Assert.Single(await host.NotificationOutboxAsync());
        Assert.DoesNotContain("采购 PO-1", outbox.Description);
        Assert.Empty(host.Channel.Sent);
        await host.DispatchOutboxAsync();
        Assert.Equal(outbox.Id, Assert.Single(host.Channel.Sent).Id);
    }

    [Fact]
    public async Task Explicit_notification_rolls_back_both_routes_if_approval_completion_fails()
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        host.Identity.Add("alice");
        host.Handler.FailOnApproved = true;
        await host.PublishAsync("notify", Definition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync("notify", "PO-2", "alice", new { amount = 12 }));
        Assert.Empty(await host.MessagesAsync());
        Assert.Empty(await host.OutboxAsync());
    }

    [Fact]
    public async Task Unknown_notification_channel_cannot_publish()
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["notify"]!["actions"]![0]!["config"]!["channels"]![0] = "Unregistered";
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("bad-notify", flow.ToJsonString()));
    }
}
