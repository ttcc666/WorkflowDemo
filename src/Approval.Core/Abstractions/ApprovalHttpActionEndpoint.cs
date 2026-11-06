namespace Approval.Core.Abstractions;

/// <summary>宿主允许流程动作调用的精确 HTTPS 地址。身份凭证由宿主管理。</summary>
/// <param name="Name">流程配置可引用的稳定标识。</param>
/// <param name="Address">目标 HTTPS URL。端口须为 443。</param>
/// <param name="Methods">允许的 HTTP 方法。</param>
/// <param name="AllowSynchronousRead">是否允许在审批事务中执行只读 GET。</param>
/// <param name="BearerSecret">宿主凭证别名。</param>
public sealed record ApprovalHttpActionEndpoint(string Name, Uri Address, IReadOnlySet<string> Methods,
    bool AllowSynchronousRead = false, string? BearerSecret = null);

/// <summary>根据宿主配置的别名解析 HTTP 动作的 Bearer Token。</summary>
public interface IApprovalActionSecretProvider
{
    /// <summary>取当前有效的凭证，凭证不得出现在输出、定义、Outbox 或日志中。</summary>
    ValueTask<string?> ResolveBearerTokenAsync(string name, CancellationToken ct);
}
