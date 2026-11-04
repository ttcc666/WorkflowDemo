using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Approval.Core.Tests;

public sealed class StatisticsBudgetTests
{
    private sealed class Reads : DbCommandInterceptor
    {
        public List<(string Sql, int[] Values)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<int>().ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task Statistics_ExactBudgetReturnsCompleteResults_OverflowRejects(bool backlog, int count)
    {
        var reads = new Reads();
        await using var host = new TestHost(o => {
            o.Statistics.MaxTaskRows = backlog ? 10 : 2;
            o.Statistics.MaxBacklogRows = backlog ? 2 : 10;
        }, b => b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(reads)));
        await host.PublishAsync("budget", Flows.SingleNode("Any", "User", "bob"));
        var created = host.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < count; i++) await host.StartAsync("budget", i.ToString(), "alice");
        reads.Commands.Clear();
        // Backlog is current, even when the historical instance range is empty.
        var query = backlog ? new StatisticsQuery(created.AddDays(1), created.AddDays(2)) : new(created.AddDays(-1), created.AddDays(1));
        var result = host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>().GetStatisticsAsync(query));
        if (count > 2)
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(() => result);
            Assert.Equal(ApprovalErrorCode.StatisticsLimitExceeded, error.Code);
            Assert.Contains(backlog ? "当前待办积压" : "关联任务", error.Message);
            Assert.Contains("2", error.Message);
        }
        else
        {
            var stats = await result;
            Assert.False(stats.Truncated); Assert.Empty(stats.Nodes); Assert.Equal(0, stats.OverdueTasks);
            Assert.Equal(backlog ? 0 : count, stats.Summary.Running);
            if (count == 0) Assert.Empty(stats.Backlog);
            else { var row = Assert.Single(stats.Backlog); Assert.Equal(count, row.Count); Assert.Equal("bob", row.AssigneeValue); Assert.Equal(created, row.OldestCreatedAt); }
        }
        var taskRead = Assert.Single(reads.Commands, c => c.Sql.Contains("ApprovalTasks") && c.Values.Contains(ApprovalStatisticsService.MaxInstances));
        Assert.Contains("JOIN (", taskRead.Sql);
        Assert.Contains("LIMIT", taskRead.Sql);
        Assert.Contains(backlog ? 11 : 3, taskRead.Values);
        if (backlog)
        {
            var backlogRead = Assert.Single(reads.Commands, c => c.Sql.Contains("ApprovalTasks") && !c.Values.Contains(ApprovalStatisticsService.MaxInstances));
            Assert.Contains("LIMIT", backlogRead.Sql); Assert.Contains(3, backlogRead.Values);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(int.MaxValue, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    [InlineData(10, int.MaxValue)]
    public void Statistics_InvalidBudgetsFailRegistration(int tasks, int backlog)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddApprovalWorkflow<TestDbContext>(o => {
            o.Statistics.MaxTaskRows = tasks; o.Statistics.MaxBacklogRows = backlog;
        }));
    }

    [Fact]
    public async Task Statistics_BacklogReturnsTopTenByCountThenOldestArrival()
    {
        await using var host = new TestHost(o => o.Statistics.MaxBacklogRows = 20);
        var start = host.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 12; i++)
        {
            await host.PublishAsync("top-" + i, Flows.SingleNode("Any", "User", "reviewer-" + i));
            await host.StartAsync("top-" + i, i.ToString(), "alice");
            host.Clock.Advance(TimeSpan.FromMinutes(1));
        }
        await host.StartAsync("top-11", "extra", "alice");
        var stats = await host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new(start.AddDays(-1), start.AddDays(1))));
        Assert.Equal(13, stats.Summary.Running);
        Assert.Equal(new[] { "reviewer-11", "reviewer-0", "reviewer-1", "reviewer-2", "reviewer-3", "reviewer-4", "reviewer-5", "reviewer-6", "reviewer-7", "reviewer-8" },
            stats.Backlog.Select(b => b.AssigneeValue));
        Assert.Equal(2, stats.Backlog[0].Count); Assert.Equal(start.AddMinutes(11), stats.Backlog[0].OldestCreatedAt);
        Assert.All(stats.Backlog.Skip(1), b => Assert.Equal(1, b.Count));
    }

    [Fact]
    public async Task Statistics_InstanceCutoffWithEqualTimestampsDoesNotReadExcludedTasks()
    {
        await using var host = new TestHost(o => o.Statistics.MaxTaskRows = 1);
        await host.PublishAsync("ties", Flows.SingleNode("Any", "User", "bob"));
        var first = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(
            new("ties", "ties", "first", "alice") { InstanceId = Guid.Parse("00000000-0000-0000-0000-000000000001") }));
        var created = first.CreatedAt;
        await host.RunAsync(async sp => {
            var db = sp.GetRequiredService<TestDbContext>();
            // Offline fixture import through the connection, not an application write path.
            // Clone 100,000 instances cheaply, all at the exact same timestamp, with deterministic IDs.
            var entity = db.Model.FindEntityType(typeof(ApprovalInstance))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var columns = entity.GetProperties().Select(p => p.GetColumnName(table)!).ToArray();
            var selections = columns.Select(c => c switch {
                "Id" => "'00000000-0000-0000-0000-' || printf('%012d', n)",
                "BusinessKey" => "CAST(n AS TEXT)",
                _ => "s.\"" + c + "\""
            });
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "WITH RECURSIVE numbers(n) AS (SELECT 2 UNION ALL SELECT n+1 FROM numbers WHERE n < 100001) "
                + "INSERT INTO \"ApprovalInstances\" (" + string.Join(",", columns.Select(c => "\"" + c + "\"")) + ") "
                + "SELECT " + string.Join(",", selections) + " FROM numbers CROSS JOIN \"ApprovalInstances\" s WHERE s.\"BusinessKey\"='first'";
            Assert.Equal(100_000, await command.ExecuteNonQueryAsync());
            var task = Assert.Single(first.Tasks);
            var taskEntity = db.Model.FindEntityType(typeof(ApprovalTask))!;
            var taskTable = StoreObjectIdentifier.Table(taskEntity.GetTableName()!, taskEntity.GetSchema());
            var taskColumns = taskEntity.GetProperties().Select(p => p.GetColumnName(taskTable)!).ToArray();
            command.CommandText = "INSERT INTO \"ApprovalTasks\" (" + string.Join(",", taskColumns.Select(c => "\"" + c + "\"")) + ") SELECT "
                + string.Join(",", taskColumns.Select(c => c switch {
                    "Id" => "'10000000-0000-0000-0000-000000000001'",
                    "InstanceId" => "'00000000-0000-0000-0000-000000100001'",
                    _ => "\"" + c + "\""
                })) + " FROM \"ApprovalTasks\" WHERE \"Id\"=$source";
            var parameter = command.CreateParameter(); parameter.ParameterName = "$source"; parameter.Value = task.Id.ToString().ToUpperInvariant();
            command.Parameters.Add(parameter);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        });
        var stats = await host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new(created.AddDays(-1), created.AddDays(1))));
        Assert.True(stats.Truncated); Assert.Equal(100_000, stats.Summary.Total);
        Assert.Equal(2, Assert.Single(stats.Backlog).Count); // Backlog intentionally covers all current instances.
        Assert.Empty(stats.Nodes); Assert.Equal(0, stats.OverdueTasks);
    }
}
