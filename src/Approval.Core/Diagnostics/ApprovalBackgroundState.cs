using System.Collections.Concurrent;

namespace Approval.Core.Diagnostics;

public sealed record ApprovalHeartbeat(string Worker, DateTime? StartedAt, DateTime? CompletedAt, bool? Succeeded);
public sealed class ApprovalBackgroundState
{
    private readonly ConcurrentDictionary<(string Tenant, string Worker), ApprovalHeartbeat> _state = new();
    private readonly ConcurrentDictionary<string, int> _positions = new();
    internal IReadOnlyList<Approval.Core.Abstractions.ApprovalTenant> Take(string worker, IReadOnlyList<Approval.Core.Abstractions.ApprovalTenant> tenants, int limit)
    {
        var enabled = tenants.Where(t => t.Enabled).OrderBy(t => t.TenantId, StringComparer.Ordinal).ToArray();
        if (enabled.Length == 0) return [];
        var position = _positions.AddOrUpdate(worker, 0, (_, old) => (old + Math.Min(limit, enabled.Length)) % enabled.Length);
        return Enumerable.Range(0, Math.Min(limit, enabled.Length)).Select(i => enabled[(position + i) % enabled.Length]).ToArray();
    }
    internal void Start(string tenant, string worker, DateTime now) { if (_state.Count < 1024 || _state.ContainsKey((tenant, worker))) _state[(tenant, worker)] = new(worker, now, null, null); }
    internal void End(string tenant, string worker, DateTime now, bool success) { if (_state.TryGetValue((tenant, worker), out var old)) _state[(tenant, worker)] = old with { CompletedAt = now, Succeeded = success }; }
    public IReadOnlyList<ApprovalHeartbeat> ForTenant(string tenant) => _state.Where(s => s.Key.Tenant == tenant).Select(s => s.Value).OrderBy(s => s.Worker).ToArray();
}
