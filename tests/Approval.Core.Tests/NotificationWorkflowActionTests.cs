using System.Text.Json.Nodes;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Theory]
    [InlineData("User", "\"bob\"")]
    [InlineData("Users", "[\"bob\",\"hana\"]")]
    public async Task Form_recipients_require_personnel_fields_and_preserve_existing_recipients(string type, string value)
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        host.Identity.Add("alice");
        host.Identity.Add("bob");
        host.Identity.Add("hana");
        await host.PublishAsync("notify", WithFormRecipient(type, "PERSON"));
        await host.StartAsync("notify", "PO-3", "alice", $$"""{"person":{{value}},"amount":7}""");
        Assert.Equal(type == "User" ? ["alice", "bob"] : new[] { "alice", "bob", "hana" },
            (await host.MessagesAsync()).Select(m => m.UserId).Order());
        Assert.Single(await host.NotificationOutboxAsync());
    }

    [Theory]
    [InlineData("Text", "person")]
    [InlineData("Number", "person")]
    [InlineData("Select", "person")]
    [InlineData("User", "missing")]
    [InlineData("User", "")]
    [InlineData(null, "person")]
    public async Task Invalid_form_recipient_in_mixed_rule_list_blocks_publication(string? type, string field)
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("bad-notify", WithFormRecipient(type, field)));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        Assert.Contains("User / Users", error.Message);
    }

    [Fact]
    public async Task Form_recipient_cannot_publish_without_the_definition_extension()
    {
        await using var host = new TestHost(build: b =>
        {
            b.Services.RemoveAll<Approval.Core.Abstractions.IApprovalDefinitionExtension>();
            b.AddNotificationActions();
        });
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("bad-notify", WithFormRecipient("User", "person")));
        Assert.Contains("User / Users", error.Message);
    }

    [Fact]
    public async Task Legacy_published_definition_with_text_recipient_rolls_back_start()
    {
        await using var host = new TestHost(build: b => b.AddNotificationActions());
        host.Identity.Add("alice");
        host.Identity.Add("bob");
        await host.PublishAsync("notify", Definition);
        await host.SeedStorageAsync(async db =>
        {
            (await db.Set<ApprovalDefinition>().SingleAsync()).DefinitionJson = WithFormRecipient("Text", "person");
            await db.SaveChangesAsync();
        });
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("notify", "PO-old", "alice", new { person = "bob", amount = 7 }));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        Assert.Empty(await host.MessagesAsync());
        Assert.Empty(await host.OutboxAsync());
        await host.RunAsync(async sp => Assert.False(await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().AnyAsync()));
    }

    private static string WithFormRecipient(string? type, string field)
    {
        var flow = JsonNode.Parse(Definition)!;
        var rules = flow["nodes"]!["notify"]!["actions"]![0]!["config"]!["recipients"]!.AsArray();
        rules.Add(new JsonObject { ["type"] = "FormField", ["value"] = field });
        if (type is not null)
            flow["form"] = new JsonObject { ["fields"] = new JsonArray(new JsonObject
            {
                ["key"] = "person", ["type"] = type, ["label"] = "Person",
                ["options"] = type == "Select" ? JsonNode.Parse("""[{"value":"bob","label":"Bob"}]""") : new JsonArray()
            }) };
        return flow.ToJsonString();
    }
}
