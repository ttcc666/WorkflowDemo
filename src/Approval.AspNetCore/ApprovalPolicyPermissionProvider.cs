using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore;

public sealed class ApprovalPolicyPermissionProvider(IHttpContextAccessor accessor, ApprovalAspNetCoreOptions options) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true;
        var http = accessor.HttpContext;
        if (http is not null && http.RequestServices.GetRequiredService<IApprovalCurrentUser>().GetUserId(http) != request.Subject.ActorId) http = null;
        if (options.PermissionPolicies.TryGetValue(request.Permission, out var policy))
        {
            if (http is null) return false;
            return (await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, request, policy)).Succeeded;
        }
        return false;
    }
}
