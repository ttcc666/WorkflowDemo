namespace Approval.Core.Domain;

public enum InstanceStatus { Running, Approved, Rejected, Withdrawn, Terminated }

public enum ApprovalTaskStatus
{
    /// <summary>依次审批中尚未轮到。</summary>
    Waiting,
    Pending,
    Approved,
    Rejected,
    /// <summary>或签时被他人处理、或流程结束时被取消。</summary>
    Canceled,
    /// <summary>已转交给他人。</summary>
    Transferred,
    /// <summary>前加签：等待加签人审批完成后恢复为待处理。</summary>
    Suspended
}

public enum ApprovalTaskKind
{
    /// <summary>节点审批人的普通任务。</summary>
    Approval,
    /// <summary>退回发起人后，发起人重新提交的任务。</summary>
    Resubmit,
    /// <summary>前加签：加签人先审，完成后回到原审批人。</summary>
    AddSignBefore,
    /// <summary>后加签：原审批人已同意，加签人审批后节点才能通过。</summary>
    AddSignAfter,
    /// <summary>并加签：与原审批人同时审批，节点通过前必须同意。</summary>
    AddSignParallel
}

public enum AssigneeKind
{
    User,
    /// <summary>角色任务（候选组）：角色内任意一人可处理，处理时实时校验角色。</summary>
    Role
}

public enum ApprovalAction
{
    Submit,
    Approve,
    Reject,
    Withdraw,
    Transfer,
    AutoApprove,
    Cc,
    Complete,
    /// <summary>动作节点执行了一个步骤。</summary>
    Action,
    /// <summary>超时提醒 / 催办。</summary>
    Remind,
    TimeoutApprove,
    TimeoutReject,
    /// <summary>驳回并退回到之前的节点或发起人，流程继续。</summary>
    Return,
    AddSign,
    /// <summary>退回发起人后重新提交。</summary>
    Resubmit,
    /// <summary>管理员终止流程。</summary>
    Terminate,
    /// <summary>管理员把待办改派给其他人（含离职交接的批量改派）。</summary>
    Reassign,
    /// <summary>审批人在节点修改了可编辑的表单字段。</summary>
    FormEdit
}

/// <summary>流程定义的一个版本。发布新版本不影响已发起的实例。所有时间字段均为 UTC。</summary>
public class ApprovalDefinition : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public required string Key { get; set; }
    public int Version { get; set; }
    public required string Name { get; set; }
    public required string DefinitionJson { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApprovalInstance : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public required string DefinitionKey { get; set; }
    public int DefinitionVersion { get; set; }

    /// <summary>业务类型，用于路由到对应的 <see cref="Abstractions.IApprovalBusinessHandler"/>。</summary>
    public required string BusinessType { get; set; }

    /// <summary>业务单据主键。</summary>
    public required string BusinessKey { get; set; }

    public string? Title { get; set; }
    public required string InitiatorId { get; set; }
    public InstanceStatus Status { get; set; }
    /// <summary>当前活动节点；并行时为多个节点 ID，以逗号分隔。</summary>
    public string? CurrentNodeId { get; set; }

    /// <summary>运行时状态（并行合并计数），见 <see cref="Engine.ExecutionState"/>。</summary>
    public string? StateJson { get; set; }

    /// <summary>退回次数；每次退回后新生成的任务记录当前轮次，用于区分节点的历次处理。</summary>
    public int Round { get; set; }

    /// <summary>表单数据快照，供条件分支和审批人解析使用。</summary>
    public string FormDataJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>乐观并发令牌，每次状态变更递增；不依赖数据库的 rowversion，兼容所有 EF Core 提供程序。</summary>
    public long ConcurrencyStamp { get; set; }

    /// <summary>已写入的审批轨迹条数，用于给 <see cref="ApprovalLog.Sequence"/> 编号。</summary>
    public int LogSequence { get; set; }

    public List<ApprovalTask> Tasks { get; set; } = [];
    public List<ApprovalLog> Logs { get; set; } = [];
}

public class ApprovalTask : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public required string NodeId { get; set; }
    public required string NodeName { get; set; }
    public AssigneeKind AssigneeKind { get; set; }

    public ApprovalTaskKind Kind { get; set; }

    /// <summary>加签任务的发起任务。</summary>
    public Guid? ParentTaskId { get; set; }

    /// <summary>生成任务时实例的 <see cref="ApprovalInstance.Round"/>。</summary>
    public int Round { get; set; }

    /// <summary>用户 ID 或角色名，取决于 <see cref="AssigneeKind"/>。</summary>
    public required string AssigneeValue { get; set; }

    public ApprovalTaskStatus Status { get; set; }

    /// <summary>依次审批的顺序。</summary>
    public int Order { get; set; }

    /// <summary>实际处理人（角色任务时为角色内的具体用户）。</summary>
    public string? HandledBy { get; set; }

    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>处理截止时间（节点设置了时限时）。</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>下一次超时处理（提醒 / 自动处理）的时间，为空表示无需再处理；超时扫描按此字段查询。</summary>
    public DateTime? NextRemindAt { get; set; }

    public int RemindCount { get; set; }
    public DateTime? LastRemindedAt { get; set; }
}

/// <summary>抄送记录：抄送节点的每个接收人一条，用于“抄送我的”列表与查看权限判断。角色抄送在查看时按角色成员判断。</summary>
public class ApprovalCarbonCopy : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public required string NodeId { get; set; }
    public AssigneeKind AssigneeKind { get; set; }

    /// <summary>用户 ID 或角色名，取决于 <see cref="AssigneeKind"/>。</summary>
    public required string AssigneeValue { get; set; }

    /// <summary>抄送时实例的 <see cref="ApprovalInstance.Round"/>（退回后再次经过抄送节点会新增记录）。</summary>
    public int Round { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>表单审批的草稿：不校验必填，只做结构规范化；提交时删除。</summary>
public class ApprovalDraft : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string DefinitionKey { get; set; }
    public string FormDataJson { get; set; } = "{}";
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>审批轨迹，只追加不修改。</summary>
public class ApprovalLog : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }

    /// <summary>实例内的顺序号；同一次操作写入的多条轨迹时间可能相同，排序以此为准。</summary>
    public int Sequence { get; set; }

    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public ApprovalAction Action { get; set; }
    public required string OperatorId { get; set; }
    public string? Comment { get; set; }

    /// <summary>结构化的附加信息（JSON），如修改表单时的字段差异 <c>[{field, label, before, after}]</c>；轨迹文本只写字段名。</summary>
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }
}
