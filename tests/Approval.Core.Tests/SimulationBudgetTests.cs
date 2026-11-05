using System.Collections;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class SimulationBudgetTests
{
    private sealed class ResolverState
    {
        public int Calls;
        public Func<string, ApproverResolveContext, IReadOnlyList<Assignee>> Resolve = (_, _) => [];
    }
    private sealed class Resolver(ResolverState state) : IApproverResolver
    {
        public string Type => "Budget";
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        { state.Calls++; return Task.FromResult(state.Resolve(value, context)); }
    }
    private sealed class Unenumerable(int count) : IReadOnlyList<Assignee>
    {
        public int Count => count;
        public Assignee this[int index] => throw new InvalidOperationException("Oversized output must not be inspected");
        public IEnumerator<Assignee> GetEnumerator() => throw new InvalidOperationException("Oversized output must not be enumerated");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class Members : IApprovalIdentityProvider, IApprovalUserDirectory, IApprovalUserStatusProvider
    {
        public List<string> RolesCalled { get; } = [];
        public int DirectoryCalls, StatusCalls;
        public Func<string, int, IReadOnlyList<string>> Resolve = (_, _) => [];
        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct)
        {
            RolesCalled.Add(role);
            return Task.FromResult(Resolve(role, RolesCalled.Count(r => r == role)));
        }
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) { StatusCalls++; return Task.FromResult(true); }
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        { DirectoryCalls++; return Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Select(id => new ApprovalUser(id, id, [])).ToArray()); }
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static TestHost Host(ResolverState state, Members? members = null) => new(build: b =>
    {
        b.Services.AddSingleton(state); b.AddApproverResolver<Resolver>();
        if (members is null) return;
        b.Services.AddSingleton<IApprovalIdentityProvider>(members);
        b.Services.AddSingleton<IApprovalUserDirectory>(members);
        b.Services.AddSingleton<IApprovalUserStatusProvider>(members);
    });
    private static ApprovalSimulationRequest Request(FlowDefinition flow, string? node = null, int? rule = null) =>
        new(flow.ToJson(), JsonSerializer.SerializeToElement(new { }), node, rule);

    private static FlowDefinition Chain(IEnumerable<List<ApproverRule>> rules)
    {
        var nodes = rules.ToArray(); var flow = new FlowDefinition { StartNodeId = "s" };
        flow.Nodes["s"] = new() { Id = "s", Type = NodeType.Start, Next = "n0" };
        for (var index = 0; index < nodes.Length; index++) flow.Nodes[$"n{index}"] = new()
        { Id = $"n{index}", Type = NodeType.Cc, Next = index + 1 == nodes.Length ? "e" : $"n{index + 1}", Approvers = nodes[index] };
        flow.Nodes["e"] = new() { Id = "e", Type = NodeType.End };
        return flow;
    }
    private static IEnumerable<int> Chunks(int total, int size)
    { while (total > 0) { var count = Math.Min(total, size); yield return count; total -= count; } }
    private static void Failed(ApprovalSimulationResult result, string dimension)
    {
        Assert.False(result.IsValid); Assert.False(result.IsComplete);
        Assert.Contains(dimension, Assert.Single(result.Errors));
        Assert.Equal("Failed", result.Steps[^1].Status); Assert.Equal(result.Errors[0], result.Steps[^1].BlockReason);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2000)]
    [InlineData(2001)]
    public async Task SimulationResolvedAssigneeBudgetCountsRawOutputsAcrossNodes(int total)
    {
        var state = new ResolverState { Resolve = (value, _) => Enumerable.Repeat(new Assignee(AssigneeKind.User, "bob"), int.Parse(value)).ToArray() };
        await using var host = Host(state);
        var flow = Chain(Chunks(total, 200).Select(count => new List<ApproverRule> { new("Budget", count.ToString()) }));
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        Assert.Equal((total + 199) / 200, state.Calls);
        if (total > 2000) Failed(result, "解析对象累计超过 2000");
        else
        {
            Assert.True(result.IsValid); Assert.True(result.IsComplete); Assert.Empty(result.Errors);
            Assert.All(result.Steps.Where(s => s.NodeType == NodeType.Cc), s => Assert.Equal(new Assignee(AssigneeKind.User, "bob"), Assert.Single(s.ResolvedAssignees)));
        }
    }

    [Theory]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    public async Task SimulationResolverBudgetStopsBeforeTheNextCallback(int calls)
    {
        var state = new ResolverState(); await using var host = Host(state);
        var flow = Chain(Chunks(calls, 100).Select(count => Enumerable.Repeat(new ApproverRule("Budget", "empty"), count).ToList()));
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        Assert.Equal(Math.Min(calls, 500), state.Calls);
        if (calls > 500) Failed(result, "Resolver 调用累计超过 500");
        else { Assert.True(result.IsValid); Assert.True(result.IsComplete); Assert.Empty(result.Errors); }
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2000)]
    [InlineData(2001)]
    public async Task SimulationTraceBudgetAccumulatesAcrossNodes(int total)
    {
        var state = new ResolverState { Resolve = (value, context) =>
        {
            for (var index = 0; index < int.Parse(value); index++) context.Trace!(new("Budget", "explanation"));
            return [];
        } };
        await using var host = Host(state);
        var counts = Chunks(total, 200).ToList();
        if (counts[^1] == 1) { counts[^2]--; counts[^1]++; } // Every rule also produces two engine traces.
        var flow = Chain(counts.Select(count => new List<ApproverRule> { new("Budget", (count - 2).ToString()) }));
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        if (total > 2000) Failed(result, "解析说明累计超过 2000");
        else { Assert.True(result.IsComplete); Assert.True(result.IsValid); Assert.Equal(total, result.Steps.Sum(s => s.ResolutionTrace.Count)); }
    }

    [Theory]
    [InlineData(4999)]
    [InlineData(5000)]
    [InlineData(5001)]
    public async Task SimulationRoleMemberBudgetCountsRepeatedExpansionsAndRawDuplicates(int total)
    {
        var state = new ResolverState();
        var members = new Members { Resolve = (role, call) =>
        {
            var count = role != "r12" ? 200 : call == 1 ? 100 : total - 4900;
            return Enumerable.Range(0, count).Select(index => "user" + index % 100).ToArray();
        } };
        await using var host = Host(state, members);
        var flow = new FlowDefinition { StartNodeId = "s" };
        flow.Nodes["s"] = new() { Id = "s", Type = NodeType.Start, Next = "p" };
        flow.Nodes["p"] = new() { Id = "p", Type = NodeType.Parallel };
        var branches = total > 5000 ? 14 : 13;
        for (var index = 0; index < branches; index++)
        {
            var id = $"n{index}"; flow.Nodes["p"].Branches.Add(new(index, null, id));
            flow.Nodes[id] = new() { Id = id, Type = NodeType.Approval, Mode = ApproveMode.Any, Next = "j", Approvers = [new("Role", $"r{index}")] };
        }
        flow.Nodes["j"] = new() { Id = "j", Type = NodeType.Join, Next = "e" }; flow.Nodes["e"] = new() { Id = "e", Type = NodeType.End };
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        Assert.Equal(26, members.RolesCalled.Count); Assert.DoesNotContain("r13", members.RolesCalled);
        Assert.Equal(total > 5000 ? 25 : 26, members.DirectoryCalls);
        if (total > 5000) Failed(result, "角色成员累计超过 5000");
        else { Assert.True(result.IsValid); Assert.False(result.IsComplete); Assert.Equal(13, result.Steps.Count(s => s.Status == "RequiresDecision")); }
    }

    [Theory]
    [InlineData(ApproveMode.Any)]
    [InlineData(ApproveMode.All)]
    [InlineData(ApproveMode.Sequential)]
    public async Task SimulationRoleLimitStopsBeforeDirectoryInEveryMode(ApproveMode mode)
    {
        var members = new Members { Resolve = (_, _) => Enumerable.Range(0, 201).Select(i => "user" + i).ToArray() };
        await using var host = Host(new(), members);
        var flow = FlowDefinition.Parse(Flows.SingleNode(mode.ToString(), "Role", "reviewers"));
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        Failed(result, "角色成员超过 200"); Assert.Single(members.RolesCalled);
        Assert.Equal(0, members.DirectoryCalls); Assert.Equal(0, members.StatusCalls);
    }

    [Theory]
    [InlineData(ApproveMode.All)]
    [InlineData(ApproveMode.Sequential)]
    public async Task SimulationExpandedUsersConsumeTheRequestAssigneeBudget(ApproveMode mode)
    {
        var state = new ResolverState { Resolve = (value, _) => value == "role" ? [new(AssigneeKind.Role, "reviewers")]
            : Enumerable.Repeat(new Assignee(AssigneeKind.User, "bob"), int.Parse(value)).ToArray() };
        var members = new Members { Resolve = (_, _) => Enumerable.Range(0, 10).Select(i => "user" + i).ToArray() };
        await using var host = Host(state, members);
        var rules = Chunks(1990, 200).Select(count => new List<ApproverRule> { new("Budget", count.ToString()) }).ToList();
        rules.Add([new("Budget", "role")]); var flow = Chain(rules);
        flow.Nodes["n10"].Type = NodeType.Approval; flow.Nodes["n10"].Mode = mode;
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(flow)));
        Failed(result, "解析对象累计超过 2000"); Assert.Single(members.RolesCalled); Assert.Equal(0, members.DirectoryCalls);
    }

    [Fact]
    public async Task SimulationRejectsOversizedResolverOutputBeforeEnumerationOrTraceFormatting()
    {
        var state = new ResolverState { Resolve = (_, _) => new Unenumerable(2001) }; await using var host = Host(state);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(Chain([[new("Budget", "large")]]))));
        Failed(result, "解析对象累计超过 2000"); Assert.Equal(1, state.Calls);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public async Task SimulationNodeAssigneeLimitRemainsIndependentFromRequestBudget(int count)
    {
        var state = new ResolverState { Resolve = (_, _) => Enumerable.Range(0, count).Select(i => new Assignee(AssigneeKind.User, "user" + i)).ToArray() };
        await using var host = Host(state);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(Chain([[new("Budget", "users")]]))));
        if (count > 200) Failed(result, "预览处理人超过 200");
        else { Assert.True(result.IsComplete); Assert.Equal(200, result.Steps.Single(s => s.NodeType == NodeType.Cc).ResolvedAssignees.Count); }
    }

    [Fact]
    public async Task SimulationTraceBudgetFailureCannotBeSwallowedByResolver()
    {
        var state = new ResolverState { Resolve = (_, context) =>
        {
            try { for (var index = 0; index < 201; index++) context.Trace!(new("Budget", "explanation")); }
            catch (ApprovalException) { /* A host callback cannot make the request healthy again. */ }
            return new Unenumerable(0);
        } };
        await using var host = Host(state);
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(Request(Chain([[new("Budget", "trace")]]))));
        Failed(result, "解析说明超过 200"); Assert.Equal(1, state.Calls);
    }

    [Fact]
    public async Task SimulationBudgetIsLocalToEachRequestAndFailuresHaveNoEffects()
    {
        var state = new ResolverState { Resolve = (value, _) => Enumerable.Repeat(new Assignee(AssigneeKind.User, "bob"), int.Parse(value)).ToArray() };
        await using var host = Host(state);
        await host.RunAsync(async sp =>
        {
            var service = sp.GetRequiredService<ApprovalSimulationService>(); var db = sp.GetRequiredService<TestDbContext>();
            var tooLarge = Request(Chain([[new("Budget", "2001")]]), "n0", 0);
            Failed(await service.SimulateAsync(tooLarge), "解析对象累计超过 2000");
            var normal = Chain(Chunks(2000, 200).Select(count => new List<ApproverRule> { new("Budget", count.ToString()) }));
            for (var index = 0; index < 2; index++) Assert.True((await service.SimulateAsync(Request(normal))).IsComplete);
            Assert.True((await service.SimulateAsync(Request(normal, "n0", 0))).IsValid);
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().CountAsync()); Assert.Equal(0, await db.Set<TestRecord>().CountAsync());
        });
        Assert.Empty(host.Handler.Calls); Assert.Empty(host.Channel.Sent);
    }
}
