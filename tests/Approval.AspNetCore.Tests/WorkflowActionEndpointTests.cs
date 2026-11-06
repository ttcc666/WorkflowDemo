using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class WorkflowActionEndpointTests
{
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
