using System.Net;
using System.Text.Json.Nodes;
using Approval.Core.Actions;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>自定义注册动作：按参数（支持模板）计算并输出结果。</summary>
public sealed class DiscountAction : IApprovalActionHandler
{
    public ApprovalActionDescriptor Descriptor { get; } = new("Discount", "计算折扣",
        [new ActionParameter("amount", "金额", ActionParameterKind.Number) { Required = true }]);

    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct) =>
        Task.FromResult(ApprovalActionResult.FromOutput(new { discounted = decimal.Parse(context.Get("amount")!) * 0.9m }));
}

/// <summary>动作节点：脚本、HTTP、注册动作，同步 / 异步执行。</summary>
public sealed class ActionNodeTests : IAsyncLifetime
{
    private readonly TestHost _host = new(build: b => b.AddActionHandler<DiscountAction>());

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("boss");
        _host.Identity.Add("manager");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>动作节点 → 条件分支（按动作写回的表单字段）→ 大额 / 小额审批。</summary>
    private static string ActionFlow(string actionsJson, string condition) => $$"""
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "act" },
        "act":    { "name": "自动处理", "type": "Action", "actions": {{actionsJson}}, "next": "cond" },
        "cond":   { "name": "判断", "type": "Condition", "branches": [
                      { "priority": 1, "expression": {{System.Text.Json.JsonSerializer.Serialize(condition)}}, "next": "big" },
                      { "priority": 2, "expression": null, "next": "small" } ] },
        "big":    { "name": "大额审批", "type": "Approval", "approvers": [{ "type": "User", "value": "boss" }], "next": "end" },
        "small":  { "name": "小额审批", "type": "Approval", "approvers": [{ "type": "User", "value": "manager" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private async Task<string> CurrentNodeAsync(Guid instanceId) => (await _host.GetInstanceAsync(instanceId)).CurrentNodeId!;

    [Fact]
    public async Task Script_can_update_form_and_drive_the_condition()
    {
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Script", "name": "风险评估", "parameters": {
                 "script": "log('amount', form.amount); form.risk = form.amount > 1000 ? 'high' : 'low'; return { checked: true };" },
               "outputVariable": "scriptResult" }]
            """, "risk == \"high\""));

        var big = await _host.StartAsync("f", "B1", "alice", new { amount = 5000 });
        Assert.Equal("big", await CurrentNodeAsync(big.Id));
        var small = await _host.StartAsync("f", "B2", "alice", new { amount = 10 });
        Assert.Equal("small", await CurrentNodeAsync(small.Id));

        var instance = await _host.GetInstanceAsync(big.Id);
        var form = JsonNode.Parse(instance.FormDataJson)!;
        Assert.Equal("high", form["risk"]!.GetValue<string>());
        Assert.True(form["scriptResult"]!["checked"]!.GetValue<bool>());

        var log = Assert.Single(await _host.LogsAsync(big.Id), l => l.Action == ApprovalAction.Action);
        Assert.Equal("风险评估：执行成功，日志：amount 5000", log.Comment);
    }

    [Theory]
    [InlineData("while (true) {}")]
    [InlineData("function f() { return f(); } f();")]
    [InlineData("throw new Error('boom')")]
    public async Task Script_failure_rolls_back_the_whole_submission(string script)
    {
        await _host.PublishAsync("f", ActionFlow(
            $$"""[{ "type": "Script", "parameters": { "script": {{System.Text.Json.JsonSerializer.Serialize(script)}} } }]""", "true"));

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("f", "B1", "alice"));
        Assert.Contains("动作 '脚本' 执行失败", ex.Message);
        Assert.Empty((await _host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetStartedByAsync("alice"))).Items);
    }

    [Fact]
    public async Task Script_cannot_access_dotnet()
    {
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Script", "parameters": { "script": "form.t = typeof System + '/' + typeof importNamespace + '/' + typeof require" } }]
            """, "false"));

        var instance = await _host.StartAsync("f", "B1", "alice");
        Assert.Equal("undefined/undefined/undefined",
            JsonNode.Parse((await _host.GetInstanceAsync(instance.Id)).FormDataJson)!["t"]!.GetValue<string>());
    }

    [Fact]
    public async Task Continue_on_error_records_failure_and_moves_on()
    {
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Script", "name": "可选步骤", "continueOnError": true, "parameters": { "script": "throw new Error('boom')" } }]
            """, "false"));

        var instance = await _host.StartAsync("f", "B1", "alice");
        Assert.Equal("small", await CurrentNodeAsync(instance.Id));
        Assert.Contains(await _host.LogsAsync(instance.Id), l => l.Comment?.StartsWith("可选步骤：执行失败（已忽略）") == true);
    }

    [Fact]
    public async Task Sync_http_output_is_parsed_and_usable_in_conditions()
    {
        _host.Http.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "level": "high", "score": 87 }""")
        };
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Http", "name": "风控查询", "outputVariable": "risk",
               "parameters": { "method": "POST", "url": "https://api.example.com/risk?id={实例ID}",
                               "body": "{\"title\": \"{标题}\", \"amount\": {form.amount}}" } }]
            """, "risk.body.level == \"high\" && risk.body.score > 80"));

        var instance = await _host.StartAsync("f", "B1", "alice", new { amount = 99 });
        Assert.Equal("big", await CurrentNodeAsync(instance.Id));

        var (request, body) = Assert.Single(_host.Http.Requests);
        Assert.Equal($"https://api.example.com/risk?id={instance.Id}", request.RequestUri!.ToString());
        Assert.Equal("""{"title": "证书 B1", "amount": 99}""", body);
    }

    [Fact]
    public async Task Async_http_goes_through_outbox_with_idempotency_key_and_retries()
    {
        var calls = 0;
        _host.Http.Respond = _ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") }
            : new HttpResponseMessage(HttpStatusCode.OK);
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Http", "name": "同步档案", "mode": "Async",
               "parameters": { "url": "https://api.example.com/archive", "body": "{\"title\": \"{标题}\"}" } }]
            """, "false"));

        var instance = await _host.StartAsync("f", "B1", "alice");
        Assert.Empty(_host.Http.Requests); // 提交时只入队
        var outbox = Assert.Single(await _host.OutboxAsync());
        Assert.Equal(OutboxKind.Action, outbox.Kind);
        Assert.Contains(await _host.LogsAsync(instance.Id), l => l.Comment == "同步档案：已提交后台执行");

        await _host.DispatchOutboxAsync();
        Assert.Contains("HTTP 503", Assert.Single(await _host.OutboxAsync()).LastError);

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.DispatchOutboxAsync();
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await _host.OutboxAsync()).Status);

        Assert.Equal(2, _host.Http.Requests.Count);
        Assert.All(_host.Http.Requests, r =>
            Assert.Equal(outbox.Id.ToString(), r.Request.Headers.GetValues("Idempotency-Key").Single()));
    }

    [Fact]
    public async Task Registered_action_resolves_templates_and_outputs()
    {
        await _host.PublishAsync("f", ActionFlow("""
            [{ "type": "Discount", "outputVariable": "price", "parameters": { "amount": "{form.amount}" } }]
            """, "price.discounted > 100"));

        var instance = await _host.StartAsync("f", "B1", "alice", new { amount = 200 });
        Assert.Equal("big", await CurrentNodeAsync(instance.Id));

        var metadata = await _host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Metadata));
        Assert.Equal(["Discount", "Http", "Script"], metadata.Actions.Select(a => a.Type).Order());
    }

    [Fact]
    public async Task Validator_checks_action_configuration()
    {
        const string flow = """
        {
          "startNodeId": "start",
          "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "act" },
            "act":   { "name": "动作", "type": "Action", "next": "end", "actions": [
                       { "type": "Script", "mode": "Async", "parameters": { "script": "if (" } },
                       { "type": "Http", "parameters": { "url": "http://169.254.169.254/latest" } },
                       { "type": "Http", "mode": "Async", "outputVariable": "x", "parameters": {} },
                       { "type": "Nope" } ] },
            "end":   { "name": "结束", "type": "End" }
          }
        }
        """;
        var errors = await _host.RunAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Validate(FlowDefinition.Parse(flow))));

        Assert.Contains(errors, e => e.Contains("第 1 个动作") && e.Contains("不支持异步"));
        Assert.Contains(errors, e => e.Contains("第 1 个动作") && e.Contains("脚本语法错误"));
        Assert.Contains(errors, e => e.Contains("第 2 个动作") && e.Contains("白名单"));
        Assert.Contains(errors, e => e.Contains("第 3 个动作") && e.Contains("不能设置输出变量"));
        Assert.Contains(errors, e => e.Contains("第 3 个动作") && e.Contains("缺少必填参数「地址」"));
        Assert.Contains(errors, e => e.Contains("未注册的动作类型 'Nope'"));
    }

    [Fact]
    public void Host_allowlist_supports_wildcards()
    {
        Assert.True(HttpActionHandler.IsHostAllowed("api.example.com", ["*.example.com"]));
        Assert.False(HttpActionHandler.IsHostAllowed("example.com.evil.io", ["*.example.com"]));
        Assert.False(HttpActionHandler.IsHostAllowed("localhost", []));
        Assert.True(HttpActionHandler.IsHostAllowed("anything", ["*"]));
    }
}

/// <summary>节点时限：超时提醒 / 重复催办、自动同意、自动驳回。</summary>
public sealed class TimeoutTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string TimeoutFlow(string timeoutJson, string mode = "Any") => $$"""
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "{{mode}}", "timeout": {{timeoutJson}},
                    "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    [Fact]
    public async Task Reminds_after_deadline_and_repeats_up_to_the_limit()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 60, "repeatMinutes": 30, "maxReminders": 2 }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");
        var task = Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks);
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(60), task.DueAt);

        _host.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await _host.ScanTimeoutsAsync());

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());
        // 未配置 Timeout 通知规则时默认给角色成员发站内信
        var reminders = await _host.MessagesAsync();
        Assert.Equal(["r1", "r2"], reminders.Select(m => m.UserId).Order());
        Assert.All(reminders, m => Assert.Equal(NotifyEvent.Timeout, m.Event));
        Assert.Contains("已超过处理时限", reminders[0].Content);

        Assert.Equal(0, await _host.ScanTimeoutsAsync()); // 同一时刻不重复提醒
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());
        _host.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, await _host.ScanTimeoutsAsync()); // 已达最多提醒次数

        task = Assert.Single((await _host.GetInstanceAsync(instance.Id)).Tasks);
        Assert.Equal(2, task.RemindCount);
        Assert.Null(task.NextRemindAt);
        Assert.Equal(2, (await _host.LogsAsync(instance.Id)).Count(l => l.Action == ApprovalAction.Remind));
    }

    [Fact]
    public async Task Handled_task_is_not_reminded()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10 }"""));
        await _host.StartAsync("t", "B1", "alice");
        await _host.ApproveMyTaskAsync("r1");

        _host.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await _host.ScanTimeoutsAsync());
    }

    [Fact]
    public async Task Auto_approve_on_timeout_advances_the_flow()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10, "action": "AutoApprove" }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");

        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await _host.ScanTimeoutsAsync());

        var result = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, result.Status);
        Assert.Contains(result.Logs, l => l.Action == ApprovalAction.TimeoutApprove && l.OperatorId == ApprovalEngine.SystemOperator);
        Assert.Contains(_host.Handler.Calls, c => c.Event == "Approved");
    }

    [Fact]
    public async Task Auto_reject_on_timeout_finishes_as_rejected()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 10, "action": "AutoReject" }"""));
        var instance = await _host.StartAsync("t", "B1", "alice");

        _host.Clock.Advance(TimeSpan.FromMinutes(15));
        await _host.ScanTimeoutsAsync();

        Assert.Equal(InstanceStatus.Rejected, (await _host.GetInstanceAsync(instance.Id)).Status);
        Assert.Contains(_host.Handler.Calls, c => c.Event == "Rejected");
    }

    [Fact]
    public async Task Sequential_deadline_starts_when_it_is_your_turn()
    {
        await _host.PublishAsync("t", TimeoutFlow("""{ "minutes": 60 }""", "Sequential"));
        var instance = await _host.StartAsync("t", "B1", "alice");
        Assert.Null((await _host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "r2").DueAt);

        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await _host.ApproveMyTaskAsync("r1");

        var second = (await _host.GetInstanceAsync(instance.Id)).Tasks.Single(t => t.AssigneeValue == "r2");
        Assert.Equal(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(60), second.DueAt);
    }

    [Fact]
    public async Task Validator_rejects_invalid_timeout()
    {
        const string flow = """
        {
          "startNodeId": "start",
          "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "cc", "timeout": { "minutes": 5 } },
            "cc":    { "name": "抄送", "type": "Cc", "approvers": [{ "type": "Initiator", "value": "" }], "next": "review" },
            "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "User", "value": "u" }], "next": "end",
                        "timeout": { "minutes": 0, "repeatMinutes": -1 } },
            "end":   { "name": "结束", "type": "End" }
          }
        }
        """;
        var errors = await _host.RunAsync(sp =>
            Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Validate(FlowDefinition.Parse(flow))));

        Assert.Contains(errors, e => e.Contains("'start' 不是审批节点"));
        Assert.Contains(errors, e => e.Contains("时限必须大于 0"));
        Assert.Contains(errors, e => e.Contains("催办间隔必须大于 0"));
    }
}
