using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class SimulationTests
{
    private static ApprovalSimulationRequest Request(string json, object? form = null, string? node = null, int? rule = null) =>
        new(json, JsonSerializer.SerializeToElement(form ?? new { }), node, rule);

    [Theory]
    [InlineData(80000, "high")]
    [InlineData(5000, "low")]
    [InlineData(1, "default")]
    public async Task ConditionsRespectPriorityAndDefault_AndStopAtHumanBoundary(int amount, string selected)
    {
        await using var host = new TestHost();
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
        var review = flow.Nodes.Values.Single(n => n.Type == NodeType.Approval);
        flow.Nodes["start"].Next = "condition";
        flow.Nodes["condition"] = new() { Id = "condition", Type = NodeType.Condition, Name = "金额", Branches =
        [new(0, null, "default"), new(2, "amount >= 5000", "low"), new(1, "amount >= 50000", "high")] };
        foreach (var id in new[] { "high", "low", "default" }) flow.Nodes[id] = new() { Id = id, Name = id, Type = NodeType.Approval, Next = "end", Approvers = review.Approvers };
        flow.Nodes.Remove(review.Id);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow.ToJson(), new { amount })));
        Assert.True(result.IsValid, string.Join(";", result.Errors)); Assert.False(result.IsComplete);
        Assert.Equal(new[] { "start", "condition", selected }, result.Steps.Select(s => s.NodeId));
        Assert.Equal("RequiresDecision", result.Steps[^1].Status);
        Assert.Equal("bob", Assert.Single(result.Steps[^1].ResolvedAssignees).Value);
        Assert.DoesNotContain(result.Steps, s => s.NodeType == NodeType.End);
        Assert.Equal(selected, result.Steps.Single(s => s.NodeId == "condition").Evaluations.Last().NextNodeId);
    }

    [Theory]
    [InlineData("Any", "Role")]
    [InlineData("All", "User")]
    [InlineData("Sequential", "User")]
    public async Task RolePreviewPreservesRuntimeTaskAssignment(string mode, string kind)
    {
        await using var host = new TestHost(); host.Identity.Add("bob", "Reviewer"); host.Identity.Add("carol", "Reviewer");
        var json = Flows.SingleNode(mode, "Role", "Reviewer");
        await host.PublishAsync("roles", json);
        var instance = await host.StartAsync("roles", "1", "alice");
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(json)));
        var preview = result.Steps.Single(s => s.NodeType == NodeType.Approval).ResolvedAssignees;
        Assert.All(preview, a => Assert.Equal(kind, a.Kind.ToString()));
        Assert.Equal(instance.Tasks.OrderBy(t => t.Order).Select(t => (t.AssigneeKind, t.AssigneeValue)), preview.Select(a => (a.Kind, a.Value)));
    }

    [Fact]
    public async Task MissingConditionVariableFails_WithoutSelectingDefault()
    {
        await using var host = new TestHost();
        const string json = """{"schemaVersion":1,"startNodeId":"s","nodes":{"s":{"type":"Start","next":"c"},"c":{"type":"Condition","branches":[{"expression":"amount > 1","next":"e"},{"next":"e"}]},"e":{"type":"End"}}}""";
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(json)));
        Assert.False(result.IsValid); Assert.Contains(result.Errors, e => e.Contains("求值失败"));
        Assert.DoesNotContain(result.Steps, s => s.NodeId == "e");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelJoinWaitsForHumanBranches_OrCompletesAutomaticBranches(bool automatic)
    {
        await using var host = new TestHost();
        var json = """
        {"schemaVersion":1,"startNodeId":"s","nodes":{
          "s":{"type":"Start","next":"p"},"p":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
          "a":{"type":"Approval","approvers":[{"type":"Role","value":"Nobody"}],"emptyApproverPolicy":"POLICY","next":"j"},
          "b":{"type":"Cc","approvers":[{"type":"User","value":"bob"}],"next":"j"},
          "j":{"type":"Join","next":"e"},"e":{"type":"End"}}}
        """;
        json = json.Replace("POLICY", automatic ? "AutoApprove" : "Error");
        // Empty role has no members and follows the configured empty policy.
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(json)));
        Assert.Equal(automatic, result.IsComplete);
        Assert.Contains(result.Steps, s => s.NodeId == "b");
        if (!automatic) Assert.Contains(result.Steps, s => s.NodeId == "j" && s.Status == "Waiting");
        else Assert.Equal("e", result.Steps[^1].NodeId);
    }

    [Fact]
    public async Task SimulationHasNoPersistentOrBusinessEffects_AndHonorsCancellation()
    {
        await using var host = new TestHost(); host.Identity.Add("bob");
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Name = "unchanged" }); await db.SaveChangesAsync();
            var before = await Counts(db);
            var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
            flow.Nodes["start"].Next = "cc";
            flow.Nodes["cc"] = new() { Id = "cc", Type = NodeType.Cc, Next = "review", Approvers = [new("Initiator", "")] };
            var result = await sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow.ToJson()));
            Assert.True(result.IsValid); Assert.Equal(before, await Counts(db)); Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal("unchanged", (await db.Set<TestRecord>().AsNoTracking().SingleAsync()).Name);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<ApprovalSimulationService>()
                .SimulateAsync(Request(Flows.SingleNode("Any", "User", "bob")), canceled.Token));
        });
        Assert.Empty(host.Handler.Calls); Assert.Empty(host.Channel.Sent);
    }

    private static async Task<int[]> Counts(TestDbContext db) =>
        [await db.Set<ApprovalInstance>().CountAsync(), await db.Set<ApprovalTask>().CountAsync(),
         await db.Set<ApprovalLog>().CountAsync(), await db.Set<ApprovalOutboxMessage>().CountAsync(), await db.Set<TestRecord>().CountAsync(),
         await db.Set<ApprovalCarbonCopy>().CountAsync(), await db.Set<ApprovalMessage>().CountAsync(), await db.Set<ApprovalOperation>().CountAsync()];

    [Fact]
    public async Task NestedParallelBranchesUseSharedJoinRules()
    {
        await using var host = new TestHost();
        const string json = """
        {"schemaVersion":1,"startNodeId":"s","nodes":{
        "s":{"type":"Start","next":"p"},"p":{"type":"Parallel","branches":[{"next":"inner"},{"next":"c"}]},
        "inner":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
        "a":{"type":"Cc","approvers":[{"type":"Initiator","value":""}],"next":"ij"},
        "b":{"type":"Cc","approvers":[{"type":"Initiator","value":""}],"next":"ij"},
        "ij":{"type":"Join","next":"j"},
        "c":{"type":"Cc","approvers":[{"type":"Initiator","value":""}],"next":"j"},
        "j":{"type":"Join","next":"e"},"e":{"type":"End"}}}
        """;
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(json)));
        Assert.True(result.IsValid, string.Join(";", result.Errors)); Assert.True(result.IsComplete);
        Assert.Equal(new[] { "s", "p", "inner", "a", "ij", "b", "ij", "j", "c", "j", "e" }, result.Steps.Select(s => s.NodeId));
        Assert.DoesNotContain(result.Steps, s => s.Status == "Waiting");
    }

    [Fact]
    public async Task SimulationUsesRuntimeAutomaticStepBudget()
    {
        await using var host = new TestHost();
        var flow = new FlowDefinition { StartNodeId = "s" };
        flow.Nodes["s"] = new() { Id = "s", Type = NodeType.Start, Next = "c0" };
        for (var index = 0; index < 201; index++) flow.Nodes[$"c{index}"] = new()
        {
            Id = $"c{index}", Type = NodeType.Cc, Next = index == 200 ? "e" : $"c{index + 1}", Approvers = [new("Initiator", "")]
        };
        flow.Nodes["e"] = new() { Id = "e", Type = NodeType.End };
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow.ToJson())));
        Assert.False(result.IsValid); Assert.False(result.IsComplete);
        Assert.Contains(result.Errors, error => error.Contains("步数超限")); Assert.Equal(200, result.Steps.Count);
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("invalid-number", false)]
    public async Task OptionalFormsNormalizeValuesAndCalculateFormulas(string amount, bool valid)
    {
        await using var host = new TestHost();
        const string json = """
        {"schemaVersion":1,"startNodeId":"s","form":{"fields":[{"key":"amount","type":"Number","label":"金额","required":true},{"key":"total","type":"Formula","label":"合计","formula":"amount * 2"}]},"nodes":{
        "s":{"type":"Start","next":"c"},"c":{"type":"Condition","branches":[{"expression":"total == 24","next":"e"},{"next":"review"}]},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"e"},"e":{"type":"End"}}}
        """;
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(json, new { amount, total = 999 })));
        Assert.Equal(valid, result.IsValid);
        Assert.Equal(valid, result.IsComplete);
        if (valid) Assert.DoesNotContain(result.Steps, s => s.NodeId == "review");
        else Assert.NotEmpty(result.Errors);
    }
}
