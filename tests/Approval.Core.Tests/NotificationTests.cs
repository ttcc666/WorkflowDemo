using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Approval.Core.Templates;

namespace Approval.Core.Tests;

/// <summary>节点通知、站内信、Outbox 可靠投递与重试。</summary>
public sealed class NotificationTests : IAsyncLifetime
{
    private readonly TestHost _host = new(o => o.InstanceUrlFormat = "https://app.example.com/approvals/{0}");

    /// <summary>审核节点：到达时通知审批人（站内信 + 测试渠道），同意时通知申请人；结束节点：通知申请人。</summary>
    private const string NotifyFlow = """
    {
      "startNodeId": "start",
      "notifications": [
        { "events": ["Rejected"], "recipients": [{ "type": "Initiator", "value": "" }], "channels": ["InApp"],
          "content": "{标题} 被 {操作人} 驳回：{意见}" }
      ],
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any",
                    "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "cc",
                    "notifications": [
                      { "events": ["Enter"], "recipients": [{ "type": "Assignees", "value": "" }], "channels": ["InApp", "Test"],
                        "title": "待审批", "content": "【待审批】{标题}，申请人 {申请人}，金额 {form.amount}" },
                      { "events": ["Approved"], "recipients": [{ "type": "Initiator", "value": "" }], "channels": ["InApp"] }
                    ] },
        "cc":     { "name": "抄送", "type": "Cc", "approvers": [{ "type": "User", "value": "watcher" }], "next": "end" },
        "end":    { "name": "结束", "type": "End",
                    "notifications": [
                      { "events": ["Completed"], "recipients": [{ "type": "Initiator", "value": "" }, { "type": "Approvers", "value": "" }],
                        "channels": ["InApp"], "content": "{标题}{结果}" }
                    ] }
      }
    }
    """;

