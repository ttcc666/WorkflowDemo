using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Domain;

namespace Approval.Tests.Infrastructure;

// Only for explicit system flow/validation fixture scopes.
// HTTP requests and the permission suites use the mandatory production gate.
internal sealed class FlowFixtureAuthorization : IApprovalAuthorization
{
    public static readonly FlowFixtureAuthorization Instance = new();
    public Task RequireAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> CanAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default) => Task.FromResult(true);
    public Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId = null, CancellationToken ct = default) => Task.FromResult(viewer);
    public Task<IQueryable<ApprovalInstance>> ScopeAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default) => Task.FromResult(query);
}
