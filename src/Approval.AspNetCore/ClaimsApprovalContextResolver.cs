using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Approval.AspNetCore;

/// <summary>仅使用已认证 Claims；请求体和租户请求头不会参与租户解析。</summary>
public sealed class ClaimsApprovalContextResolver(IHttpContextAccessor accessor, ApprovalOptions workflow, ApprovalAspNetCoreOptions options)
    : IApprovalContextResolver
{
    public ApprovalExecutionIdentity Resolve()
    {
        var http = accessor.HttpContext;
        if (!workflow.MultiTenant)
            return new(workflow.FixedTenantId!, http is null ? null : http.GetApprovalUserId());
        if (http?.User.Identity?.IsAuthenticated != true)
            throw new ApprovalException("未认证的租户上下文") { Code = ApprovalErrorCode.Unauthorized };
        var tenants = http.User.FindAll(options.TenantClaimType).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (tenants.Length != 1)
            throw new ApprovalException("缺少唯一的可信租户 Claim") { Code = ApprovalErrorCode.Unauthorized };
        return new(tenants[0], http.GetApprovalUserId());
    }
}
