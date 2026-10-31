namespace Approval.Core.Engine;

public sealed record StartApprovalRequest(string DefinitionKey, string BusinessType, string BusinessKey, string InitiatorId)
{
    public string? Title { get; init; }

    /// <summary>表单数据：任意可序列化对象或 JSON 字符串，用于条件分支与审批人解析。</summary>
    public object? FormData { get; init; }

    public string? Comment { get; init; }
}
