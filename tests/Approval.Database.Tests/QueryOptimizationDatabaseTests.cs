using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class QueryClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    [Fact]
    public async Task GovernanceSummary_TranslatesNullableDraftChangesAndFiltersResourcesOnEachProvider()
    {
        await using var host = await AtomicHostAsync(new AtomicFaults());
        await using (var db = fixture.CreateStorage())
        {
            foreach (var (key, draft, published) in new (string, string?, long?)[]
                { ("absent", null, null), ("changed", AtomicFlow, 1), ("equal", AtomicFlow, 2),
                  ("hidden", AtomicFlow, null), ("unpublished", AtomicFlow, null) })
                db.Add(new ApprovalDefinitionManagement
                {
                    Id = Guid.NewGuid(), Key = key, Name = key, Category = "team", DraftJson = draft,
                    DraftRevision = 2, PublishedDraftRevision = published, Revision = 3,
                    UpdatedAt = Timestamp, UpdatedBy = "admin"
                });
            await db.SaveChangesAsync();
        }
        host.GetRequiredService<AtomicPermissions>().Rule = r => r.Resource.DefinitionKey != "hidden";
        var values = await AtomicRun(host, "alice", sp =>
            sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("team"));
        Assert.Equal(new[] { "absent", "changed", "equal", "unpublished" }, values.Select(v => v.Key));
        Assert.Equal(new[] { false, true, false, true }, values.Select(v => v.HasDraftChanges));
        Assert.All(values, v =>
        {
            Assert.Equal("team", v.Category); Assert.Equal(v.Key, v.Name); Assert.Equal(3, v.Revision);
            Assert.Equal(Timestamp, v.UpdatedAt);
            foreach (var code in new[] { ApprovalPermissions.DefinitionDesign, ApprovalPermissions.DefinitionPublish, ApprovalPermissions.DefinitionDisable })
                Assert.True(v.Capabilities[code]);
        });
        var uncategorized = await AtomicRun(host, "alice", sp =>
            sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync(null, uncategorized: true));
        Assert.Equal("doc", Assert.Single(uncategorized).Key);
    }

    [Fact]
    public async Task Backlog_StreamsCaseDistinctAssigneesAndFiltersTasksOnEachProvider()
    {
        var now = Timestamp.AddHours(2);
        await using var host = await AtomicHostAsync(new AtomicFaults(), configureServices: services =>
            services.AddSingleton<TimeProvider>(new QueryClock(now)));
        await using (var db = fixture.CreateStorage())
        {
            var definition = Definition("backlog"); db.Add(definition);
            foreach (var (value, offset, due, status) in new (string, int, DateTime?, InstanceStatus)[]
                { ("bob", 0, Timestamp.AddHours(1), InstanceStatus.Running),
                  ("bob", 15, Timestamp.AddMinutes(75), InstanceStatus.Running),
                  ("BOB", 15, null, InstanceStatus.Running),
                  ("excluded", 0, now, InstanceStatus.Approved) })
            {
                var instance = Instance(); instance.DefinitionId = definition.Id; instance.DefinitionKey = definition.Key;
                instance.CreatedAt = Timestamp.AddMinutes(offset); instance.Status = status;
                db.Add(instance);
                db.Add(new ApprovalTask
                {
                    Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = value, Status = ApprovalTaskStatus.Pending,
                    CreatedAt = instance.CreatedAt, DueAt = due
                });
                if (offset == 0 && status == InstanceStatus.Running)
                    db.Add(new ApprovalTask
                    {
                        Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                        AssigneeKind = AssigneeKind.User, AssigneeValue = "cancelled", Status = ApprovalTaskStatus.Canceled,
                        CreatedAt = instance.CreatedAt, DueAt = Timestamp
                    });
            }
            await db.SaveChangesAsync();
        }
        var stats = await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new(Timestamp.AddDays(-1), Timestamp.AddDays(1), "backlog")));
        Assert.Equal(3, stats.Summary.Running); Assert.Equal(1, stats.Summary.Approved);
        Assert.Equal(new[] { "bob", "BOB" }, stats.Backlog.Select(b => b.AssigneeValue));
        var lower = stats.Backlog[0];
        Assert.Equal(AssigneeKind.User, lower.AssigneeKind);
        Assert.Equal(2, lower.Count); Assert.Equal(2, lower.Overdue); Assert.Equal(Timestamp, lower.OldestCreatedAt);
        var upper = stats.Backlog[1];
        Assert.Equal(1, upper.Count); Assert.Equal(0, upper.Overdue); Assert.Equal(Timestamp.AddMinutes(15), upper.OldestCreatedAt);
    }
}
