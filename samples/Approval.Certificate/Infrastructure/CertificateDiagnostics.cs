using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Infrastructure;

public sealed record UnstaffedTask(Guid TaskId, Guid InstanceId, string NodeName, string Problem);
public sealed record CertificateDiagnosticsResult(int PendingApplications, int FailedDeliveries, int DueDeliveries, int OverdueTasks,
    IReadOnlyList<UnstaffedTask> UnstaffedTasks, bool Truncated, DatabaseReadiness Database);

public sealed class CertificateDiagnostics(AppDbContext db, ApprovalEngine engine, CertificateDatabase database, TimeProvider clock)
{
    public async Task<CertificateDiagnosticsResult> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var instances = db.Set<ApprovalInstance>().Where(i => i.BusinessType == CertificateService.BusinessType);
        var outbox = db.Set<ApprovalOutboxMessage>().Where(m => instances.Any(i => i.Id == m.InstanceId));
        var tasks = db.Set<ApprovalTask>().Where(t => t.Status == ApprovalTaskStatus.Pending
            && instances.Any(i => i.Id == t.InstanceId && i.Status == InstanceStatus.Running));
        var count = await tasks.CountAsync(ct);
        var sample = await tasks.AsNoTracking().OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(100).ToListAsync(ct);
        var unavailable = new List<UnstaffedTask>();
        var loaded = new Dictionary<Guid, (ApprovalInstance Instance, FlowDefinition Flow)>();
        foreach (var task in sample)
        {
            if (task.Kind == ApprovalTaskKind.Resubmit) continue;
            if (!loaded.TryGetValue(task.InstanceId, out var entry))
            {
                var instance = await instances.AsNoTracking().Include(i => i.Tasks).FirstAsync(i => i.Id == task.InstanceId, ct);
                var definition = await db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
                entry = (instance, FlowDefinition.Parse(definition.DefinitionJson));
                loaded[instance.Id] = entry;
            }
            var eligible = await engine.GetEligibleApproversAsync(entry.Instance, entry.Flow.GetNode(task.NodeId), ct);
            if (eligible.Count == 0 || (task.AssigneeKind == AssigneeKind.User && !eligible.Contains(task.AssigneeValue)))
                unavailable.Add(new(task.Id, task.InstanceId, task.NodeName, "待办缺少合格的当前处理人，请核查岗位并改派"));
        }
        return new(await db.CertificateRequests.CountAsync(r => r.Status == RequestStatus.Reviewing, ct),
            await outbox.CountAsync(m => m.Status == OutboxStatus.Failed, ct),
            await outbox.CountAsync(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now, ct),
            await tasks.CountAsync(t => t.DueAt < now, ct), unavailable, count > sample.Count, await database.CheckAsync(ct));
    }
}
