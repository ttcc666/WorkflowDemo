using Approval.Core.Abstractions;
namespace Approval.AspNetCore.Tests;
public sealed class ApiFixturePermissionProvider(FakeUsers users, Microsoft.AspNetCore.Http.IHttpContextAccessor accessor, ApprovalAspNetCoreOptions options) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true;
        if (options.PermissionPolicies.ContainsKey(request.Permission))
            return await new ApprovalPolicyPermissionProvider(accessor, options).AuthorizeAsync(request, ct);
        if (!ApprovalPermissions.IsManagement(request.Permission) && !ApprovalPermissions.IsDesigner(request.Permission)) return true;
        if (await users.IsInRoleAsync(request.Subject.ActorId, "Admin", ct)) return true;
        return ApprovalPermissions.IsDesigner(request.Permission) && await users.IsInRoleAsync(request.Subject.ActorId, "Designer", ct);
    }
}
