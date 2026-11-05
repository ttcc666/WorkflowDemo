using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Tests;

public sealed class StartDefinitionSelectionTests
{
    private static Task<T> Write<T>(TestHost host, Func<IServiceProvider, Task<T>> action) => host.RunAsAsync("admin", sp =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("selection.test", "doc", Guid.NewGuid().ToString("N"), null, ApprovalPermissions.DefinitionPublish),
            (fresh, _) => action(fresh)));

    [Theory]
    [InlineData("active-history", 1, "bob")]
    [InlineData("explicit-history", 1, "bob")]
    [InlineData("explicit-current", 2, "carol")]
    public async Task Preflight_AndStartSelectSameActiveOrExplicitVersion(string mode, int expectedVersion, string expectedUser)
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var first = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("doc"));
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "carol"));
        var second = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("doc"));
        if (mode != "explicit-history")
        {
            var state = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
            await Write(host, sp => sp.GetRequiredService<ApprovalDefinitionService>().SelectVersionAsync("doc", first!.Id, state!.Revision));
        }
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, mode, "alice")
        { DefinitionId = mode == "explicit-history" ? first!.Id : mode == "explicit-current" ? second!.Id : null };
        var nodeId = FlowDefinition.Parse(first!.DefinitionJson).Nodes.Values.Single(n => n.Type == NodeType.Approval).Id;
        var users = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, nodeId));
        Assert.Equal([expectedUser], users);
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
        var actual = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { OperationId = "start" }));
        Assert.Equal(expectedVersion, actual.DefinitionVersion); Assert.Equal(expectedUser, Assert.Single(actual.Tasks).AssigneeValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preflight_AndStartRejectDisabledFlowWithSameError(bool explicitVersion)
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var state = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        await Write(host, sp => sp.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, state!.Revision));
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "disabled", "alice")
        { DefinitionId = explicitVersion ? state!.ActiveDefinitionId : null };
        var nodeId = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob")).Nodes.Values
            .Single(n => n.Type == NodeType.Approval).Id;
        var preview = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp =>
            sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, nodeId)));
        var actual = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp =>
            sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { OperationId = "disabled" })));
        Assert.Equal(actual.Code, preview.Code); Assert.Equal(actual.Message, preview.Message); Assert.Equal(ApprovalErrorCode.InvalidOperation, preview.Code);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().Where(o => o.ActorId == "alice").ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync()); Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
        });
    }

    [Fact]
    public async Task Start_OriginalKeyReplaysBoundVersionAfterNewPublicationAndDisable()
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "bob"));
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "replay", "alice") { OperationId = "stable" };
        var first = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        await host.PublishAsync("doc", Flows.SingleNode("Any", "User", "carol"));
        var state = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("doc"));
        await Write(host, sp => sp.GetRequiredService<ApprovalDefinitionService>().SetEnabledAsync("doc", false, state!.Revision));
        var replay = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request));
        Assert.Equal(first.Id, replay.Id); Assert.Equal(first.DefinitionId, replay.DefinitionId); Assert.Equal(1, replay.DefinitionVersion);
        Assert.Equal("bob", Assert.Single(replay.Tasks).AssigneeValue);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); Assert.Single(await db.Set<ApprovalInstance>().ToListAsync());
            var operation = Assert.Single(await db.Set<ApprovalOperation>().Where(o => o.ActorId == "alice").ToListAsync());
            Assert.Equal(first.DefinitionId, operation.DefinitionId);
            Assert.Single(await db.Set<ApprovalLog>().Where(l => l.Action == ApprovalAction.Submit).ToListAsync());
        });
    }

    private sealed class PreparedResolver : IApproverResolver, IApprovalLifecycleHandler
    {
        public string Type => "Prepared";
        public List<ApproverResolveContext> Inputs { get; } = [];
        public int Preparations { get; private set; }
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        { Inputs.Add(context); return Task.FromResult<IReadOnlyList<Assignee>>([new(AssigneeKind.User, (string)context.FormData["preparedUser"]!)]); }
        public Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext context, CancellationToken ct)
        { Preparations++; return Task.FromResult(new ApprovalStartPreparation(context.Title, context.FormDataJson)); }
    }

    [Fact]
    public async Task Preflight_PreservesPreparedVariablesAndContextWithoutRunningLifecycle()
    {
        var resolver = new PreparedResolver();
        await using var host = new TestHost(build: b =>
        { b.Services.AddSingleton<IApproverResolver>(resolver); b.Services.AddSingleton<IApprovalLifecycleHandler>(resolver); });
        var flow = Flows.SingleNode("Any", "Prepared", "preparedUser"); await host.PublishAsync("doc", flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "prepared", "alice") { FormData = new { preparedUser = "bob", amount = 7 } };
        var node = FlowDefinition.Parse(flow).Nodes.Values.Single(n => n.Type == NodeType.Approval);
        var users = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, node.Id));
        Assert.Equal(["bob"], users); Assert.Equal(0, resolver.Preparations);
        var actual = await host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(request with { OperationId = "prepared" }));
        Assert.Equal("bob", Assert.Single(actual.Tasks).AssigneeValue); Assert.Equal(1, resolver.Preparations);
        Assert.Equal(2, resolver.Inputs.Count);
        Assert.All(resolver.Inputs, input =>
        {
            Assert.Equal(request.BusinessType, input.Instance.BusinessType); Assert.Equal(request.BusinessKey, input.Instance.BusinessKey);
            Assert.Equal("alice", input.Instance.InitiatorId); Assert.Equal(actual.DefinitionId, input.Instance.DefinitionId);
            Assert.Equal("bob", input.FormData["preparedUser"]); Assert.Equal(7m, input.FormData["amount"]); Assert.Equal(node.Id, input.Node.Id);
        });
    }
}
