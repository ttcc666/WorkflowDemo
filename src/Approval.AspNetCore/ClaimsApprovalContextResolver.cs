using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Approval.AspNetCore;

public sealed class ClaimsApprovalContextResolver(IHttpContextAccessor accessor) : IApprovalContextResolver
{
    public ApprovalExecutionIdentity Resolve() => new(accessor.HttpContext?.GetApprovalUserId());
}
