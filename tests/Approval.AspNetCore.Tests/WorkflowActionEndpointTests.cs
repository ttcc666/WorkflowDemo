using Approval.Core.Engine;
using Approval.Core.Domain;
using Approval.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class WorkflowActionEndpointTests
{
    [Fact]
    public async Task Notification_recipient_is_validated_by_the_forms_entry_point()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddApprovalForms().AddNotificationActions());
        await host.PublishAsync("action-notify", """
            {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},
             "form":{"fields":[{"key":"person","type":"User","label":"Person","required":true}]},"nodes":{
              "start":{"type":"Start","next":"notify"},
              "notify":{"type":"Action","next":"end","actions":[{"id":"notice","type":"Notification","config":{
                "recipients":[{"type":"FormField","value":"person"}],"channels":["InApp"],"title":"Hello","content":"Approval"}}]},
              "end":{"type":"End"}
            }}
            """);
        const string start = "/api/approval/forms/action-notify/start";
        var invalid = await (await host.SendAsync(HttpMethod.Post, start, "alice", new { formData = new { person = "missing" } })).ProblemAsync(400);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("person", out _));
        Assert.False(await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalInstance>().AnyAsync()));
        Assert.False(await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalMessage>().AnyAsync()));

        await host.PostJsonAsync(start, "alice", new { formData = new { person = "bob" } });
        var message = Assert.Single((await host.GetJsonAsync("/api/approval/messages", "bob")).EnumerateArray());
        Assert.Equal("Action", message.GetProperty("event").GetString());
        Assert.Equal("Hello", message.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Action_failure_problem_only_reports_safe_error()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddApprovalForms().AddCalculationActions());
        await host.PublishAsync("action-fails", """
            {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},
             "form":{"fields":[{"key":"memo","type":"Text","label":"Memo"}]},"nodes":{
              "start":{"type":"Start","next":"action"},
              "action":{"type":"Action","next":"end","actions":[{"id":"fault","type":"JavaScript","config":{
                "script":"function execute(c) { throw new Error(c.form.memo); }"}}]},"end":{"type":"End"}
            }}
            """);
        const string secret = "credential-from-handler-should-stay-private";
        var problem = await (await host.SendAsync(HttpMethod.Post, "/api/approval/forms/action-fails/start", "alice",
            new { formData = new { memo = secret } })).ProblemAsync(400);
        Assert.Equal("InvalidOperation", problem.GetProperty("code").GetString());
        Assert.Contains("ExecutionFailed", problem.GetProperty("title").GetString());
        Assert.DoesNotContain(secret, problem.GetRawText());
        Assert.False(await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalInstance>().AnyAsync()));
    }

    [Fact]
    public async Task Design_metadata_and_action_audit_are_visible_only_with_their_capabilities()
    {
        await using var host = await ApiHost.StartAsync(build: builder => builder.AddCalculationActions());
        var designer = await host.GetJsonAsync("/api/approval/meta", "admin");
        Assert.Contains(designer.GetProperty("actions").EnumerateArray(), entry => entry.GetProperty("type").GetString() == "JavaScript");
        var participant = await host.GetJsonAsync("/api/approval/meta", "alice");
        Assert.True(!participant.TryGetProperty("actions", out var actions) || actions.ValueKind == System.Text.Json.JsonValueKind.Null);
        await host.PublishAsync("action", """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"calculate"},
              "calculate":{"type":"Action","next":"end","actions":[
                {"id":"secret-step","type":"JavaScript","outputKey":"risk",
                 "config":{"script":"function execute() { return { internalResult: 'safe-example' }; }"}}
              ]},
              "end":{"type":"End"}
            }}
            """);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("action", "Doc", "D-1", "alice")));
        var publicDefinition = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}/definition", "alice");
        Assert.DoesNotContain("internalResult", publicDefinition.GetProperty("definitionJson").GetString());
        Assert.DoesNotContain("secret-step", publicDefinition.GetProperty("definitionJson").GetString());
        var publicDetails = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}", "alice");
        Assert.DoesNotContain("secret-step", publicDetails.GetRawText());
        var adminDetails = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}", "admin");
        Assert.Contains("secret-step", adminDetails.GetRawText());
        Assert.DoesNotContain("safe-example", adminDetails.GetRawText());
    }
}
