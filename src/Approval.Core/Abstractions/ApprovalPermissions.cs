using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

public static class ApprovalPermissions
{
    public const string SessionRead = "approval.session.read", DirectoryRead = "approval.directory.read";
    public const string InstanceRead = "approval.instance.read", InstanceReadAll = "approval.instance.read-all", InstanceReadSensitive = "approval.instance.read-sensitive", InstanceStart = "approval.instance.start", InstanceWithdraw = "approval.instance.withdraw", InstanceTerminate = "approval.instance.terminate";
    public const string TaskRead = "approval.task.read", TaskApprove = "approval.task.approve", TaskReject = "approval.task.reject", TaskReturn = "approval.task.return", TaskTransfer = "approval.task.transfer", TaskAddSign = "approval.task.add-sign", TaskResubmit = "approval.task.resubmit", TaskReassign = "approval.task.reassign", TaskReassignBulk = "approval.task.reassign-bulk";
    public const string DefinitionRead = "approval.definition.read", DefinitionReadSensitive = "approval.definition.read-sensitive", DefinitionDesign = "approval.definition.design", DefinitionValidate = "approval.definition.validate", DefinitionSimulate = "approval.definition.simulate", DefinitionPublish = "approval.definition.publish", DefinitionDisable = "approval.definition.disable";
    public const string DraftRead = "approval.draft.read", DraftWrite = "approval.draft.write", DraftDelete = "approval.draft.delete", FileRead = "approval.file.read", FileUpload = "approval.file.upload", MessageRead = "approval.message.read", MessageWrite = "approval.message.write";
    public const string OutboxRead = "approval.outbox.read", OutboxRetry = "approval.outbox.retry", StatisticsRead = "approval.statistics.read", DiagnosticsRead = "approval.diagnostics.read";
    public const string OutboxDispatch = "approval.system.outbox-dispatch", TaskTimeout = "approval.system.task-timeout";

    public static IReadOnlySet<string> All { get; } = typeof(ApprovalPermissions).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);

    public static bool IsManagement(string code) => code is InstanceReadAll or InstanceReadSensitive or InstanceTerminate or TaskReassign or TaskReassignBulk
        or DefinitionPublish or DefinitionDisable or OutboxRead or OutboxRetry or StatisticsRead or DiagnosticsRead;

    public static bool IsDesigner(string code) => code is DefinitionReadSensitive or DefinitionDesign or DefinitionValidate or DefinitionSimulate;
}

public sealed record ApprovalPermissionSubject(string ActorId, bool IsSystem);
public sealed record ApprovalResource
{
    public Guid? DefinitionId { get; init; }
    public string? DefinitionKey { get; init; }
    public Guid? InstanceId { get; init; }
    public Guid? TaskId { get; init; }
    public Guid? FileId { get; init; }
    public Guid? DraftId { get; init; }
    public Guid? OutboxId { get; init; }
    public Guid? MessageId { get; init; }
    public string? BusinessType { get; init; }
    public string? BusinessKey { get; init; }
    public string? TargetUserId { get; init; }
}
public sealed record ApprovalPermissionRequest(ApprovalPermissionSubject Subject, string Permission, ApprovalResource Resource);

public interface IApprovalPermissionProvider
{
    Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct);
}

/// <summary>仅在数据库查询中收窄范围，应用在分页及 count 前；不逐条远程授权过滤。</summary>
public interface IApprovalQueryScope
{
    Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct);
}

public interface IApprovalSystemActorContext : IApprovalActorContext
{ IReadOnlySet<string> AllowedPermissions { get; } }
