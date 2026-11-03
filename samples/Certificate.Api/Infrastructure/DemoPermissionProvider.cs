using Approval.Core.Abstractions;
namespace Certificate.Api.Infrastructure;
/// <summary>仅由 DemoMode 显式启用；生产宿主必须提供自己的权限实现。</summary>
public sealed class DemoPermissionProvider(IApprovalIdentityProvider identity) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true;
        if (!ApprovalPermissions.IsManagement(request.Permission) && !ApprovalPermissions.IsDesigner(request.Permission)) return true;
        return await identity.IsInRoleAsync(request.Subject.ActorId, "Admin", ct);
    }
}
