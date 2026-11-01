namespace Approval.Core.Engine;

public sealed record StartApprovalRequest(string DefinitionKey, string BusinessType, string BusinessKey, string InitiatorId)
{
    public string? Title { get; init; }

    /// <summary>表单数据：任意可序列化对象或 JSON 字符串，用于条件分支与审批人解析。</summary>
    public object? FormData { get; init; }

    public string? Comment { get; init; }

    /// <summary>预先指定实例 ID（如需在同一事务中关联附件等数据），为空时自动生成。</summary>
    public Guid? InstanceId { get; init; }

    /// <summary>锁定已经校验过的已发布版本，避免校验与发起之间发布新版本导致规则变化。</summary>
    public Guid? DefinitionId { get; init; }
}
