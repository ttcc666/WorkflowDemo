using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>事务提交后至少一次投递。Id 在全部重试中稳定，宿主按此 ID 幂等。</summary>
public sealed record ApprovalEvent(Guid Id, string EventType, Guid InstanceId, Guid DefinitionId,
    int DefinitionVersion, string BusinessType, string BusinessKey, InstanceStatus Status,
    string? NodeId, string? NodeName, Guid? TaskId, string OperatorId, string? Comment, DateTime OccurredAt)
{
    public IReadOnlyList<Assignee> Recipients { get; init; } = [];
    public string? DeliveryTarget { get; init; }
    public Approval.Core.Notifications.ApprovalNotificationRequest? Notification { get; init; }
}

public interface IApprovalEventHandler
{
    Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct);
}
