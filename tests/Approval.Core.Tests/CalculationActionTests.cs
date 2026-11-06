using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class CalculationActionTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"action"},
          "action":{"type":"Action","next":"route","actions":[
            {"id":"risk","type":"JavaScript","outputKey":"risk","config":{
              "script":"function execute(context) { return { high: context.form.items.some(i => i.total >= 500), items: context.form.items }; }"
            }},
            {"id":"map","type":"Mapping","outputKey":"purchase","config":{"mappings":[
              {"source":"outputs.risk.high","target":"requiresReview","transform":"Boolean"},
              {"source":"form.vendor","target":"supplier.name","default":"unknown"},
              {"source":"outputs.risk.items","target":"lines","transform":"Array","filterPath":"selected","filterEquals":true,
               "items":[{"source":"total","target":"amount","transform":"Number","precision":2},
                        {"source":"memo","target":"memo","omitNull":true}]}
            ]}}
          ]},
          "route":{"type":"Condition","branches":[
            {"priority":1,"expression":"outputs.purchase.requiresReview == true","next":"review"},
            {"priority":2,"next":"end"}]},
          "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},
          "end":{"type":"End"}
        }}
        """;

    [Fact]
    public async Task Script_and_mapping_run_in_order_and_simulation_matches_runtime_output()
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        host.Identity.Add("bob");
        var form = JsonSerializer.Deserialize<JsonElement>("""
            {"vendor":null,"items":[{"total":501.235,"selected":true},{"total":2,"selected":false}]}
            """);
        await host.PublishAsync("purchase", Definition);
        var preview = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(Definition, form)));
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        Assert.Empty(await host.OutboxAsync());
        var instance = await host.StartAsync("purchase", "B1", "alice", form);
        var stored = await host.GetInstanceAsync(instance.Id);
        Assert.Equal("review", Assert.Single(stored.Tasks).NodeId);
        var output = ExecutionState.Load(stored).ActionOutputs["purchase"].Value;
        Assert.Equal("unknown", output.GetProperty("supplier").GetProperty("name").GetString());
        var line = Assert.Single(output.GetProperty("lines").EnumerateArray());
        Assert.Equal(501.24m, line.GetProperty("amount").GetDecimal());
        Assert.False(line.TryGetProperty("memo", out _));
        Assert.True(JsonElement.DeepEquals(output, Assert.Single(preview.Steps, s => s.NodeId == "action").Actions![1].Output!.Value));
    }

    [Theory]
    [InlineData("function execute() { while (true) {} }")]
    [InlineData("function execute() { function f() { return 1 + f(); } return { answer: f() }; }")]
    [InlineData("function execute() { return { answer: System.IO.File.ReadAllText('secret') }; }")]
    [InlineData("function execute() { return { answer: fetch('https://example.invalid') }; }")]
    [InlineData("function execute() { return { answer: Function('return 1')() }; }")]
    [InlineData("function execute() { return { answer: 'a'.repeat(50000) }; }")]
    [InlineData("function execute() { let value = {}; for (let i = 0; i < 1000; i++) value = { value }; return value; }")]
    public async Task Script_escape_or_exhaustion_rolls_back_and_a_new_engine_remains_usable(string script)
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        var flow = System.Text.Json.Nodes.JsonNode.Parse(Definition)!;
        flow["nodes"]!["action"]!["actions"]![0]!["config"]!["script"] = script;
        await host.PublishAsync("unsafe", flow.ToJsonString());
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("unsafe", "B1", "alice", new { }));
        Assert.Empty(await host.OutboxAsync());
        var handler = new JavaScriptActionHandler();
        var empty = JsonSerializer.SerializeToElement(new { });
        var result = await handler.ExecuteAsync(new(Guid.Empty, "", 0, "", "", "", "", "", "", "", 0, "",
                InstanceStatus.Running, Guid.Empty, 1, empty, empty),
            JsonSerializer.SerializeToElement(new { script = "function execute() { return { ok: true }; }" }), CancellationToken.None);
        Assert.True(result.Output!.Value.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Publish_rejects_invalid_javascript_syntax()
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        var flow = System.Text.Json.Nodes.JsonNode.Parse(Definition)!;
        flow["nodes"]!["action"]!["actions"]![0]!["config"]!["script"] = "function execute( {";
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("invalid", flow.ToJsonString()));
        Assert.Contains("语法无效", error.Message);
    }
}
