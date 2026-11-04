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

    InvalidDefinition,

    /// <summary>未识别当前用户（未登录）。</summary>
    Unauthorized,

    /// <summary>输入校验失败，详见 <see cref="ApprovalValidationException.Errors"/>。</summary>
    Validation,

    AuthorizationUnavailable,
    OperationIdRequired,
    CommitOutcomeUnknown,
    ReplayExpired
}

/// <summary>输入校验失败（如表单数据），<see cref="Errors"/> 为“字段 → 错误信息”。</summary>
public sealed class ApprovalValidationException : ApprovalException
{
    public ApprovalValidationException(IReadOnlyDictionary<string, string[]> errors, string message = "提交的数据有误") : base(message)
    {
        Errors = errors;
        Code = ApprovalErrorCode.Validation;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
