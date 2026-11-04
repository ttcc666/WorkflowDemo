using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using System.Dynamic;

namespace Approval.Core.Tests;

public sealed class ResolverFormDataIsolationTests
{
    private const string Json = """
        {"amount":12.5,"enabled":true,"missing":null,"title":"original","large":1e100,
         "person":{"name":"alice","tags":["first",{"code":"nested"}]},
         "items":[{"owner":{"name":"bob"}},2]}
        """;

    private sealed class Captures
    {
        public string Mutation = "set";
        public bool Fail;
        public List<ApproverResolveContext> Mutated { get; } = [];
        public List<ApproverResolveContext> Observed { get; } = [];
    }

    private sealed class MutatingResolver(Captures captures) : IApproverResolver
    {
        public string Type => "Mutating";
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        {
            AssertOriginal(context);
            var dictionary = Assert.IsAssignableFrom<IDictionary<string, object?>>(context.FormData);
            Assert.True(dictionary.IsReadOnly);
            Assert.Throws<NotSupportedException>(() =>
            {
                switch (captures.Mutation)
                {
                    case "add": dictionary.Add("injected", true); break;
                    case "remove": dictionary.Remove("title"); break;
                    case "clear": dictionary.Clear(); break;
                    default: dictionary["title"] = "changed"; break;
                }
            });
            dynamic person = context.FormData["person"]!;
            person.name = "changed";
            var tags = Assert.IsType<object?[]>(person.tags);
            tags[0] = "changed";
            ((IDictionary<string, object?>)tags[1]!)["code"] = "changed";
            var items = Assert.IsType<object?[]>(context.FormData["items"]);
            dynamic item = items[0]!;
            item.owner.name = "changed";
            items[1] = 999m;
            captures.Mutated.Add(context);
            if (captures.Fail) throw new InvalidOperationException("resolver failed after editing its snapshot");
            return Task.FromResult<IReadOnlyList<Assignee>>([new(AssigneeKind.User, "bob")]);
        }
    }

    private sealed class ObservingResolver(Captures captures) : IApproverResolver
    {
        public string Type => "Observing";
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        {
            AssertOriginal(context);
            captures.Observed.Add(context);
            return Task.FromResult<IReadOnlyList<Assignee>>([new(AssigneeKind.User, "carol")]);
        }
    }

    private static void AssertOriginal(ApproverResolveContext context)
    {
        Assert.Equal(12.5m, Assert.IsType<decimal>(context.FormData["AMOUNT"]));
        Assert.True(Assert.IsType<bool>(context.FormData["enabled"]));
        Assert.Null(context.FormData["missing"]);
        Assert.Equal("original", context.FormData["title"]);
        Assert.Equal(1e100, Assert.IsType<double>(context.FormData["large"]));
        dynamic person = Assert.IsType<ExpandoObject>(context.FormData["person"]);
        Assert.Equal("alice", (string)person.name);
        var tags = Assert.IsType<object?[]>(person.tags);
        Assert.Equal("first", tags[0]);
        dynamic tag = Assert.IsType<ExpandoObject>(tags[1]);
        Assert.Equal("nested", (string)tag.code);
        var items = Assert.IsType<object?[]>(context.FormData["items"]);
        dynamic item = Assert.IsType<ExpandoObject>(items[0]);
        Assert.Equal("bob", (string)item.owner.name);
        Assert.Equal(2m, Assert.IsType<decimal>(items[1]));
        Assert.Equal(Json, context.Instance.FormDataJson);
    }

    private static string Flow()
    {
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "Mutating", ""));
        // 重复同一个 resolver 也必须每次获得新快照。
        flow.Nodes["review"].Approvers = [new("Mutating", "first"), new("Mutating", "again"), new("Observing", "")];
        flow.Nodes["review"].Next = "condition";
        flow.Nodes["condition"] = new() { Type = NodeType.Condition, Name = "Original form",
            Branches = [new(1, "person.name == \"alice\"", "end"), new(99, null, "wrong")] };
        flow.Nodes["wrong"] = new() { Type = NodeType.Approval, Name = "Wrong branch", Next = "end",
            Approvers = [new("User", "unexpected")] };
        return flow.ToJson();
    }

    [Theory]
    [InlineData("set")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    public async Task Resolvers_ReceiveReadOnlyRootsAndIndependentNestedSnapshots(string mutation)
    {
        var captures = new Captures { Mutation = mutation };
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton(captures);
            b.AddApproverResolver<MutatingResolver>().AddApproverResolver<ObservingResolver>();
        });
        await host.PublishAsync("doc", Flow());
        var instance = await host.StartAsync("doc", "one", "alice", Json);
        Assert.Equal(2, captures.Mutated.Count);
        var observed = Assert.Single(captures.Observed);
        foreach (var mutated in captures.Mutated)
        {
            Assert.NotSame(mutated.FormData, observed.FormData);
            Assert.NotSame(mutated.FormData["person"], observed.FormData["person"]);
            Assert.NotSame(mutated.FormData["items"], observed.FormData["items"]);
        }
        Assert.Equal(new[] { "bob", "carol" }, instance.Tasks.Select(t => t.AssigneeValue).Order());
        Assert.Equal(Json, (await host.GetInstanceAsync(instance.Id)).FormDataJson);
        await host.ApproveAsync(instance.Tasks.Single(t => t.AssigneeValue == "bob").Id, "bob");
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        Assert.Equal(Json, saved.FormDataJson);
        AssertOriginal(observed);
    }

    [Fact]
    public async Task ResolverFailure_DoesNotPolluteTheNextOperationOrPersistFormData()
    {
        var captures = new Captures { Fail = true };
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton(captures);
            b.AddApproverResolver<MutatingResolver>().AddApproverResolver<ObservingResolver>();
        });
        await host.PublishAsync("doc", Flow());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync("doc", "failed", "alice", Json));
        Assert.Empty(captures.Observed);
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
        captures.Fail = false;
        var instance = await host.StartAsync("doc", "retry", "alice", Json);
        AssertOriginal(Assert.Single(captures.Observed));
        Assert.Equal(Json, (await host.GetInstanceAsync(instance.Id)).FormDataJson);
        Assert.Equal(new[] { "bob", "carol" }, instance.Tasks.Select(t => t.AssigneeValue).Order());
    }
}
