using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Approval.MultiTenantHost;

public sealed class TenantActor(IHttpContextAccessor http, TenantScopes scopes, TenantDirectory directory) : IApprovalContextResolver
{
    public ApprovalExecutionIdentity Resolve()
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new ApprovalException("缺少认证身份") { Code = ApprovalErrorCode.Unauthorized };
        if (!string.Equals(scopes.FromPrincipal(principal).PartitionKey, scopes.Current, StringComparison.Ordinal))
            throw new ApprovalException("认证身份与当前宿主租户不一致") { Code = ApprovalErrorCode.Forbidden };
        var users = principal.FindAll("sub").ToArray();
        if (users.Length != 1 || !directory.Require(scopes.Current).Users.Any(u => u.Id == users[0].Value && u.IsActive))
            throw new ApprovalException("当前租户用户不可用") { Code = ApprovalErrorCode.Forbidden };
        return new(users[0].Value);
    }
}

public sealed class TenantPermissions(TenantScopes scopes, TenantDirectory directory) : IApprovalPermissionProvider
{
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tenant = directory.Require(scopes.Current);
        return Task.FromResult(request.Subject.IsSystem
            ? request.Subject.ActorId == "approval-background" && request.Permission is ApprovalPermissions.OutboxDispatch or ApprovalPermissions.TaskTimeout
            : tenant.Users.Any(u => u.Id == request.Subject.ActorId && u.IsActive && u.Permissions.Contains(request.Permission)));
    }
}

public sealed class TenantUsers(TenantScopes scopes, TenantDirectory directory) : IApprovalIdentityProvider, IApprovalUserDirectory
{
    private readonly string _partition = scopes.Current;
    private TenantDirectory.TenantEntry Tenant => string.Equals(_partition, scopes.Current, StringComparison.Ordinal)
        ? directory.Require(_partition) : throw new ApprovalException("用户目录不能跨宿主租户复用") { Code = ApprovalErrorCode.Forbidden };
    private IReadOnlyList<ApprovalUser> Users => Tenant.Users.Select(u => new ApprovalUser(u.Id, u.Name, u.Roles) { IsActive = u.IsActive }).ToArray();
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(Tenant.Users.Any(u => u.Id == userId && u.IsActive && u.Roles.Contains(role)));
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Tenant.Users.Where(u => u.IsActive && u.Roles.Contains(role)).Select(u => u.Id).ToArray());
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Tenant.Users.SingleOrDefault(u => u.Id == userId && u.IsActive)?.Roles ?? []);
    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(Users.Where(u => u.IsActive && (string.IsNullOrEmpty(keyword) || u.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))).Take(take).ToArray());
    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(Users.Where(u => ids.Contains(u.Id)).ToArray());
    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Tenant.Users.Where(u => u.IsActive).SelectMany(u => u.Roles).Distinct().Order().ToArray());
}

public sealed class TenantDemoAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, TenantDirectory directory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthenticateResult.Fail("Invalid demo credential."));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(header[7..]));
        var identity = directory.DemoIdentities.FirstOrDefault(i => CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(Encoding.UTF8.GetBytes(i.Token))));
        if (identity is null) return Task.FromResult(AuthenticateResult.Fail("Invalid demo credential."));
        var claims = new[] { new Claim("sub", identity.UserId), new Claim("tenant_id", identity.TenantId) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