    public async Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        await _host.PublishAsync("notify", NotifyFlow);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Enter_rule_writes_in_app_messages_for_role_members_and_outbox_for_external_channel()
    {
        var instance = await _host.StartAsync("notify", "B1", "alice", new { amount = 1200 });

        var messages = await _host.MessagesAsync();
        Assert.Equal(["r1", "r2"], messages.Select(m => m.UserId).Order());
        Assert.All(messages, m =>
        {
            Assert.Equal("待审批", m.Title);
            Assert.Equal("【待审批】证书 B1，申请人 alice，金额 1200", m.Content);
            Assert.Equal(NotifyEvent.Enter, m.Event);
            Assert.Equal(instance.Id, m.InstanceId);
        });

        // 外部渠道只写 Outbox，提交后由后台投递
        var outbox = Assert.Single(await _host.NotificationOutboxAsync());
        Assert.Equal(OutboxKind.Event, outbox.Kind);
        Assert.Equal("Host", outbox.Target);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.Empty(_host.Channel.Sent);

        await _host.DispatchOutboxAsync();

        var sent = Assert.Single(_host.Channel.Sent);
        Assert.Equal(outbox.Id, sent.Id); // 消息 ID 即幂等键
        Assert.Equal(["r1", "r2"], sent.Recipients.Order());
        Assert.Equal($"https://app.example.com/approvals/{instance.Id}", sent.Link);
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await _host.NotificationOutboxAsync()).Status);
    }

    [Fact]
    public async Task Approve_cc_and_completion_notify_the_configured_recipients()
    {
        await _host.StartAsync("notify", "B1", "alice", new { amount = 1 });
        await _host.ApproveMyTaskAsync("r2", "可以");

        // 同一次操作内写入的消息时间相同，按事件取
        var alice = (await _host.MessagesAsync("alice")).ToDictionary(m => m.Event);
        Assert.Equal([NotifyEvent.Approved, NotifyEvent.Completed], alice.Keys.Order());
        Assert.Equal("【已同意】证书 B1，r2 同意了 审核", alice[NotifyEvent.Approved].Content);
        Assert.Equal("证书 B1已通过", alice[NotifyEvent.Completed].Content);

        // 已审批过的人收到结束通知；抄送节点未配置规则时默认发站内信
        Assert.Contains(await _host.MessagesAsync("r2"), m => m.Event == NotifyEvent.Completed);
        var cc = Assert.Single(await _host.MessagesAsync("watcher"));
        Assert.Equal("【抄送】证书 B1，申请人 alice", cc.Content);
    }

    [Fact]
    public async Task Flow_level_rule_applies_to_rejection()
    {
        await _host.StartAsync("notify", "B1", "alice");
        var todo = Assert.Single(await _host.TodoAsync("r1"));
        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().RejectAsync(todo.TaskId, "r1", "材料不全"));

        var message = Assert.Single(await _host.MessagesAsync("alice"));
        Assert.Equal(NotifyEvent.Rejected, message.Event);
        Assert.Equal("证书 B1 被 r1 驳回：材料不全", message.Content);
    }

    [Fact]
    public async Task Messages_and_outbox_roll_back_with_the_approval()
    {
        await _host.StartAsync("notify", "B1", "alice");
        var before = (await _host.MessagesAsync()).Count;
        var outboxBefore = (await _host.NotificationOutboxAsync()).Count;

        _host.Handler.FailOnApproved = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.ApproveMyTaskAsync("r1"));

        Assert.Equal(before, (await _host.MessagesAsync()).Count);
        Assert.Equal(outboxBefore, (await _host.NotificationOutboxAsync()).Count);
    }

    [Fact]
    public async Task Failed_delivery_is_retried_with_exponential_backoff()
    {
        _host.Channel.FailTimes = 2;
        await _host.StartAsync("notify", "B1", "alice");

        await _host.DispatchOutboxAsync();
        var message = Assert.Single(await _host.NotificationOutboxAsync());
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("渠道暂时不可用", message.LastError);
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), message.NextAttemptAt);

        // 未到重试时间不投递
        await _host.DispatchOutboxAsync();
        Assert.Equal(1, _host.Channel.Attempts);

        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        await _host.DispatchOutboxAsync();
        message = Assert.Single(await _host.NotificationOutboxAsync());
        Assert.Equal(2, message.Attempts);
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddSeconds(60), message.NextAttemptAt);

        _host.Clock.Advance(TimeSpan.FromSeconds(60));
        await _host.DispatchOutboxAsync();
        message = Assert.Single(await _host.NotificationOutboxAsync());
        Assert.Equal(OutboxStatus.Succeeded, message.Status);
        Assert.Single(_host.Channel.Sent);
    }

    [Fact]
    public async Task Delivery_is_marked_failed_after_max_attempts_and_can_be_retried_manually()
    {
        await using var host = new TestHost(o => o.Outbox.MaxAttempts = 2);
        host.Identity.Add("r1", "Reviewer");
        host.Channel.FailTimes = 2;
        await host.PublishAsync("notify", NotifyFlow);
        await host.StartAsync("notify", "B1", "alice");

        await host.DispatchOutboxAsync();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.DispatchOutboxAsync();
        var message = Assert.Single(await host.NotificationOutboxAsync());
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(2, message.Attempts);

        await host.RunAsync(sp => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(message.Id));
        await host.DispatchOutboxAsync();
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await host.NotificationOutboxAsync()).Status);
    }

    [Fact]
    public async Task Message_service_marks_messages_read_only_for_their_owner()
    {
        await _host.StartAsync("notify", "B1", "alice");
        var r1Message = Assert.Single(await _host.MessagesAsync("r1"));

        await _host.RunAsync(async sp =>
        {
            var service = sp.GetRequiredService<ApprovalMessageService>();
            Assert.Equal(1, await service.UnreadCountAsync("r1"));
            await Assert.ThrowsAsync<ApprovalException>(() => service.MarkReadAsync("r2", r1Message.Id));
        });
        await _host.RunAsync(async sp =>
        {
            var service = sp.GetRequiredService<ApprovalMessageService>();
            await service.MarkReadAsync("r1", r1Message.Id);
            Assert.Equal(0, await service.UnreadCountAsync("r1"));
            Assert.Equal(1, await service.MarkAllReadAsync("r2"));
        });
    }

    [Fact]
    public async Task Published_definition_with_unavailable_host_channel_rolls_back_instead_of_dropping_notification()
    {
        // 模拟发布后宿主撤下渠道适配器；不能在发起时静默漏发通知。
        await _host.SeedStorageAsync(async db =>
        {
            var definition = new ApprovalDefinition
            {
                Id = Guid.NewGuid(), Key = "host-notify", Name = "Host notification", Version = 1, IsPublished = true,
                DefinitionJson = NotifyFlow.Replace("\"Test\"", "\"RemovedHostChannel\""), CreatedAt = _host.Clock.GetUtcNow().UtcDateTime
            };
            db.Add(definition);
            db.Add(new ApprovalDefinitionManagement { Id = Guid.NewGuid(), Key = definition.Key, Name = definition.Name, ActiveDefinitionId = definition.Id, LastVersion = 1, UpdatedBy = "admin" });
            await db.SaveChangesAsync();
        });
        var error = await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("host-notify", "one", "alice"));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        Assert.Contains("RemovedHostChannel", error.Message);
        Assert.Empty(await _host.MessagesAsync());
        Assert.Empty(await _host.NotificationOutboxAsync());
        Assert.Empty(await _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().ToListAsync()));
        Assert.Empty(await _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalTask>().ToListAsync()));
        Assert.Empty(await _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalLog>().ToListAsync()));
    }

    [Fact]
    public async Task Unknown_outbox_kind_fails_instead_of_being_acknowledged()
    {
        await _host.SeedStorageAsync(async db =>
        {
            db.Add(new ApprovalOutboxMessage
            {
                Id = Guid.NewGuid(), Kind = (OutboxKind)999, Target = "Unknown", PayloadJson = "{}",
                Status = OutboxStatus.Pending, MaxAttempts = 1,
                CreatedAt = _host.Clock.GetUtcNow().UtcDateTime, NextAttemptAt = _host.Clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
        });
        await _host.DispatchOutboxAsync();
        var message = Assert.Single(await _host.OutboxAsync());
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("不支持的 Outbox 消息类型", message.LastError);
        Assert.Empty(_host.Channel.Sent);
    }

    [Fact]
    public async Task Validator_reports_unknown_channel_and_unsupported_event()
    {
        const string flow = """
        {
          "startNodeId": "start",
          "nodes": {
            "start":  { "name": "提交", "type": "Start", "next": "review" },
            "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "User", "value": "u1" }], "next": "end",
                        "notifications": [{ "events": ["Enter"], "recipients": [{ "type": "Nobody", "value": "" }], "channels": ["Fax"] }] },
            "end":    { "name": "结束", "type": "End",
                        "notifications": [{ "events": ["Enter"], "recipients": [{ "type": "Initiator", "value": "" }], "channels": ["InApp"] }] }
          }
        }
        """;
        var errors = await _host.RunAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Validate(FlowDefinition.Parse(flow))));

        Assert.Contains(errors, e => e.Contains("未注册的渠道 'Fax'"));
        Assert.Contains(errors, e => e.Contains("未注册的接收人类型 'Nobody'"));
        Assert.Contains(errors, e => e.Contains("事件 Enter 不适用"));
    }
}

