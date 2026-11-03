using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Approval.Core.Diagnostics;

public sealed record ApprovalDiagnosticContribution(string Code, bool Healthy, long Count);
public interface IApprovalDiagnosticsContributor { Task<ApprovalDiagnosticContribution> CheckAsync(CancellationToken ct); }
public sealed record ApprovalDiagnosticFinding(string Code, Guid ResourceId);
public sealed record ApprovalDiagnosticReport(bool Ready, bool DataAvailable, bool SchemaManaged, int PendingMigrations, bool ModelDrift,
    int RunningInstances, int PendingOutbox, int FailedOutbox, DateTime? OldestPendingAt, int ExpiredLeases, int OverdueTasks,
    int MissingBusinessSlots, int StaleBusinessSlots, int PendingFileDeletions, bool AssignmentSampleTruncated,
    IReadOnlyList<ApprovalDiagnosticFinding> Findings, IReadOnlyList<ApprovalHeartbeat> Heartbeats,
    IReadOnlyList<ApprovalDiagnosticContribution> Contributions);

public sealed class ApprovalDiagnosticsService(ApprovalDbContextAccessor accessor, ApprovalEngine engine,
    IApprovalTenantContext tenant, ApprovalOptions options, TimeProvider clock, ApprovalBackgroundState background,
    IEnumerable<IApprovalDiagnosticsContributor> contributors)
{
    private readonly DbContext _db = accessor.Context;
    public async Task<ApprovalDiagnosticReport> CheckAsync(CancellationToken ct = default)
    {
        await accessor.Authorization.RequireAsync(ApprovalPermissions.DiagnosticsRead, ct: ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var ready = await ReadinessAsync(_db, ct);
        var managed = _db.GetService<IMigrationsAssembly>().ModelSnapshot is not null;
        var pending = 0; var drift = false;
        try { pending = managed ? (await _db.Database.GetPendingMigrationsAsync(ct)).Count() : 0; drift = managed && _db.Database.HasPendingModelChanges(); }
        catch (Exception) when (!ct.IsCancellationRequested) { ready = false; }
        if (!ready) return new(false, false, managed, pending, drift, 0, 0, 0, null, 0, 0, 0, 0, 0, false, [], background.ForTenant(tenant.TenantId), []);
        var instances = await accessor.Authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        var outbox = _db.Set<ApprovalOutboxMessage>().AsNoTracking().Where(o => o.InstanceId == null || instances.Any(i => i.Id == o.InstanceId));
        var tasks = _db.Set<ApprovalTask>().AsNoTracking().Where(t => t.Status == ApprovalTaskStatus.Pending && instances.Any(i => i.Id == t.InstanceId && i.Status == InstanceStatus.Running));
        var total = await tasks.CountAsync(ct);
        var sample = await tasks.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(options.Background.AssignmentSampleSize).ToListAsync(ct);
        var findings = new List<ApprovalDiagnosticFinding>();
        var flows = new Dictionary<Guid, (ApprovalInstance Instance, FlowDefinition Flow)>();
        foreach (var task in sample)
        {
            if (!flows.TryGetValue(task.InstanceId, out var loaded))
            {
                var instance = await instances.Include(i => i.Tasks).SingleAsync(i => i.Id == task.InstanceId, ct);
                var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().SingleAsync(d => d.Id == instance.DefinitionId, ct);
                try { loaded = (instance, FlowDefinition.Parse(definition.DefinitionJson)); flows[instance.Id] = loaded; }
                catch (ApprovalException) { findings.Add(new("invalid_definition", instance.Id)); continue; }
            }
            if (!loaded.Flow.Nodes.TryGetValue(task.NodeId, out var node)) { findings.Add(new("missing_node", task.Id)); continue; }
            var eligible = await engine.GetEligibleApproversAsync(loaded.Instance, node, ct);
            if (eligible.Count == 0 || task.AssigneeKind == AssigneeKind.User && !eligible.Contains(task.AssigneeValue)) findings.Add(new("unavailable_assignee", task.Id));
        }
        var contributions = new List<ApprovalDiagnosticContribution>();
        foreach (var contributor in contributors.Take(20))
        {
            try
            {
                using var timeout = new CancellationTokenSource(options.PermissionTimeout, clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                var item = await contributor.CheckAsync(linked.Token).WaitAsync(linked.Token);
                if (item.Count < 0 || !System.Text.RegularExpressions.Regex.IsMatch(item.Code, "^[a-z0-9._]{1,64}$")) throw new InvalidOperationException();
                contributions.Add(item);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { contributions.Add(new("contributor_unavailable", false, 0)); }
        }
        var running = instances.Where(i => i.Status == InstanceStatus.Running);
        var slots = _db.Set<ApprovalBusinessSlot>().AsNoTracking();
        var stale = slots.Where(s => ! _db.Set<ApprovalInstance>().Any(i => i.Id == s.InstanceId && i.Status == InstanceStatus.Running)
            && (!_db.Set<ApprovalInstance>().Any(i => i.Id == s.InstanceId) || instances.Any(i => i.Id == s.InstanceId)));
        return new(ready && pending == 0 && !drift, true, managed, pending, drift, await running.CountAsync(ct),
            await outbox.CountAsync(o => o.Status == OutboxStatus.Pending, ct), await outbox.CountAsync(o => o.Status == OutboxStatus.Failed, ct),
            await outbox.Where(o => o.Status == OutboxStatus.Pending).MinAsync(o => (DateTime?)o.CreatedAt, ct),
            await outbox.CountAsync(o => o.Status == OutboxStatus.Pending && o.LeaseId != null && o.NextAttemptAt <= now, ct),
            await tasks.CountAsync(t => t.DueAt != null && t.DueAt <= now, ct),
            options.SingleRunningInstancePerBusiness ? await running.CountAsync(i => !slots.Any(s => s.InstanceId == i.Id), ct) : 0,
            await stale.CountAsync(ct), await _db.Set<ApprovalFileDeletion>().CountAsync(d => d.Status == FileDeletionStatus.Pending || d.Status == FileDeletionStatus.Processing, ct),
            total > sample.Count, findings, background.ForTenant(tenant.TenantId), contributions);
    }
    public static async Task<bool> ReadinessAsync(DbContext db, CancellationToken ct = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) return false;
            _ = await db.Set<ApprovalMaintenanceJob>().AsNoTracking().AnyAsync(ct);
            if (db.GetService<IMigrationsAssembly>().ModelSnapshot is not null)
                return !db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync(ct)).Any();
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }
}
