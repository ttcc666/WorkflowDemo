namespace Approval.Core.Abstractions;

/// <summary>当前作用域的可信租户；不可从业务请求体设置。</summary>
public interface IApprovalTenantContext { string TenantId { get; } }
public interface IApprovalActorContext { string ActorId { get; } bool IsSystem { get; } }
public sealed record ApprovalExecutionIdentity(string TenantId, string? ActorId, bool IsSystem = false);
/// <summary>宿主认证适配器；远程解析应在进入审批作用域之前完成。</summary>
public interface IApprovalContextResolver { ApprovalExecutionIdentity Resolve(); }
public sealed record ApprovalTenant(string TenantId, bool Enabled = true);
public interface IApprovalTenantCatalog
{
    Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct);
}
public interface IApprovalTenantScopeFactory
{
    Task<Approval.Core.Tenancy.ApprovalTenantScope> CreateAsync(string tenantId, CancellationToken ct = default);
}
/// <summary>单租户宿主和显式离线任务可使用的不可变上下文。</summary>
public sealed class FixedApprovalTenantContext(string tenantId) : IApprovalTenantContext
{
    public string TenantId { get; } = Approval.Core.Tenancy.ApprovalTenantIds.Validate(tenantId);
}
