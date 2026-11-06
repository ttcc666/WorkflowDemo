using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class WorkflowActionTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"action"},
          "action":{"type":"Action","next":"condition","actions":[
            {"id":"first","type":"Risk","outputKey":"risk","config":{"threshold":100}},
            {"id":"second","type":"Risk","outputKey":"review","config":{}}
          ]},
          "condition":{"type":"Condition","branches":[
            {"priority":1,"expression":"outputs.review.level == \"high\" && form.amount >= 100","next":"review"},
            {"priority":2,"next":"end"}
          ]},
          "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},
          "end":{"type":"End"}
        }}
        """;

    [Fact]
    public async Task Runs_in_order_and_routes_using_persisted_outputs()
    {
        await using var host = CreateHost();
        await host.PublishAsync("risk", Definition);
        var instance = await host.StartAsync("risk", "B1", "alice", new { amount = 125 });
        var stored = await host.GetInstanceAsync(instance.Id);
        Assert.Equal("review", Assert.Single(stored.Tasks).NodeId);
        var state = ExecutionState.Load(stored);
        Assert.Equal("high", state.ActionOutputs["risk"].Value.GetProperty("level").GetString());
        Assert.Equal("high", state.ActionOutputs["review"].Value.GetProperty("level").GetString());
        Assert.Equal(["first", "second"], stored.Logs.Where(l => l.Action == ApprovalAction.ActionExecuted)
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l.Details!).GetProperty("id").GetString()));
        await host.RunAsync(async sp => Assert.Single(await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().ToListAsync()));
    }

    [Fact]
    public async Task Step_failure_rolls_back_host_data_instance_audit_and_outbox()
    {
        await using var host = CreateHost();
        await host.PublishAsync("risk", Definition);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("risk", "B1", "alice", new { amount = -1 }));
        Assert.Contains("action/second", error.Message);
        Assert.DoesNotContain("private exception", error.Message);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Empty(await db.Set<TestRecord>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
        });
    }

    [Fact]
    public async Task Replay_of_the_same_start_command_returns_original_instance_without_running_steps_twice()
    {
        await using var host = CreateHost();
        host.Identity.Add("alice", "Admin");
        await host.PublishAsync("risk", Definition);
        var request = new StartApprovalRequest("risk", "Purchase", "PO", "alice")
            { OperationId = "start-1", FormData = new { amount = 125 } };
        Task<ApprovalInstance> Start() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        var first = await Start();
        Assert.Equal(first.Id, (await Start()).Id);
        var saved = await host.GetInstanceAsync(first.Id);
        Assert.Equal(2, saved.Logs.Count(l => l.Action == ApprovalAction.ActionExecuted));
        await host.RunAsync(async sp => Assert.Single(await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().ToListAsync()));
    }

    [Fact]
    public async Task Parallel_outputs_are_isolated_until_join_then_visible_together()
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        await host.PublishAsync("parallel-actions", """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"fork"},
              "fork":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
              "a":{"type":"Action","next":"join","actions":[
                {"id":"a","type":"JavaScript","outputKey":"left","config":{"script":"function execute() { return { x: 2 }; }"}}
              ]},
              "b":{"type":"Action","next":"join","actions":[
                {"id":"b","type":"JavaScript","outputKey":"right","config":{"script":"function execute(c) { return { x: c.outputs.left === undefined ? 3 : 100 }; }"}}
              ]},
              "join":{"type":"Join","next":"total"},
              "total":{"type":"Action","next":"end","actions":[
                {"id":"sum","type":"JavaScript","outputKey":"total","config":{"script":"function execute(c) { return { x: c.outputs.left.x + c.outputs.right.x }; }"}}
              ]},"end":{"type":"End"}
            }}
            """);
        var instance = await host.StartAsync("parallel-actions", "PO", "alice");
        var state = ExecutionState.Load(await host.GetInstanceAsync(instance.Id));
        Assert.Empty(state.Joins);
        Assert.Equal(5, state.ActionOutputs["total"].Value.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Return_to_start_discards_previous_output_and_resubmit_recalculates()
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        await host.PublishAsync("resubmit-actions", """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"calc"},
              "calc":{"type":"Action","next":"review","actions":[
                {"id":"calc","type":"JavaScript","outputKey":"amount","config":{"script":"function execute(c) { return { value: c.form.amount }; }"}}
              ]},
              "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},
              "end":{"type":"End"}
            }}
            """);
        var instance = await host.StartAsync("resubmit-actions", "PO", "alice", new { amount = 10 });
        var review = Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(review.Id, "bob", "Change amount", "start"));
        var returned = await host.GetInstanceAsync(instance.Id);
        Assert.Empty(ExecutionState.Load(returned).ActionOutputs);
        var resubmit = Assert.Single(returned.Tasks, task => task.Kind == ApprovalTaskKind.Resubmit);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.Id, "alice", formData: new { amount = 20 }));
        var updated = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(20, ExecutionState.Load(updated).ActionOutputs["amount"].Value.GetProperty("value").GetInt32());
        Assert.Equal(2, updated.Logs.Count(log => log.Action == ApprovalAction.ActionExecuted));
    }

    [Theory]
    [InlineData("type", "\"Missing\"", "动作类型未注册")]
    [InlineData("mode", "\"Async\"", "不支持该执行模式")]
    [InlineData("configVersion", "2", "配置版本未知")]
    [InlineData("outputKey", "\"review\"", "输出键无效")]
    public async Task Publish_rejects_invalid_step_configuration(string property, string value, string reason)
    {
        await using var host = CreateHost();
        var json = System.Text.Json.Nodes.JsonNode.Parse(Definition)!;
        json["nodes"]!["action"]!["actions"]![0]![property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("risk", json.ToJsonString()));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task Public_view_omits_step_configuration_and_simulation_does_not_execute_handler()
    {
        await using var host = CreateHost();
        var definition = FlowDefinition.Parse(Definition);
        Assert.Null(definition.ToPublicView().GetNode("action").Actions);
        Assert.Equal(2, definition.GetNode("action").Actions!.Count);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>()
            .SimulateAsync(new(Definition, JsonSerializer.SerializeToElement(new { amount = 125 }))));
        Assert.False(result.IsComplete);
        Assert.Equal("Blocked", Assert.Single(result.Steps, s => s.NodeType == NodeType.Action).Status);
        await host.RunAsync(async sp => Assert.Empty(await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().ToListAsync()));
        Assert.Empty(await host.OutboxAsync());
    }

    [Fact]
    public async Task Condition_cannot_inspect_clr_type_through_a_dynamic_action_output()
    {
        await using var host = CreateHost();
        var flow = System.Text.Json.Nodes.JsonNode.Parse(Definition)!;
        flow["nodes"]!["condition"]!["branches"]![0]!["expression"] = "outputs.risk.GetType() != null";
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("reflection", flow.ToJsonString()));
        Assert.Contains("不能读取 CLR 类型", failure.Message);
    }

    private static TestHost CreateHost() => new(build: b => b.AddActionHandler<RiskHandler>().AddActionHandler<RiskHandler>());

    private sealed class RiskHandler(TestDbContext db) : IApprovalActionHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new("Risk", "Risk", "Test", [ActionExecutionMode.Sync]);

        public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => version == 1 ? [] : ["配置版本未知"];

        public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            var amount = context.Form.GetProperty("amount").GetInt32();
            if (config.TryGetProperty("threshold", out var threshold))
            {
                db.Add(new TestRecord { Id = context.ExecutionId, Name = context.BusinessKey });
                return Task.FromResult(new ApprovalActionResult(true,
                    JsonSerializer.SerializeToElement(new { level = amount >= threshold.GetInt32() ? "high" : "low" })));
            }
            if (amount < 0) throw new InvalidOperationException("private exception");
            return Task.FromResult(new ApprovalActionResult(true, context.Outputs.GetProperty("risk")));
        }
    }
}
