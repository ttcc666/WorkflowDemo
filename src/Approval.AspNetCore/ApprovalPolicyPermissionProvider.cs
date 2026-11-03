using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore;

public sealed class ApprovalPolicyPermissionProvider(IHttpContextAccessor accessor, IApprovalIdentityProvider identity, ApprovalAspNetCoreOptions options) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true;
        var http = accessor.HttpContext;
        // A maintenance batch re-evaluates its persisted creator. Never substitute a different
        // user's active HTTP principal for that creator's permission-policy evaluation.
        if (http is not null && http.RequestServices.GetRequiredService<IApprovalCurrentUser>().GetUserId(http) != request.Subject.ActorId) http = null;
        if (options.PermissionPolicies.TryGetValue(request.Permission, out var policy))
        {
            if (http is null) return false;
            return (await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, request, policy)).Succeeded;
        }
        if (!ApprovalPermissions.IsManagement(request.Permission) && !ApprovalPermissions.IsDesigner(request.Permission)) return true;
        var admin = !string.IsNullOrEmpty(options.AdminPolicy)
            ? http is not null && (await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, request, options.AdminPolicy)).Succeeded
            : await identity.IsInRoleAsync(request.Subject.ActorId, options.AdminRole, ct);
        if (admin) return true;
        if (!ApprovalPermissions.IsDesigner(request.Permission)) return false;
        foreach (var role in options.DefinitionReaders)
            if (await identity.IsInRoleAsync(request.Subject.ActorId, role, ct)) return true;
        return false;
    }
}
