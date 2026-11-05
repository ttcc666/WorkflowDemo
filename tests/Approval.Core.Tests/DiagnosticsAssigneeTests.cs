using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class DiagnosticsAssigneeTests
{
    private sealed class CustomRoles : IApproverResolver
    {
        public string Type => "CustomRoles";
        public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Assignee>>([new(AssigneeKind.Role, "A"), new(AssigneeKind.Role, "B")]);
    }

    private sealed class Users : IApprovalUserDirectory, IApprovalUserStatusProvider
    {
        public HashSet<string> DirectoryInactive { get; } = [];
        public HashSet<string> StatusInactive { get; } = [];
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(!StatusInactive.Contains(userId));
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Select(id => new ApprovalUser(id, id, []) { IsActive = !DirectoryInactive.Contains(id) }).ToArray());
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("Role", false, false)]
    [InlineData("Role", true, false)]
    [InlineData("Role", true, true)]
    [InlineData("CustomRoles", true, false)]
    public async Task RoleDiagnosticReportsOnlyUnavailableRole(string resolver, bool removeA, bool removeB)
    {
        await using var host = new TestHost(build: b => b.AddApproverResolver<CustomRoles>());
        host.Identity.Add("admin", "Admin"); host.Identity.Add("a", "A"); host.Identity.Add("b", "B");
        await host.PublishAsync("roles", Flows.SingleNode("Any", resolver, "A,B"));
        var instance = await host.StartAsync("roles", "one", "starter");
        Assert.Equal(2, instance.Tasks.Count);
        if (removeA) host.Identity.UserRoles["a"].Clear();
        if (removeB) host.Identity.UserRoles["b"].Clear();
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        var expected = instance.Tasks.Where(t => t.AssigneeValue == "A" && removeA || t.AssigneeValue == "B" && removeB).Select(t => t.Id).Order();
        Assert.Equal(expected, report.Findings.Select(f => f.TaskId!.Value).Order());
        Assert.All(report.Findings, f => { Assert.Equal("unavailable_assignee", f.Code); Assert.Equal(instance.Id, f.InstanceId); });
    }

    [Theory]
    [InlineData("self")]
    [InlineData("directory")]
    [InlineData("status")]
    [InlineData("history")]
    [InlineData("different-members")]
    public async Task RoleDiagnosticUsesExistingEligibilityConstraints(string exclusion)
    {
        var users = new Users();
        await using var host = new TestHost(o => o.AllowSameUserMultipleNodes = false, b =>
        {
            b.Services.AddSingleton<IApprovalUserDirectory>(users);
            b.Services.AddSingleton<IApprovalUserStatusProvider>(users);
        });
        host.Identity.Add("admin", "Admin"); host.Identity.Add("a", "A"); host.Identity.Add("b", "B");
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "Role", "A,B"));
        flow.Nodes["review"].RequireQualifiedAssignee = true;
        await host.PublishAsync("roles", flow.ToJson());
        var instance = await host.StartAsync("roles", "one", "starter");
        switch (exclusion)
        {
            case "self": host.Identity.UserRoles["a"].Clear(); host.Identity.Add("starter", "A"); break;
            case "directory": users.DirectoryInactive.Add("a"); break;
            case "status": users.StatusInactive.Add("a"); break;
            case "different-members":
                host.Identity.Add("a2", "A"); users.DirectoryInactive.Add("a"); users.StatusInactive.Add("a2"); break;
            case "history":
                await host.SeedStorageAsync(async db =>
                {
                    db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "previous", NodeName = "Previous",
                        AssigneeKind = AssigneeKind.User, AssigneeValue = "a", HandledBy = "a", Status = ApprovalTaskStatus.Approved,
                        CreatedAt = host.Clock.GetUtcNow().UtcDateTime });
                    await db.SaveChangesAsync();
                });
                break;
        }
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.Equal(instance.Tasks.Single(t => t.AssigneeValue == "A").Id, Assert.Single(report.Findings).TaskId);
        Assert.Equal("unavailable_assignee", report.Findings[0].Code);
    }

    [Fact]
    public async Task UserDiagnosticStillChecksTheAssignedUser()
    {
        var users = new Users();
        await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalUserStatusProvider>(users));
        host.Identity.Add("admin", "Admin");
        await host.PublishAsync("users", Flows.SingleNode("Any", "User", "a,b"));
        var instance = await host.StartAsync("users", "one", "starter");
        users.StatusInactive.Add("a");
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.Equal(instance.Tasks.Single(t => t.AssigneeValue == "a").Id, Assert.Single(report.Findings).TaskId);
        Assert.Equal("unavailable_assignee", report.Findings[0].Code);
    }
}
