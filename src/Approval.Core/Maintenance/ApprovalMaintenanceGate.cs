using System.Collections.Concurrent;

namespace Approval.Core.Maintenance;

/// <summary>只防止同一进程内手动维护与 Worker 重叠；不是分布式锁或持久租约。</summary>
public sealed class ApprovalMaintenanceGate
{
    private readonly ConcurrentDictionary<Guid, byte> _running = new();
    internal IDisposable? TryEnter(Guid id) => _running.TryAdd(id, 0) ? new Exit(_running, id) : null;
    private sealed class Exit(ConcurrentDictionary<Guid, byte> running, Guid id) : IDisposable
    {
        public void Dispose() => running.TryRemove(id, out _);
    }
}
