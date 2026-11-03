using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Forms;

namespace Approval.AspNetCore.Contracts;

/// <summary>ApprovalDefinition 的 HTTP 快照；保持既有 JSON 字段与 UTC 日期表示。</summary>
public sealed record ApprovalDefinitionResponse(
    Guid Id,
    string Key,
    int Version,
    string Name,
    string DefinitionJson,
    bool IsPublished,
    DateTime CreatedAt)
{
    public static ApprovalDefinitionResponse From(ApprovalDefinition value) => new(
        value.Id, value.Key, value.Version, value.Name, value.DefinitionJson, value.IsPublished, value.CreatedAt);
}

/// <summary>ApprovalTask 的 HTTP 快照；保持既有 JSON 字段与 UTC 日期表示。</summary>
public sealed record ApprovalTaskResponse(
    Guid Id,
    Guid InstanceId,
    string NodeId,
    string NodeName,
    AssigneeKind AssigneeKind,
    ApprovalTaskKind Kind,
    Guid? ParentTaskId,
    int Round,
    string AssigneeValue,
    ApprovalTaskStatus Status,
    int Order,
    string? HandledBy,
    string? Comment,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    DateTime? DueAt,
    DateTime? NextRemindAt,
    int RemindCount,
    DateTime? LastRemindedAt)
{
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>();
    public static ApprovalTaskResponse From(ApprovalTask value) => new(
        value.Id, value.InstanceId, value.NodeId, value.NodeName, value.AssigneeKind, value.Kind, value.ParentTaskId, value.Round, value.AssigneeValue, value.Status, value.Order, value.HandledBy, value.Comment, value.CreatedAt, value.CompletedAt, value.DueAt, value.NextRemindAt, value.RemindCount, value.LastRemindedAt);
}

/// <summary>ApprovalLog 的 HTTP 快照；保持既有 JSON 字段与 UTC 日期表示。</summary>
public sealed record ApprovalLogResponse(
    Guid Id,
    Guid InstanceId,
    int Sequence,
    string? NodeId,
    string? NodeName,
    ApprovalAction Action,
    string OperatorId,
    string? Comment,
    string? Details,
    DateTime CreatedAt)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? TaskId { get; init; }
    public static ApprovalLogResponse From(ApprovalLog value) => new(
        value.Id, value.InstanceId, value.Sequence, value.NodeId, value.NodeName, value.Action, value.OperatorId, value.Comment, value.Details, value.CreatedAt) { TaskId = value.TaskId };
}

/// <summary>ApprovalMessage 的 HTTP 快照；保持既有 JSON 字段与 UTC 日期表示。</summary>
public sealed record ApprovalMessageResponse(
    Guid Id,
    string UserId,
    string Title,
    string Content,
    NotifyEvent Event,
    Guid? InstanceId,
    string? NodeId,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt)
{
    public static ApprovalMessageResponse From(ApprovalMessage value) => new(
        value.Id, value.UserId, value.Title, value.Content, value.Event, value.InstanceId, value.NodeId, value.IsRead, value.CreatedAt, value.ReadAt);
}

/// <summary>ApprovalOutboxMessage 的 HTTP 快照；保持既有 JSON 字段与 UTC 日期表示。</summary>
public sealed record ApprovalOutboxMessageResponse(
    Guid Id,
    OutboxKind Kind,
    string Target,
    Guid? InstanceId,
    string? NodeId,
    string? Description,
    string PayloadJson,
    OutboxStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTime NextAttemptAt,
    string? LastError,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    long ConcurrencyStamp)
{
    public static ApprovalOutboxMessageResponse From(ApprovalOutboxMessage value) => new(
        value.Id, value.Kind, value.Target, value.InstanceId, value.NodeId, value.Description, value.PayloadJson, value.Status, value.Attempts, value.MaxAttempts, value.NextAttemptAt, value.LastError, value.CreatedAt, value.CompletedAt, value.ConcurrencyStamp);
}

/// <summary>经过查看权限和字段权限过滤的审批详情。</summary>
public sealed record ApprovalInstanceResponse(
    Guid Id, Guid DefinitionId, string DefinitionKey, int DefinitionVersion,
    string BusinessType, string BusinessKey, string? Title, string InitiatorId,
    InstanceStatus Status, string? CurrentNodeId, int Round, string FormDataJson,
    DateTime CreatedAt, DateTime? CompletedAt, IReadOnlyList<ApprovalTaskResponse> Tasks,
    IReadOnlyList<ApprovalLogResponse> Logs, ApprovalAccessLevel Access,
    IReadOnlyList<string> HiddenFields, bool CanWithdraw)
{
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>();
    public static ApprovalInstanceResponse From(InstanceDetail value) => new(
        value.Id, value.DefinitionId, value.DefinitionKey, value.DefinitionVersion,
        value.BusinessType, value.BusinessKey, value.Title, value.InitiatorId, value.Status,
        value.CurrentNodeId, value.Round, value.FormDataJson, value.CreatedAt, value.CompletedAt,
        value.Tasks.Select(ApprovalTaskResponse.From).ToArray(), value.Logs.Select(ApprovalLogResponse.From).ToArray(),
        value.Access, value.HiddenFields, value.CanWithdraw);
}

/// <summary>当前用户及审批功能权限。</summary>
public sealed record ApprovalMeResponse(string UserId, string Name, IReadOnlyList<string> Roles,
    bool IsAdmin, bool CanViewDefinitions, bool FormsEnabled, string? FormBusinessType)
{
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>();
}
/// <summary>当前用户可处理的任务和允许编辑的字段。</summary>
public sealed record ApprovalMyTaskResponse(ApprovalTaskResponse? Task, IReadOnlyList<string> EditableFields)
{
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>();
}
/// <summary>列表计数或批量处理计数。</summary>
public sealed record CountResponse(int Count);
/// <summary>任务是否可由当前用户处理。</summary>
public sealed record CanHandleResponse(bool CanHandle);
/// <summary>流程定义校验错误和非阻塞提示。</summary>
public sealed record ApprovalValidationResponse(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
/// <summary>发布成功的流程版本。</summary>
public sealed record ApprovalPublishedResponse(Guid Id, string Key, int Version);
/// <summary>上传完成的附件元数据。</summary>
public sealed record ApprovalFileResponse(Guid Id, string Name, long Size, string ContentType);
/// <summary>保存成功的草稿。</summary>
public sealed record ApprovalDraftSavedResponse(Guid DraftId, DateTime UpdatedAt);
/// <summary>已发起的审批实例。</summary>
public sealed record ApprovalStartedResponse(Guid InstanceId);
