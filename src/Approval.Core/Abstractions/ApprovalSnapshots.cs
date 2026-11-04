using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>宿主回调可读取的审批快照，不包含 EF 导航、并发令牌或内部运行态。</summary>
public sealed record ApprovalInstanceContext
{
    public Guid Id { get; init; }
    public Guid DefinitionId { get; init; }
    public required string DefinitionKey { get; init; }
    public int DefinitionVersion { get; init; }
    public required string BusinessType { get; init; }
    public required string BusinessKey { get; init; }
    public string? Title { get; init; }
    public required string InitiatorId { get; init; }
    public InstanceStatus Status { get; init; }
    public string? CurrentNodeId { get; init; }
    public int Round { get; init; }
    public string FormDataJson { get; init; } = "{}";
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>审批人规则所需的节点标识与审批方式；不暴露可变流程图。</summary>
public sealed record ApprovalNodeContext(string Id, string Name, NodeType Type, ApproveMode Mode);

/// <summary>开始回调返回完整的标题与申请数据；标题可显式为空，不返回内部实体修改。</summary>
public sealed record ApprovalStartPreparation(string? Title, string FormDataJson);

public sealed record ApprovalTaskContext
{
    public Guid Id { get; init; }
    public Guid InstanceId { get; init; }
    public required string NodeId { get; init; }
    public required string NodeName { get; init; }
    public AssigneeKind AssigneeKind { get; init; }
    public ApprovalTaskKind Kind { get; init; }
    public Guid? ParentTaskId { get; init; }
    public int Round { get; init; }
    public required string AssigneeValue { get; init; }
    public ApprovalTaskStatus Status { get; init; }
    public int Order { get; init; }
    public string? HandledBy { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? DueAt { get; init; }
    public DateTime? NextRemindAt { get; init; }
    public int RemindCount { get; init; }
    public DateTime? LastRemindedAt { get; init; }
}

public sealed record ApprovalLogContext
{
    public Guid Id { get; init; }
    public Guid InstanceId { get; init; }
    public Guid? TaskId { get; init; }
    public int Sequence { get; init; }
    public string? NodeId { get; init; }
    public string? NodeName { get; init; }
    public ApprovalAction Action { get; init; }
    public required string OperatorId { get; init; }
    public string? Comment { get; init; }
    public string? Details { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record ApprovalDataProjectionContext(ApprovalInstanceContext Instance, IReadOnlyList<ApprovalLogContext> Logs);

public sealed record ApprovalDefinitionSnapshot(Guid Id, string Key, int Version, string Name, string DefinitionJson, bool IsPublished, DateTime CreatedAt)
{
    public string? Category { get; init; }
}

internal static class ApprovalContextSnapshots
{
    internal static ApprovalInstanceContext Instance(ApprovalInstance value) => new()
    {
        Id = value.Id,
        DefinitionId = value.DefinitionId,
        DefinitionKey = value.DefinitionKey,
        DefinitionVersion = value.DefinitionVersion,
        BusinessType = value.BusinessType,
        BusinessKey = value.BusinessKey,
        Title = value.Title,
        InitiatorId = value.InitiatorId,
        Status = value.Status,
        CurrentNodeId = value.CurrentNodeId,
        Round = value.Round,
        FormDataJson = value.FormDataJson,
        CreatedAt = value.CreatedAt,
        CompletedAt = value.CompletedAt
    };

    internal static ApprovalNodeContext Node(FlowNode value) => new(value.Id, value.Name, value.Type, value.Mode);

    internal static ApprovalTaskContext Task(ApprovalTask value) => new()
    {
        Id = value.Id,
        InstanceId = value.InstanceId,
        NodeId = value.NodeId,
        NodeName = value.NodeName,
        AssigneeKind = value.AssigneeKind,
        Kind = value.Kind,
        ParentTaskId = value.ParentTaskId,
        Round = value.Round,
        AssigneeValue = value.AssigneeValue,
        Status = value.Status,
        Order = value.Order,
        HandledBy = value.HandledBy,
        Comment = value.Comment,
        CreatedAt = value.CreatedAt,
        CompletedAt = value.CompletedAt,
        DueAt = value.DueAt,
        NextRemindAt = value.NextRemindAt,
        RemindCount = value.RemindCount,
        LastRemindedAt = value.LastRemindedAt
    };

    internal static ApprovalLogContext Log(ApprovalLog value) => new()
    {
        Id = value.Id,
        InstanceId = value.InstanceId,
        TaskId = value.TaskId,
        Sequence = value.Sequence,
        NodeId = value.NodeId,
        NodeName = value.NodeName,
        Action = value.Action,
        OperatorId = value.OperatorId,
        Comment = value.Comment,
        Details = value.Details,
        CreatedAt = value.CreatedAt
    };

    internal static IReadOnlyList<ApprovalTaskContext> Tasks(IEnumerable<ApprovalTask> values) => Array.AsReadOnly(values.Select(Task).ToArray());

    internal static IReadOnlyList<ApprovalLogContext> Logs(IEnumerable<ApprovalLog> values) => Array.AsReadOnly(values.Select(Log).ToArray());

    internal static ApprovalDefinitionSnapshot Definition(ApprovalDefinition value) => new(
        value.Id, value.Key, value.Version, value.Name, value.DefinitionJson, value.IsPublished, value.CreatedAt)
    { Category = value.Category };
}
