using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Approval.Core.Tests;

public sealed class AssigneePreviewTests
{
    public sealed class CustomResolver : IApproverResolver
    {
        public string Type => "CustomPreview";
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Assignee>>([new(Approval.Core.Domain.AssigneeKind.User, "custom")]);
    }

    [Theory]
    [InlineData("User", "bob", "bob")]
    [InlineData("Role", "Reviewer", "bob")]
    [InlineData("FormField", "reviewer", "bob")]
    [InlineData("Initiator", "", "test-fixture")]
    [InlineData("DeptLeader", "1", "manager")]
    [InlineData("Manager", "1", "manager")]
    [InlineData("ManagerChain", "2", "manager,chief")]
    [InlineData("CustomPreview", "", "custom")]
    public async Task AllRegisteredResolverTypesUseRuntimeResolution(string type, string value, string expected)
    {
        await using var host = new TestHost(o => o.AllowInitiatorApprove = true, b => b
            .AddOrganizationProvider<FakeOrganization>().AddApproverResolver<CustomResolver>());
        host.Identity.Add("bob", "Reviewer");
        host.Organization.AddDepartment("dept", null, "manager");
        host.Organization.AddUser("test-fixture", "dept", "manager");
        host.Organization.AddUser("manager", "dept", "chief");
        var flow = FlowDefinition.Parse(Flows.SingleNode("Sequential", type, value));
        if (type == "FormField") flow.Form = JsonSerializer.Deserialize<JsonElement>("""{"fields":[{"key":"reviewer","type":"User","label":"审核人"}]}""");
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(
            new(flow.ToJson(), JsonSerializer.SerializeToElement(new { reviewer = "bob" }), "review")));
        Assert.True(result.IsValid, string.Join(";", result.Errors));
        Assert.Equal(expected.Split(','), Assert.Single(result.Steps).ResolvedAssignees.Select(a => a.Value));
        Assert.False(result.IsComplete);
    }

    private sealed class Users : IApprovalUserDirectory
    {
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Select(id => new ApprovalUser(id, id, []) { IsActive = id == "directory-active" }).ToArray());
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Status : IApprovalUserStatusProvider
    {
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(userId is "status-active" or "starter");
    }

    [Fact]
    public async Task RoleRequiresSameMemberToPassBothDirectoryAndStatus_InPreviewAndRuntime()
    {
        await using var host = new TestHost(build: b =>
        {
            b.Services.AddSingleton<IApprovalUserDirectory, Users>();
            b.Services.AddSingleton<IApprovalUserStatusProvider, Status>();
        });
        host.Identity.Add("directory-active", "Reviewer"); host.Identity.Add("status-active", "Reviewer");
        var flow = Flows.SingleNode("Any", "Role", "Reviewer", "AutoApprove");
        var preview = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(flow, JsonSerializer.SerializeToElement(new { }))));
        Assert.True(preview.IsComplete);
        var step = preview.Steps.Single(s => s.NodeId == "review");
        Assert.Empty(step.ResolvedAssignees); Assert.Single(step.ExcludedAssignees);
        Assert.All(Assert.Single(step.Assignments).Members, member => Assert.False(member.IsEligible));
        await host.PublishAsync("role", flow);
        // Runtime starter needs a valid user under the status adapter; directory governs assignees.
        var instance = await host.StartAsync("role", "1", "starter");
        Assert.Empty(instance.Tasks); Assert.Equal(Approval.Core.Domain.InstanceStatus.Approved, instance.Status);
    }

    [Fact]
    public async Task SelfApprovalAndInvalidRuleIndexAreExplicit_AndDoNotChangeDefinition()
    {
        await using var host = new TestHost();
        var json = Flows.SingleNode("Any", "Initiator", "");
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(json, JsonSerializer.SerializeToElement(new { }), "review")));
        var step = Assert.Single(result.Steps);
        Assert.False(result.IsValid); Assert.Empty(step.ResolvedAssignees);
        Assert.Equal("不允许自审", Assert.Single(Assert.Single(step.Assignments).Members).ExclusionReason);
        var invalid = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(json, JsonSerializer.SerializeToElement(new { }), "review", 5)));
        Assert.False(invalid.IsValid); Assert.Contains("规则序号无效", invalid.Errors);
    }

    private sealed class WritingResolver(TestDbContext db) : IApproverResolver
    {
        public string Type => "Writing";
        public async Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        {
            db.Add(new TestRecord { Name = "forbidden-preview-write" });
            await db.SaveChangesAsync(ct);
            return [];
        }
    }

    [Fact]
    public async Task ResolverCannotSaveHostBusinessDataDuringPreview()
    {
        await using var host = new TestHost(build: b => b.AddApproverResolver<WritingResolver>());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(sp =>
            sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(Flows.SingleNode("Any", "Writing", ""), JsonSerializer.SerializeToElement(new { })))));
        Assert.Contains("SaveChanges", failure.Message);
        var count = await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync());
        Assert.Equal(0, count);
    }

    private sealed class ResolverGate
    { public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class WaitingResolver(ResolverGate gate) : IApproverResolver
    {
        public string Type => "Waiting";
        public async Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
        {
            gate.Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
    }
    [Fact]
    public async Task InFlightResolverObservesCallerCancellation()
    {
        var gate = new ResolverGate();
        await using var host = new TestHost(build: b => { b.Services.AddSingleton(gate); b.AddApproverResolver<WaitingResolver>(); });
        using var cancellation = new CancellationTokenSource();
        var pending = host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(
            new(Flows.SingleNode("Any", "Waiting", ""), JsonSerializer.SerializeToElement(new { })), cancellation.Token));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task MissingDirectoryAndStatusAreReportedAsUnknown()
    {
        await using var host = new TestHost(build: b => b.Services.RemoveAll<IApprovalUserStatusProvider>());
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(
            new(Flows.SingleNode("Any", "User", "bob"), JsonSerializer.SerializeToElement(new { }), "review")));
        var member = Assert.Single(Assert.Single(Assert.Single(result.Steps).Assignments).Members);
        Assert.Null(member.IsEligible); Assert.Null(member.Name);
        Assert.Contains("无法验证", member.ExclusionReason);
    }

    [Fact]
    public async Task SingleRulePreviewResolvesOnlySelectedRule_WhileWholeNodeKeepsOrderAndDeduplicates()
    {
        await using var host = new TestHost();
        var flow = FlowDefinition.Parse(Flows.SingleNode("Sequential", "User", "bob"));
        flow.Nodes["review"].Approvers.Add(new("User", "carol"));
        flow.Nodes["review"].Approvers.Add(new("User", "bob"));
        var data = JsonSerializer.SerializeToElement(new { });
        var single = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(flow.ToJson(), data, "review", 1)));
        Assert.Equal("carol", Assert.Single(Assert.Single(single.Steps).ResolvedAssignees).Value);
        var whole = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(new(flow.ToJson(), data, "review")));
        Assert.Equal(new[] { "bob", "carol" }, Assert.Single(whole.Steps).ResolvedAssignees.Select(a => a.Value));
    }

    [Fact]
    public async Task QualifiedNodeWithoutDirectoryFailsBeforeCreatingInstanceOrTasks()
    {
        await using var host = new TestHost();
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
        flow.Nodes["review"].RequireQualifiedAssignee = true;
        var preview = await host.RunAsync(sp => sp.GetRequiredService<ApprovalSimulationService>().SimulateAsync(
            new(flow.ToJson(), JsonSerializer.SerializeToElement(new { }))));
        Assert.False(preview.IsValid); Assert.Contains(preview.Errors, message => message.Contains("需要注册用户目录"));
        await host.PublishAsync("qualified", flow.ToJson());
        var failure = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("qualified", "1", "alice"));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, failure.Code);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(0, await db.Set<Approval.Core.Domain.ApprovalInstance>().CountAsync());
            Assert.Equal(0, await db.Set<Approval.Core.Domain.ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<Approval.Core.Domain.ApprovalLog>().CountAsync());
        });
    }
}
