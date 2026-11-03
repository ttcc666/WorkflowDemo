using Approval.Core.Abstractions;
using Approval.Core;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task Parallel_ConcurrentBranchCompletionAndReplay_CreateOnlyOneDownstreamTask()
    {
        var faults = new AtomicFaults(); await using var host = await AtomicHostAsync(faults);
        const string flow = """
            {"startNodeId":"start","nodes":{"start":{"type":"Start","next":"fork"},
              "fork":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
              "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
              "b":{"type":"Approval","approvers":[{"type":"User","value":"carol"}],"next":"join"},
              "join":{"type":"Join","next":"final"},
              "final":{"type":"Approval","approvers":[{"type":"User","value":"charlie"}],"next":"end"},"end":{"type":"End"}}}
            """;
        await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("parallel.publish", "parallel", "publish", new { flow }, ApprovalPermissions.DefinitionPublish),
            async (fresh, ct) => { await fresh.GetRequiredService<ApprovalDefinitionService>().PublishAsync("parallel", "并行", flow, ct); return true; }));
        var instanceId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("parallel.start", "one", "start", null, ApprovalPermissions.InstanceStart),
            async (fresh, ct) => (await fresh.GetRequiredService<ApprovalEngine>().StartAsync(new("parallel", AtomicBusiness.Type, "parallel-business", "alice"), ct)).Id));
        var tasks = await AtomicRun(host, "alice", sp => sp.GetRequiredService<AtomicContractContext>().Set<ApprovalTask>()
            .Where(t => t.InstanceId == instanceId).AsNoTracking().ToListAsync());
        Assert.Equal(2, tasks.Count);
        async Task Approve(ApprovalTask task)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    await AtomicRun(host, task.AssigneeValue, sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                        new("parallel.approve", task.Id.ToString(), "approve", new { task.Id }, ApprovalPermissions.TaskApprove, new() { TaskId = task.Id }),
                        async (fresh, ct) => { await fresh.GetRequiredService<ApprovalEngine>().ApproveAsync(task.Id, task.AssigneeValue, ct: ct); return true; }));
                    return;
                }
                catch (ApprovalException error) when (error.Code == ApprovalErrorCode.Conflict && attempt < 7) { }
            }
        }
        await Task.WhenAll(tasks.Select(Approve));
        await Task.WhenAll(tasks.Select(Approve));
        await AtomicRun(host, "alice", async sp =>
        {
            var db = sp.GetRequiredService<AtomicContractContext>();
            var final = Assert.Single(await db.Set<ApprovalTask>().Where(t => t.InstanceId == instanceId && t.Status == ApprovalTaskStatus.Pending).ToListAsync());
            Assert.Equal("final", final.NodeId); Assert.Equal("charlie", final.AssigneeValue);
            Assert.Equal(2, await db.Set<ApprovalLog>().CountAsync(l => l.InstanceId == instanceId && l.Action == ApprovalAction.Approve));
            Assert.Null((await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == instanceId)).StateJson);
            return true;
        });
    }
}
