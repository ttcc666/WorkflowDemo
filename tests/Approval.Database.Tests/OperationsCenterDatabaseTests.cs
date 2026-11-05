using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class OperationsScope : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.BusinessKey != "hidden"));
    }

    [Fact]
    public async Task OperationsOverdueFilter_TranslatesScopeAndPaginationOnEachProvider()
    {
        var now = Timestamp.AddHours(2);
        await using var host = await AtomicHostAsync(new AtomicFaults(), configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(new QueryClock(now));
            services.AddSingleton<IApprovalQueryScope, OperationsScope>();
        });
        var expected = new List<Guid>();
        await using (var db = fixture.CreateStorage())
        {
            var definition = Definition("operations"); db.Add(definition);
            var cases = new (string Key, InstanceStatus State, ApprovalTaskStatus Task, DateTime? Due)[]
            {
                ("visible-1", InstanceStatus.Running, ApprovalTaskStatus.Pending, now.AddMinutes(-1)),
                ("visible-2", InstanceStatus.Returned, ApprovalTaskStatus.Pending, now),
                ("hidden", InstanceStatus.Running, ApprovalTaskStatus.Pending, now),
                ("current", InstanceStatus.Running, ApprovalTaskStatus.Pending, now.AddMinutes(1)),
                ("no-due", InstanceStatus.Running, ApprovalTaskStatus.Pending, null),
                ("finished", InstanceStatus.Approved, ApprovalTaskStatus.Pending, now),
                ("cancelled-task", InstanceStatus.Running, ApprovalTaskStatus.Canceled, now)
            };
            for (var index = 0; index < cases.Length; index++)
            {
                var sample = cases[index];
                var instance = Instance(); instance.DefinitionId = definition.Id; instance.DefinitionKey = definition.Key;
                instance.BusinessKey = sample.Key; instance.Status = sample.State; instance.CreatedAt = Timestamp.AddMinutes(index);
                db.Add(instance);
                db.Add(new ApprovalTask { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review",
                    AssigneeKind = AssigneeKind.User, AssigneeValue = "bob", Status = sample.Task, CreatedAt = instance.CreatedAt, DueAt = sample.Due });
                if (index < 2) expected.Add(instance.Id);
            }
            await db.SaveChangesAsync();
        }
        expected.Reverse();
        for (var page = 1; page <= 3; page++)
        {
            var result = await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalQueryService>()
                .SearchInstancesAsync(new(DefinitionKey: "operations", OverdueOnly: true), new(page, 1)));
            Assert.Equal(2, result.Total);
            Assert.Equal(expected.Skip(page - 1).Take(1), result.Items.Select(i => i.Id));
        }
    }
}
