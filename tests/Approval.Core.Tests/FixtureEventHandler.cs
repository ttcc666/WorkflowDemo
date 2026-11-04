using Approval.Core.Abstractions;
using Approval.Core.Notifications;
namespace Approval.Core.Tests;
public interface ITestNotificationTarget
{
    string Name { get; }
    string DisplayName { get; }
    Task SendAsync(ApprovalNotificationRequest request, CancellationToken ct);
}
public sealed class RecordingEvents
{
    public List<ApprovalEvent> All { get; } = [];
    public IEnumerable<ApprovalEvent> TaskEvents => All.Where(e => e.EventType is "TaskCreated" or "Cc" or "Completed");
}
internal sealed class FixtureEventHandler(IEnumerable<ITestNotificationTarget> targets, RecordingEvents events) : IApprovalEventHandler
{
    public async Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        if (approvalEvent.Notification is { } request)
        {
            var target = targets.LastOrDefault(t => t.Name.Equals(approvalEvent.DeliveryTarget, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("host notification target unavailable");
            await target.SendAsync(request, ct);
        }
        else events.All.Add(approvalEvent);
    }
}
