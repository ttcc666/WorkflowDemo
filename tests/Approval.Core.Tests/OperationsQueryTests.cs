using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Diagnostics;

namespace Approval.Core.Tests;

public sealed class OperationsQueryTests
{
    private sealed class Scope : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.BusinessKey != "hidden"));
    }

    [Fact]
    public async Task OverdueInstanceFilterIsScopedAndPaged_AndExcludesCurrentTasks()
    {
        await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalQueryScope, Scope>());
        host.Identity.Add("admin", "Admin"); host.Identity.Add("bob");
        var json = Flows.SingleNode("Any", "User", "bob").Replace("\"next\": \"end\"", "\"timeout\": {\"minutes\": 60}, \"next\": \"end\"");
        await host.PublishAsync("overdue", json);
        var visible = await host.StartAsync("overdue", "visible", "alice");
        await host.StartAsync("overdue", "hidden", "alice");
        host.Clock.Advance(TimeSpan.FromHours(2));
        await host.StartAsync("overdue", "current", "alice");
        var page = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalQueryService>()
            .SearchInstancesAsync(new(OverdueOnly: true), new(1, 1)));
        Assert.Equal(1, page.Total); Assert.Equal(visible.Id, Assert.Single(page.Items).Id);
        var empty = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalQueryService>()
            .SearchInstancesAsync(new(OverdueOnly: true), new(2, 1)));
        Assert.Equal(1, empty.Total); Assert.Empty(empty.Items);
        var denied = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<ApprovalQueryService>()
            .SearchInstancesAsync(new(OverdueOnly: true), new(1, 1))));
        Assert.Equal(ApprovalErrorCode.Forbidden, denied.Code);
    }

    private sealed class Status : IApprovalUserStatusProvider
    {
        public bool Available = true;
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(userId != "bob" || Available);
    }

    [Fact]
    public async Task DiagnosticFindingsCarryScopedResourceLinks_AndExposeSampleTruncation()
    {
        var status = new Status();
        await using var host = new TestHost(o => o.Background.AssignmentSampleSize = 1, b =>
        {
            b.Services.AddSingleton<IApprovalUserStatusProvider>(status);
            b.Services.AddSingleton<IApprovalQueryScope, Scope>();
        });
        host.Identity.Add("admin", "Admin");
        await host.PublishAsync("diagnostics", Flows.SingleNode("Any", "User", "bob"));
        var first = await host.StartAsync("diagnostics", "visible-1", "alice");
        var second = await host.StartAsync("diagnostics", "visible-2", "alice");
        var hidden = await host.StartAsync("diagnostics", "hidden", "alice");
        status.Available = false;
        var report = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        Assert.True(report.AssignmentSampleTruncated); Assert.Equal(2, report.RunningInstances);
        var finding = Assert.Single(report.Findings);
        Assert.Equal("unavailable_assignee", finding.Code);
        Assert.Contains(finding.InstanceId, new Guid?[] { first.Id, second.Id }); Assert.NotEqual(hidden.Id, finding.InstanceId);
        Assert.Equal(finding.ResourceId, finding.TaskId);
        Assert.Equal("diagnostics", finding.DefinitionKey); Assert.Equal(first.DefinitionId, finding.DefinitionId);
    }
}
