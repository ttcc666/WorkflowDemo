using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
namespace Approval.ReferenceHost;

// 宿主示例适配器，生产环境替换为自己的认证/目录/授权服务。
public sealed class ReferenceDirectory
{
    private readonly object _gate = new();
    private readonly UserEntry[] _users;
    private readonly DemoIdentity[] _demo;
    public ReferenceDirectory(IConfiguration configuration)
    {
        _users = configuration.GetSection("Directory:Users").Get<UserEntry[]>() ?? [];
        _demo = configuration.GetSection("Authentication:DemoIdentities").Get<DemoIdentity[]>() ?? [];
        if (_users.Length == 0 || _users.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != _users.Length
            || _users.Any(u => string.IsNullOrWhiteSpace(u.Id) || u.Permissions.Any(p => !ApprovalPermissions.All.Contains(p) || p.StartsWith("approval.system.", StringComparison.Ordinal))))
            throw new InvalidOperationException("Configure a unique host user directory.");
    }
    public void ValidateDemoIdentities()
    {
        if (_demo.Length == 0 || _demo.Any(i => i.Token.Length < 32 || !IsMember(i.UserId)) || _demo.Select(i => i.Token).Distinct().Count() != _demo.Length)
            throw new InvalidOperationException("Configure unique development demo tokens for active host users.");
    }
    public DemoIdentity? AuthenticateDemo(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return _demo.FirstOrDefault(i => IsMember(i.UserId) && CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(Encoding.UTF8.GetBytes(i.Token))));
    }
    public bool IsMember(string user) => _users.Any(u => u.Id == user && u.IsActive);
    public bool InRole(string user, string role) => _users.Any(u => u.Id == user && u.IsActive && u.Roles.Contains(role));
    public IReadOnlyList<ApprovalUser> Users() => _users.Select(u => new ApprovalUser(u.Id, u.Name, u.Roles) { IsActive = u.IsActive }).ToArray();
    public bool HasGrant(string user, string permission)
    { lock (_gate) return _users.Any(u => u.Id == user && u.IsActive && u.Permissions.Contains(permission)); }
    public bool ChangeGrant(string user, string permission, bool allowed)
    {
        lock (_gate)
        {
            var entry = _users.SingleOrDefault(u => u.Id == user); if (entry is null) return false;
            entry.Permissions = entry.Permissions.Where(p => p != permission).Concat(allowed ? [permission] : Array.Empty<string>()).ToArray(); return true;
        }
    }
    public sealed class UserEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string[] Roles { get; set; } = [];
        public string[] Permissions { get; set; } = [];
    }
    public sealed class DemoIdentity
    {
        public string Token { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}
public sealed class DirectoryContextResolver(IHttpContextAccessor http, ReferenceDirectory directory) : IApprovalContextResolver
{
    public ApprovalExecutionIdentity Resolve()
    {
        var principal = http.HttpContext?.User;
        var users = principal?.FindAll("sub").Select(c => c.Value).Distinct().ToArray() ?? [];
        if (principal?.Identity?.IsAuthenticated != true || users.Length != 1)
            throw new ApprovalException("Missing authenticated subject.") { Code = ApprovalErrorCode.Unauthorized };
        if (!directory.IsMember(users[0])) throw new ApprovalException("Host user is unavailable.") { Code = ApprovalErrorCode.Forbidden };
        return new(users[0]);
    }
}
public sealed class DirectoryPermissionProvider(ReferenceDirectory directory) : IApprovalPermissionProvider
{
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var subject = request.Subject;
        return Task.FromResult(subject.IsSystem
            ? subject.ActorId == "approval-background" && request.Permission is ApprovalPermissions.OutboxDispatch or ApprovalPermissions.TaskTimeout
            : !request.Permission.StartsWith("approval.system.", StringComparison.Ordinal) && directory.HasGrant(subject.ActorId, request.Permission));
    }
}
public sealed class DirectoryIdentityProvider(ReferenceDirectory directory) : IApprovalIdentityProvider, IApprovalUserDirectory
{
    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(directory.InRole(userId, role));
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users().Where(u => u.IsActive && u.Roles.Contains(role)).Select(u => u.Id).ToArray());
    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users().SingleOrDefault(u => u.Id == userId && u.IsActive)?.Roles ?? []);
    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(directory.Users().Where(u => u.IsActive && (string.IsNullOrEmpty(keyword) || u.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))).Take(take).ToArray());
    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>(directory.Users().Where(u => ids.Contains(u.Id)).ToArray());
    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(directory.Users().Where(u => u.IsActive).SelectMany(u => u.Roles).Distinct().Order().ToArray());
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
        var claims = new[] { new Claim("sub", identity.UserId), new Claim(ClaimTypes.NameIdentifier, identity.UserId) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
