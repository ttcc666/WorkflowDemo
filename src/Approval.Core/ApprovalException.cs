namespace Approval.Core;

/// <summary>审批业务规则错误（无权限、状态不允许、配置错误等），API 层可统一映射为 400/403/409。</summary>
public class ApprovalException(string message, Exception? inner = null) : Exception(message, inner)
{
    public ApprovalErrorCode Code { get; init; } = ApprovalErrorCode.InvalidOperation;
}

public enum ApprovalErrorCode
{
    InvalidOperation,
    NotFound,
    Forbidden,
    /// <summary>并发冲突（例如会签时多人同时提交），调用方可重新加载后重试。</summary>
    Conflict,
    InvalidDefinition
}
