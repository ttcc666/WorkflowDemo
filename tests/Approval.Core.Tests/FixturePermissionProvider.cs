using Approval.Core.Abstractions;
namespace Approval.Core.Tests;
public sealed class FixturePermissionProvider(FakeIdentityProvider identity) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true;
        if (!ApprovalPermissions.IsManagement(request.Permission) && !ApprovalPermissions.IsDesigner(request.Permission)) return true;
        return await identity.IsInRoleAsync(request.Subject.ActorId, "Admin", ct);
    }
}
