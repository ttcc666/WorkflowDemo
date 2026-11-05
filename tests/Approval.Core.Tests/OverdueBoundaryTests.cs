using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class OverdueBoundaryTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, false)]
    public async Task PendingOverdueBoundaryIsConsistentAcrossQueriesStatisticsAndDiagnostics(int? seconds, bool overdue)
    {
        await using var host = new TestHost(); host.Identity.Add("admin", "Admin");
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
        flow.Nodes["review"].Timeout = seconds is null ? null : new() { Minutes = 60 };
        await host.PublishAsync("deadline", flow.ToJson());
        var instances = new List<Guid>();
        // Two tasks for the same assignee exercise both backlog dictionary branches.
        for (var index = 0; index < 2; index++) instances.Add((await host.StartAsync("deadline", index.ToString(), "alice")).Id);
        host.Clock.Advance(TimeSpan.FromSeconds(3600 - (seconds ?? 0)));
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var todos = await host.RunAsAsync("bob", sp => sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("bob", new(OverdueOnly: true)));
        var filtered = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalQueryService>().SearchInstancesAsync(new(OverdueOnly: true)));
        var stats = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalStatisticsService>().GetStatisticsAsync(new(now.AddDays(-1), now.AddDays(1))));
        var diagnostics = await host.RunAsAsync("admin", sp => sp.GetRequiredService<ApprovalDiagnosticsService>().CheckAsync());
        var count = overdue ? 2 : 0;
        Assert.Equal(count, todos.Total); Assert.Equal(count, filtered.Total);
        Assert.Equal(overdue ? instances.Order() : Enumerable.Empty<Guid>(), todos.Items.Select(t => t.InstanceId).Order());
        Assert.Equal(overdue ? instances.Order() : Enumerable.Empty<Guid>(), filtered.Items.Select(i => i.Id).Order());
        Assert.Equal(count, stats.OverdueTasks); Assert.Equal(count, Assert.Single(stats.Backlog).Overdue);
        Assert.Equal(count, diagnostics.OverdueTasks); Assert.Empty(diagnostics.Findings);
    }

    [Fact]
    public async Task CompletedAtDeadlineKeepsHistoricalStatisticsMeaning()
    {
        await using var host = new TestHost();
        var flow = FlowDefinition.Parse(Flows.SingleNode("Any", "User", "bob"));
        flow.Nodes["review"].Timeout = new() { Minutes = 60 };
        await host.PublishAsync("deadline", flow.ToJson());
        var instance = await host.StartAsync("deadline", "one", "alice");
        host.Clock.Advance(TimeSpan.FromHours(1)); await host.ApproveMyTaskAsync("bob");
        var saved = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(saved.Tasks.Single().DueAt, saved.Tasks.Single().CompletedAt);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var stats = await host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>().GetStatisticsAsync(new(now.AddDays(-1), now.AddDays(1))));
        Assert.Equal(0, stats.OverdueTasks); Assert.Equal(0, Assert.Single(stats.Nodes).Overdue); Assert.Empty(stats.Backlog);
    }
}
