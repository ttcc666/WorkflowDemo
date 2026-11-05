using Approval.Core.Abstractions;

namespace Approval.Testing;

/// <summary>Records real Outbox delivery attempts, including failures and repeated event IDs.</summary>
public sealed class ApprovalTestEvents : IApprovalEventHandler
{
    private readonly object _gate = new();
    private readonly List<ApprovalEvent> _attempts = [];
    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);

    public IReadOnlyList<ApprovalEvent> Snapshot()
    {
        lock (_gate) return _attempts.ToArray();
    }

    public void FailNext(string eventType, int attempts = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        lock (_gate) _failures[eventType] = attempts;
    }

    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _attempts.Add(approvalEvent with
            {
                Recipients = Array.AsReadOnly(approvalEvent.Recipients.ToArray()),
                Notification = approvalEvent.Notification is { } notice
                    ? notice with { Recipients = Array.AsReadOnly(notice.Recipients.ToArray()) } : null
            });
            if (!_failures.TryGetValue(approvalEvent.EventType, out var remaining) || remaining == 0) return Task.CompletedTask;
            _failures[approvalEvent.EventType] = remaining - 1;
            return Task.FromException(new InvalidOperationException($"Injected test delivery failure for '{approvalEvent.EventType}'."));
        }
    }
}
