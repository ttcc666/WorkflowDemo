using System.Text.Json.Serialization;

namespace Approval.Core.Domain;

public enum MaintenanceTarget { Instances, ReadMessages, SucceededOutbox, FailedOutbox, BusinessDrafts, DesignDrafts, Operations, UnboundFiles, ArchivedPayloads }
public enum MaintenanceMode { Archive, Purge }
public enum MaintenanceJobStatus { Pending, Running, Completed, Failed }
public enum FileDeletionStatus { Pending, Processing, Completed, Cancelled }

public sealed class ApprovalArchiveRecord : IApprovalEntity
{
    public Guid Id { get; set; }
    public MaintenanceTarget Target { get; set; }
    public Guid ResourceId { get; set; }
    public long SourceVersion { get; set; }
    public Guid? InstanceId { get; set; }
    public Guid? DefinitionId { get; set; }
    public string? DefinitionKey { get; set; }
    public string? BusinessType { get; set; }
    public string? BusinessKey { get; set; }
    public string? UserId { get; set; }
    public DateTime ArchivedAt { get; set; }
    public DateTime? PurgedAt { get; set; }
    public DateTime? PayloadExpiredAt { get; set; }
    [JsonIgnore] public required string SnapshotJson { get; set; }
    public required string SnapshotHash { get; set; }
    public long ConcurrencyStamp { get; set; }
}
public sealed class ApprovalMaintenanceAudit : IApprovalEntity
{
    public Guid Id { get; set; }
    public Guid? JobId { get; set; }
    public int? Batch { get; set; }
    public Guid? ResourceId { get; set; }
    public MaintenanceTarget Target { get; set; }
    public required string Action { get; set; }
    public required string ActorId { get; set; }
    public string? Reason { get; set; }
    public int Count { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class ApprovalMaintenanceJob : IApprovalEntity
{
    public Guid Id { get; set; }
    public MaintenanceTarget Target { get; set; }
    public MaintenanceMode Mode { get; set; }
    public required string ActorId { get; set; }
    public bool SystemRequested { get; set; }
    public required string PolicyHash { get; set; }
    public DateTime Cutoff { get; set; }
    public DateTime CreatedAt { get; set; }
    public MaintenanceJobStatus Status { get; set; }
    public int CompletedBatches { get; set; }
    public int Processed { get; set; }
    public string? LastErrorCode { get; set; }
    public long ConcurrencyStamp { get; set; }
}
public sealed class ApprovalRetentionHold : IApprovalEntity
{
    public Guid Id { get; set; }
    public MaintenanceTarget Target { get; set; }
    public Guid ResourceId { get; set; }
    public required string Reason { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class ApprovalFileDeletion : IApprovalEntity
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    [JsonIgnore] public required string StorageKey { get; set; }
    public FileDeletionStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
    public long ConcurrencyStamp { get; set; }
}
