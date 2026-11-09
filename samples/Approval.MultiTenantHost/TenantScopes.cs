using System.Security.Claims;
using Approval.Core;
using Approval.Core.Abstractions;

namespace Approval.MultiTenantHost;

/// <summary>宿主自己的目录；Core 不持有租户、用户或授权配置。</summary>
public sealed class TenantDirectory
{
    public TenantDirectory(IConfiguration configuration)
    {
        Tenants = configuration.GetSection("Directory:Tenants").Get<TenantEntry[]>() ?? [];
        DemoIdentities = configuration.GetSection("Authentication:DemoIdentities").Get<DemoIdentity[]>() ?? [];
        if (Tenants.Length == 0 || Tenants.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != Tenants.Length
            || Tenants.Any(t => string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 64 || t.Users.Length == 0
                || t.Users.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != t.Users.Length
                || t.Users.Any(u => string.IsNullOrWhiteSpace(u.Id) || u.Permissions.Any(p => !ApprovalPermissions.All.Contains(p)
                    || p.StartsWith("approval.system.", StringComparison.Ordinal)))))
            throw new InvalidOperationException("请配置唯一的宿主租户、用户及人工权限。");
    }

    public TenantEntry[] Tenants { get; }
    public DemoIdentity[] DemoIdentities { get; }
    public TenantEntry Require(string id) => Tenants.SingleOrDefault(t => t.Id == id && t.IsActive)
        ?? throw new ApprovalException("宿主租户不可用") { Code = ApprovalErrorCode.Forbidden };

    public void ValidateDemoIdentities()
    {
        if (DemoIdentities.Length == 0 || DemoIdentities.Select(i => i.Token).Distinct(StringComparer.Ordinal).Count() != DemoIdentities.Length
            || DemoIdentities.Any(i => i.Token.Length < 32 || !Require(i.TenantId).Users.Any(u => u.Id == i.UserId && u.IsActive)))
            throw new InvalidOperationException("Demo 凭据必须唯一且对应可用租户中的用户。");
    }

    public sealed class TenantEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public UserEntry[] Users { get; set; } = [];
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
        public string TenantId { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}

/// <summary>不可变范围快照覆盖实际异步调用链；不能从请求 Header 或 DTO 设置租户。</summary>
public sealed class TenantScopes(TenantDirectory directory) : IApprovalHostScopeProvider
{
    private readonly TenantDirectory _directory = directory;
    private readonly AsyncLocal<string?> _current = new();
    public string Current => _current.Value ?? throw new ApprovalException("缺少可信宿主租户上下文") { Code = ApprovalErrorCode.Forbidden };
    public IApprovalHostScope Capture() => ForTenant(Current);
    public IApprovalHostScope ForTenant(string id) => new Scope(this, _directory.Require(id).Id);
    public Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<IApprovalHostScope>>(_directory.Tenants.Where(t => t.IsActive).Select(t => ForTenant(t.Id)).ToArray());
    }
    public IApprovalHostScope FromPrincipal(ClaimsPrincipal principal)
    {
        var tenants = principal.FindAll("tenant_id").ToArray();
        var users = principal.FindAll("sub").ToArray();
        if (principal.Identity?.IsAuthenticated != true || tenants.Length != 1 || users.Length != 1)
            throw new ApprovalException("认证身份必须包含唯一的 sub 与 tenant_id") { Code = ApprovalErrorCode.Forbidden };
        var tenant = _directory.Require(tenants[0].Value);
        if (!tenant.Users.Any(u => u.Id == users[0].Value && u.IsActive))
            throw new ApprovalException("用户不属于当前租户或已停用") { Code = ApprovalErrorCode.Forbidden };
        return ForTenant(tenant.Id);
    }
    private sealed class Scope(TenantScopes owner, string partition) : IApprovalHostScope
    {
        public string PartitionKey => partition;
        public IDisposable Enter()
        {
            owner._directory.Require(partition);
            var previous = owner._current.Value;
            owner._current.Value = partition;
            return new Restore(owner, previous);
        }
    }
    private sealed class Restore(TenantScopes owner, string? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}
