using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using System.Reflection;
using System.Text.Json;

namespace Approval.Core.Tests;

public sealed class ExtensionContextTests
{
    private sealed class Captures
    {
        public ApproverResolveContext? Resolved;
        public ApprovalInstanceContext? Completed;
        public ApprovalStartPreparation? Preparation;
    }

    private sealed class SnapshotHandler(Captures captures) : IApproverResolver, IApprovalBusinessHandler, IApprovalLifecycleHandler
    {
        public string Type => "Snapshot";
        public string BusinessType => RecordingBusinessHandler.Type;
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        {
            captures.Resolved = context;
            return Task.FromResult<IReadOnlyList<Assignee>>([new(AssigneeKind.User, value)]);
        }
        public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct)
        {
            captures.Completed = instance;
            return Task.CompletedTask;
        }
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
        public Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct) =>
            Task.FromResult(captures.Preparation ?? new(instance.Title, instance.FormDataJson));
    }

    [Fact]
    public async Task ResolverAndEditSnapshots_RemainStableWhenTheInstanceCompletes()
    {
        var captures = new Captures();
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton(captures);
            b.AddApproverResolver<SnapshotHandler>().AddBusinessHandler<SnapshotHandler>();
        });
        await host.PublishAsync("snapshot", Flows.SingleNode("Any", "Snapshot", "bob"));
        var id = await host.RunAsync(sp => sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async token =>
        {
            var engine = sp.GetRequiredService<ApprovalEngine>();
            var instance = await engine.StartAsync(new("snapshot", RecordingBusinessHandler.Type, "one", "alice") { FormData = new { amount = 1 } }, token);
            await engine.ApproveWithDataAsync(Assert.Single(instance.Tasks).Id, "bob", "approved", (input, task, flow) =>
            {
                Assert.Equal(instance.Id, input.Id);
                Assert.Equal("review", task.NodeId);
                Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
                flow.Nodes["review"].Next = "review";
                return Task.FromResult<ApprovalDataEdit?>(new("{\"amount\":2}", "amount changed", "[]"));
            }, token);
            return instance.Id;
        }));
        Assert.Equal(InstanceStatus.Running, captures.Resolved!.Instance.Status);
        using var before = JsonDocument.Parse(captures.Resolved.Instance.FormDataJson);
        Assert.Equal(1, before.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("review", captures.Resolved.Node.Id);
        Assert.Equal(ApproveMode.Any, captures.Resolved.Node.Mode);
        Assert.Equal(InstanceStatus.Approved, captures.Completed!.Status);
        Assert.Equal(id, captures.Completed.Id);
        Assert.Equal("one", captures.Completed.BusinessKey);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, captures.Completed.CompletedAt);
        var saved = await host.GetInstanceAsync(id);
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        using var after = JsonDocument.Parse(saved.FormDataJson);
        Assert.Equal(2, after.RootElement.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task LifecyclePreparation_CanClearTitleAndReplaceDataWithoutReceivingAnEntity()
    {
        var captures = new Captures { Preparation = new(null, "{\"amount\":7}") };
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton(captures);
            b.AddLifecycleHandler<SnapshotHandler>();
        });
        await host.PublishAsync("snapshot", Flows.SingleNode("Any", "User", "bob"));
        var instance = await host.StartAsync("snapshot", "one", "alice", new { amount = 1 });
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Null(saved.Title);
        Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal("bob", Assert.Single(saved.Tasks).AssigneeValue);
        using var data = JsonDocument.Parse(saved.FormDataJson);
        Assert.Equal(7, data.RootElement.GetProperty("amount").GetInt32());
    }

    [Fact]
    public void ExtensionContracts_DoNotExposePersistenceEntitiesOrRuntimeState()
    {
        var visited = new HashSet<Type>();
        foreach (var contract in new[] { typeof(IApprovalBusinessHandler), typeof(IApprovalLifecycleHandler), typeof(IApproverResolver),
            typeof(IApprovalAccessPolicy), typeof(IApprovalDataProjection) })
            foreach (var method in contract.GetMethods())
            {
                Inspect(method.ReturnType);
                foreach (var parameter in method.GetParameters()) Inspect(parameter.ParameterType);
            }
        var edit = typeof(ApprovalEngine).GetMethod(nameof(ApprovalEngine.ApproveWithDataAsync))!;
        foreach (var parameter in edit.GetParameters()) Inspect(parameter.ParameterType);
        foreach (var context in new[] { typeof(ApprovalInstanceContext), typeof(ApprovalTaskContext), typeof(ApprovalLogContext) })
            Assert.DoesNotContain(context.GetProperties(), p => p.Name is "StateJson" or "ConcurrencyStamp" or "LogSequence");

        void Inspect(Type type)
        {
            if (!visited.Add(type)) return;
            Assert.False(typeof(IApprovalEntity).IsAssignableFrom(type), $"EF entity exposed by extension: {type.FullName}");
            if (type.IsArray) Inspect(type.GetElementType()!);
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Inspect(argument);
            if (type.Assembly != typeof(ApprovalEngine).Assembly || type.IsEnum) return;
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)) Inspect(property.PropertyType);
        }
    }
}
