using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class StatisticsReads : DbCommandInterceptor
    {
        public List<(string Sql, int[] Values)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<int>().ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StatisticsScope : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.BusinessKey != "statistics-hidden"));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task Statistics_BudgetsAreAppliedInSqlAfterScopeAndFlowFilters(bool backlog, int rows)
    {
        var reads = new StatisticsReads();
        await using var host = await AtomicHostAsync(new AtomicFaults(), configure: o => {
            o.Statistics.MaxTaskRows = backlog ? 10 : 2;
            o.Statistics.MaxBacklogRows = backlog ? 2 : 10;
        }, configureServices: services => {
            services.AddSingleton<IApprovalQueryScope, StatisticsScope>();
            services.ConfigureDbContext<AtomicContractContext>(o => o.AddInterceptors(reads));
        });
        var definition = Definition("bounded-statistics");
        await using (var db = fixture.CreateStorage())
        {
            db.Add(definition);
            for (var n = 0; n < rows + 6; n++)
            {
                var instance = Instance(); instance.DefinitionId = definition.Id; instance.DefinitionKey = definition.Key;
                if (n >= rows && n < rows + 3) instance.BusinessKey = "statistics-hidden";
                if (n >= rows + 3) instance.DefinitionKey = "another-flow";
                db.Add(instance);
                db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = ApprovalTaskStatus.Pending, CreatedAt = Timestamp });
            }
            await db.SaveChangesAsync();
        }
        reads.Commands.Clear();
        var query = backlog ? new StatisticsQuery(Timestamp.AddDays(1), Timestamp.AddDays(2), definition.Key)
            : new(Timestamp.AddDays(-1), Timestamp.AddDays(1), definition.Key);
        var result = AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalStatisticsService>().GetStatisticsAsync(query));
        if (rows > 2)
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(() => result);
            Assert.Equal(ApprovalErrorCode.StatisticsLimitExceeded, error.Code);
            Assert.Contains(backlog ? "当前待办积压" : "关联任务", error.Message);
        }
        else
        {
            var stats = await result;
            Assert.Equal(backlog ? 0 : 2, stats.Summary.Total);
            Assert.Equal(2, Assert.Single(stats.Backlog).Count);
            Assert.False(stats.Truncated);
        }
        var tasks = Assert.Single(reads.Commands, c => c.Sql.Contains("ApprovalTasks") && c.Values.Contains(ApprovalStatisticsService.MaxInstances));
        Assert.Contains(backlog ? 11 : 3, tasks.Values);
        Assert.Matches(@"(?i)LIMIT|TOP\s*\(|FETCH NEXT", tasks.Sql);
        Assert.Matches(@"(?i)JOIN\s*\(", tasks.Sql);
        if (backlog)
        {
            var pending = Assert.Single(reads.Commands, c => c.Sql.Contains("ApprovalTasks") && !c.Values.Contains(ApprovalStatisticsService.MaxInstances));
            Assert.Contains(3, pending.Values); Assert.Matches(@"(?i)LIMIT|TOP\s*\(|FETCH NEXT", pending.Sql);
        }
    }

    [Fact]
    public async Task Statistics_StreamsNodeMetricsAndOrdinalBacklogAtExactBudget()
    {
        var now = Timestamp.AddHours(2);
        await using var host = await AtomicHostAsync(new AtomicFaults(), configure: o => {
            o.Statistics.MaxTaskRows = 12; o.Statistics.MaxBacklogRows = 6;
        }, configureServices: s => s.AddSingleton<TimeProvider>(new QueryClock(now)));
        var definition = Definition("metrics");
        await using (var db = fixture.CreateStorage())
        {
            db.Add(definition);
            var instance = Instance(); instance.DefinitionId = definition.Id; instance.DefinitionKey = definition.Key;
            db.Add(instance);
            foreach (var (value, kind) in new[] { ("bob", AssigneeKind.User), ("BOB", AssigneeKind.User), ("bób", AssigneeKind.User),
                ("bob ", AssigneeKind.User), ("bob", AssigneeKind.Role), ("bob", AssigneeKind.User) })
                db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = kind, AssigneeValue = value, Status = ApprovalTaskStatus.Pending, CreatedAt = Timestamp,
                    DueAt = value == "BOB" ? null : now });
            foreach (var (status, kind, actor, minutes, due) in new[] {
                (ApprovalTaskStatus.Approved, ApprovalTaskKind.Approval, "bob", 30, 20),
                (ApprovalTaskStatus.Rejected, ApprovalTaskKind.Approval, "bob", 60, 60),
                (ApprovalTaskStatus.Approved, ApprovalTaskKind.Resubmit, "bob", 90, 20),
                (ApprovalTaskStatus.Approved, ApprovalTaskKind.Approval, ApprovalEngine.SystemOperator, 90, 20),
                (ApprovalTaskStatus.Canceled, ApprovalTaskKind.Approval, "bob", 90, 20) })
                db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = status, Kind = kind, HandledBy = actor,
                    CreatedAt = Timestamp, CompletedAt = Timestamp.AddMinutes(minutes), DueAt = Timestamp.AddMinutes(due) });
            db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = ApprovalTaskStatus.Approved, CreatedAt = Timestamp });
            await db.SaveChangesAsync();
        }
        var stats = await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new(Timestamp.AddDays(-1), Timestamp.AddDays(1), definition.Key)));
        var node = Assert.Single(stats.Nodes);
        Assert.Equal(2, node.Handled); Assert.Equal(45, node.AverageMinutes); Assert.Equal(1, node.Overdue);
        Assert.Equal(8, stats.OverdueTasks); // Three historical late completions plus five pending tasks exactly at their deadline.
        Assert.Equal(5, stats.Backlog.Count);
        Assert.Equal(2, stats.Backlog[0].Count);
        Assert.Equal("bob", stats.Backlog[0].AssigneeValue);
        Assert.All(stats.Backlog, b => { Assert.Equal(b.AssigneeValue == "BOB" ? 0 : b.Count, b.Overdue); Assert.Equal(Timestamp, b.OldestCreatedAt); });
        Assert.Contains(stats.Backlog, b => b.AssigneeKind == AssigneeKind.Role && b.AssigneeValue == "bob");
        foreach (var name in new[] { "bob", "BOB", "bób", "bob " })
            Assert.Single(stats.Backlog, b => b.AssigneeKind == AssigneeKind.User && b.AssigneeValue == name);
    }
}
