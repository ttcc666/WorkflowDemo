using System.Collections.Concurrent;
namespace Approval.Core.Diagnostics;
public sealed record ApprovalHeartbeat(string Worker, DateTime? StartedAt, DateTime? CompletedAt, bool? Succeeded);
public sealed class ApprovalBackgroundState
{
    private readonly ConcurrentDictionary<string, ApprovalHeartbeat> _state = new();
    internal void Start(string worker, DateTime now) => _state[worker] = new(worker, now, null, null);
    internal void End(string worker, DateTime now, bool success)
    { if (_state.TryGetValue(worker, out var old)) _state[worker] = old with { CompletedAt = now, Succeeded = success }; }
    public IReadOnlyList<ApprovalHeartbeat> Snapshot() => _state.Values.OrderBy(s => s.Worker).ToArray();
}
