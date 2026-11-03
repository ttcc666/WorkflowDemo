using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Approval.MultiTenant.Api;

// A small stand-in for an external access service. Replace the scoped adapters below
// with the organization's directory/authorization SDK; never trust request-supplied roles.
public sealed class ReferenceDirectory
{
    private readonly object _gate = new();
    private readonly TenantEntry[] _tenants;
    private readonly DemoIdentity[] _demo;
    public ReferenceDirectory(IConfiguration configuration)
    {
        _tenants = configuration.GetSection("Directory:Tenants").Get<TenantEntry[]>() ?? [];
        _demo = configuration.GetSection("Authentication:DemoIdentities").Get<DemoIdentity[]>() ?? [];
        if (_tenants.Length == 0 || _tenants.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != _tenants.Length)
            throw new InvalidOperationException("Configure a nonempty, unique tenant directory.");
        foreach (var tenant in _tenants)
        {
            Approval.Core.Tenancy.ApprovalTenantIds.Validate(tenant.Id);
            if (tenant.Users.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != tenant.Users.Length
                || tenant.Users.Any(u => string.IsNullOrWhiteSpace(u.Id) || u.Permissions.Any(p => !ApprovalPermissions.All.Contains(p) || p.StartsWith("approval.system.", StringComparison.Ordinal))))
                throw new InvalidOperationException("Invalid directory users or permissions.");
        }
    }
    public void ValidateDemoIdentities()
    {
        if (_demo.Length == 0 || _demo.Any(i => i.Token.Length < 32 || !IsMember(i.TenantId, i.UserId))
            || _demo.Select(i => i.Token).Distinct(StringComparer.Ordinal).Count() != _demo.Length)
            throw new InvalidOperationException("Configure unique demo tokens of at least 32 characters for existing members via secrets or environment.");
    }
    public DemoIdentity? AuthenticateDemo(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return _demo.FirstOrDefault(i => CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(Encoding.UTF8.GetBytes(i.Token))));
    }
    public IReadOnlyList<ApprovalTenant> Tenants() => _tenants.Select(t => new ApprovalTenant(t.Id, t.Enabled)).ToArray();
    public bool IsMember(string tenant, string user) => _tenants.Any(t => t.Enabled && t.Id == tenant && t.Users.Any(u => u.Id == user));
    public bool InRole(string tenant, string user, string role) => Users(tenant).Any(u => u.Id == user && u.Roles.Contains(role, StringComparer.Ordinal));
    public IReadOnlyList<ApprovalUser> Users(string tenant) => _tenants.Where(t => t.Enabled && t.Id == tenant).SelectMany(t => t.Users)
        .Select(u => new ApprovalUser(u.Id, u.Name, u.Roles.ToArray())).ToArray();
    public bool HasGrant(string tenant, string user, string permission)
    {
        lock (_gate) return _tenants.Where(t => t.Enabled && t.Id == tenant).SelectMany(t => t.Users)
            .Any(u => u.Id == user && u.Permissions.Contains(permission, StringComparer.Ordinal));
    }
    public bool ChangeGrant(string tenant, string user, string permission, bool allowed)
    {
        lock (_gate)
        {
            var entry = _tenants.Where(t => t.Enabled && t.Id == tenant).SelectMany(t => t.Users).SingleOrDefault(u => u.Id == user);
            if (entry is null) return false;
            entry.Permissions = entry.Permissions.Where(p => p != permission).Concat(allowed ? [permission] : Array.Empty<string>()).ToArray();
            return true;
        }
    }
    public sealed class TenantEntry
    {
        public string Id { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public UserEntry[] Users { get; set; } = [];
    }
    public sealed class UserEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string[] Roles { get; set; } = [];
        public string[] Permissions { get; set; } = [];
    }
    public sealed class DemoIdentity
    {
        public string Token { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}

public sealed class DirectoryContextResolver(IHttpContextAccessor http, ReferenceDirectory directory) : IApprovalContextResolver
{
    public ApprovalExecutionIdentity Resolve()
    {
        var principal = http.HttpContext?.User;
        var tenants = principal?.FindAll("tenant_id").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var users = principal?.FindAll("sub").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (principal?.Identity?.IsAuthenticated != true || tenants.Length != 1 || users.Length != 1)
            throw new ApprovalException("Missing authenticated tenant or subject.") { Code = ApprovalErrorCode.Unauthorized };
        if (!directory.IsMember(tenants[0], users[0]))
            throw new ApprovalException("Tenant membership is unavailable or disabled.") { Code = ApprovalErrorCode.Forbidden };
        return new(tenants[0], users[0]);
    }
}
public sealed class DirectoryTenantCatalog(ReferenceDirectory directory) : IApprovalTenantCatalog
{
    public Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(directory.Tenants()); }
}
public sealed class DirectoryPermissionProvider(ReferenceDirectory directory) : IApprovalPermissionProvider
{
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var subject = request.Subject;
        var allowed = subject.IsSystem
            ? subject.ActorId == "approval-background" && directory.Tenants().Any(t => t.Enabled && t.TenantId == subject.TenantId)
                && request.Permission is ApprovalPermissions.OutboxDispatch or ApprovalPermissions.TaskTimeout or ApprovalPermissions.FileCleanup or ApprovalPermissions.MaintenanceRun
            : directory.HasGrant(subject.TenantId, subject.ActorId, request.Permission);
        return Task.FromResult(allowed);
    }
}
public sealed class DirectoryIdentityProvider(IApprovalTenantContext tenant, ReferenceDirectory directory) : IApprovalIdentityProvider, IApprovalUserDirectory
{
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(directory.InRole(tenant.TenantId, userId, role));
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users(tenant.TenantId).Where(u => u.Roles.Contains(role)).Select(u => u.Id).ToArray());
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users(tenant.TenantId).SingleOrDefault(u => u.Id == userId)?.Roles ?? []);
    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(directory.Users(tenant.TenantId).Where(u => string.IsNullOrEmpty(keyword) || u.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).Take(take).ToArray());
    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(directory.Users(tenant.TenantId).Where(u => ids.Contains(u.Id)).ToArray());
    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users(tenant.TenantId).SelectMany(u => u.Roles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}
public sealed class DemoAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ReferenceDirectory directory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers.Authorization.ToString();
        if (value.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || directory.AuthenticateDemo(value[7..]) is not { } identity)
            return Task.FromResult(AuthenticateResult.Fail("Invalid demo credential."));
        var claims = new[] { new Claim("sub", identity.UserId), new Claim(ClaimTypes.NameIdentifier, identity.UserId), new Claim("tenant_id", identity.TenantId) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
