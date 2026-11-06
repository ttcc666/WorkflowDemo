using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class ActionNodeSimulationTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"a"},
          "a":{"type":"Action","next":"condition","actions":[
            {"id":"left","type":"JavaScript","outputKey":"left","config":{"script":"function execute(c) { return { value: c.form.amount }; }"}}
          ]},
          "condition":{"type":"Condition","branches":[
            {"expression":"workflow.outputs.left.value > 10","next":"target"},{"next":"end"}
          ]},
          "target":{"type":"Action","next":"end","actions":[
            {"id":"right","type":"JavaScript","outputKey":"right","config":{"script":"function execute(c) { return { value: c.outputs.left.value * 2 }; }"}}
          ]},"end":{"type":"End"}
        }}
        """;

    [Theory]
    [InlineData(12, true)]
    [InlineData(9, false)]
    public async Task Target_preview_accumulates_outputs_follows_condition_and_leaves_storage_unchanged(int amount, bool reachesTarget)
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        var result = await Simulate(host, Definition, amount);
        Assert.Equal(reachesTarget, result.IsValid);
        Assert.False(result.IsComplete);
        var target = result.Steps[^1];
        Assert.Equal("target", target.NodeId);
        Assert.Equal(reachesTarget, result.Steps.Single(s => s.NodeId == "condition").Evaluations[0].Matched);
        if (reachesTarget)
        {
            Assert.Equal(["start", "a", "condition", "target"], result.Steps.Select(s => s.NodeId));
            Assert.Equal(24, Assert.Single(target.Actions!).Output!.Value.GetProperty("value").GetInt32());
        }
        else
        {
            Assert.Null(target.Actions);
            Assert.Contains("未经过目标动作", target.BlockReason);
        }
        await AssertReadOnly(host);
    }

    [Theory]
    [InlineData("failure", "动作预演失败")]
    [InlineData("missing-preview", "未注册只读预演")]
    [InlineData("human", "需要人工决策")]
    public async Task Unreachable_target_reports_its_upstream_blocker(string mode, string reason)
    {
        var executed = new ActionCalls();
        await using var host = new TestHost(build: b =>
        {
            b.AddCalculationActions().AddActionHandler<NoPreview>();
            b.Services.AddSingleton(executed);
        });
        var flow = JsonNode.Parse(Definition)!;
        var nodes = flow["nodes"]!.AsObject();
        var first = nodes["a"]!["actions"]![0]!;
        if (mode == "failure") first["config"]!["script"] = "function execute() { throw new Error('secret'); }";
        if (mode == "missing-preview") first["type"] = "NoPreview";
        if (mode == "human")
        {
            nodes["start"]!["next"] = "review";
            nodes["review"] = JsonNode.Parse("""{"type":"Approval","next":"a","approvers":[{"type":"User","value":"bob"}]}""");
        }
        var result = await Simulate(host, flow.ToJsonString(), 12);
        Assert.False(result.IsValid);
        Assert.Equal("target", result.Steps[^1].NodeId);
        Assert.Contains(reason, result.Steps[^1].BlockReason);
        Assert.Equal(0, executed.Count);
        await AssertReadOnly(host);
    }

    [Fact]
    public async Task Nested_join_combines_visible_branch_outputs_and_empty_approval_can_auto_pass()
    {
        await using var host = new TestHost(build: b => b.AddCalculationActions());
        var result = await Simulate(host, """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"review"},
              "review":{"type":"Approval","mode":"All","emptyApproverPolicy":"AutoApprove","approvers":[{"type":"Role","value":"Nobody"}],"next":"fork"},
              "fork":{"type":"Parallel","branches":[{"next":"inner"},{"next":"b"}]},
              "inner":{"type":"Parallel","branches":[{"next":"a"},{"next":"cc"}]},
              "a":{"type":"Action","next":"innerJoin","actions":[{"id":"a","type":"JavaScript","outputKey":"left","config":{"script":"function execute() { return { value: 2 }; }"}}]},
              "cc":{"type":"Cc","approvers":[{"type":"User","value":"alice"}],"next":"innerJoin"},
              "innerJoin":{"type":"Join","next":"join"},
              "b":{"type":"Action","next":"join","actions":[{"id":"b","type":"JavaScript","outputKey":"right","config":{"script":"function execute(c) { return { value: c.outputs.left === undefined ? 3 : 100 }; }"}}]},
              "join":{"type":"Join","next":"target"},
              "target":{"type":"Action","next":"end","actions":[{"id":"sum","type":"JavaScript","outputKey":"sum","config":{"script":"function execute(c) { return { value: c.outputs.left.value + c.outputs.right.value }; }"}}]},
              "end":{"type":"End"}
            }}
            """, 0);
        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        Assert.Contains(result.Steps, s => s.Status == "AutoApproved");
        Assert.Equal(5, Assert.Single(result.Steps[^1].Actions!).Output!.Value.GetProperty("value").GetInt32());
        Assert.DoesNotContain(result.Steps, s => s.Status == "Waiting");
        await AssertReadOnly(host);
    }

    [Fact]
    public async Task Async_target_shows_plan_without_executing_or_enqueuing()
    {
        var executed = new ActionCalls();
        await using var host = new TestHost(build: b =>
        {
            b.AddCalculationActions().AddActionHandler<AsyncPlan>();
            b.Services.AddSingleton(executed);
        });
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["target"]!["actions"] = JsonNode.Parse("""
            [{"id":"deliver","type":"AsyncPlan","mode":"Async","failurePolicy":"Retry","config":{}}]
            """);
        var result = await Simulate(host, flow.ToJsonString(), 12);
        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        Assert.False(result.IsComplete);
        var step = Assert.Single(result.Steps[^1].Actions!);
        Assert.Equal("Skipped", step.Status);
        Assert.Equal(12, step.Output!.Value.GetProperty("amount").GetInt32());
        Assert.Equal(0, executed.Count);
        await AssertReadOnly(host);
    }

    private static Task<ApprovalSimulationResult> Simulate(TestHost host, string flow, int amount) => host.RunAsync(sp =>
        sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(flow, JsonSerializer.SerializeToElement(new { amount }), "target")));

    private static Task AssertReadOnly(TestHost host) => host.RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<TestDbContext>();
        Assert.False(await db.Set<ApprovalInstance>().AnyAsync());
        Assert.False(await db.Set<ApprovalTask>().AnyAsync());
        Assert.False(await db.Set<ApprovalLog>().AnyAsync());
        Assert.False(await db.Set<ApprovalOutboxMessage>().AnyAsync());
        Assert.False(await db.Set<TestRecord>().AnyAsync());
    });

    private sealed class ActionCalls { internal int Count; }

    private sealed class NoPreview(ActionCalls calls) : IApprovalActionHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new("NoPreview", "NoPreview", "Test", [ActionExecutionMode.Sync]);
        public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => [];
        public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            calls.Count++;
            throw new InvalidOperationException();
        }
    }

    private sealed class AsyncPlan(ActionCalls calls) : IApprovalActionHandler, IApprovalActionPreviewHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new("AsyncPlan", "Async plan", "Test", [ActionExecutionMode.Async]);
        public bool ProvidesOutput => false;
        public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => [];
        public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) =>
            Task.FromResult(new ApprovalActionResult(true,
                JsonSerializer.SerializeToElement(new { amount = context.Outputs.GetProperty("left").GetProperty("value").GetInt32() })));
        public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            calls.Count++;
            throw new InvalidOperationException();
        }
    }
}