public sealed class TemplateRendererTests
{
    private static readonly Dictionary<string, object?> Variables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["标题"] = "证书 \"A\"",
        ["title"] = "证书 \"A\"",
        ["form"] = Conditions.FormData.Parse("""{ "amount": 12.5, "applicant": { "name": "张三" } }"""),
        ["dueAt"] = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc)
    };

    [Theory]
    [InlineData("【待审批】{标题}", "【待审批】证书 \"A\"")]
    [InlineData("{title} {form.amount} {FORM.APPLICANT.NAME}", "证书 \"A\" 12.5 张三")]
    [InlineData("截止 {dueAt}", "截止 2026-01-02 03:04")]
    [InlineData("{未知变量} 保留", "{未知变量} 保留")]
    [InlineData("""{"a": 1, "t": "{标题|json}"}""", """{"a": 1, "t": "证书 \"A\""}""")]
    [InlineData("q={标题|url}", "q=%E8%AF%81%E4%B9%A6%20%22A%22")]
    public void Renders_placeholders(string template, string expected) =>
        Assert.Equal(expected, TemplateRenderer.Render(template, Variables, timeZone: TimeZoneInfo.Utc));

    [Fact]
    public void Json_encoding_applies_to_all_placeholders_by_default() =>
        Assert.Equal("""{"t":"证书 \"A\""}""", TemplateRenderer.Render("""{"t":"{标题}"}""", Variables, TemplateEncoding.Json));
}
