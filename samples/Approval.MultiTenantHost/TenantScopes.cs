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
        // Keep the configured ID unchanged, but do not admit aliases that a case-insensitive
        // database could treat as the same tenant. Claims still require an exact directory match.
        if (Tenants.Length == 0 || Tenants.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Tenants.Length
            || Tenants.Any(t => string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 64 || t.Id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))))
            throw new InvalidOperationException("宿主租户标识必须为 1–64 个非空白、非控制字符，且不能仅以大小写区分租户；不会自动规范化已有标识。");
        if (Tenants.Any(t => t.Users.Length == 0
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
    private sealed record Frame(string Partition, Frame? Previous);
    private readonly AsyncLocal<Frame?> _current = new();
    public string Current => _current.Value?.Partition is { } partition ? _directory.Require(partition).Id
        : throw new ApprovalException("缺少可信宿主租户上下文") { Code = ApprovalErrorCode.Forbidden };
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
            var frame = new Frame(partition, owner._current.Value);
            owner._current.Value = frame;
            return new Restore(owner, frame);
        }
    }
    private sealed class Restore(TenantScopes owner, Frame frame) : IDisposable
    {
        public void Dispose()
        {
            // Disposal belongs to the current ExecutionContext. A shared mutable disposed flag
            // would let a child task prevent its parent from restoring the previous tenant.
            if (ReferenceEquals(owner._current.Value, frame)) { owner._current.Value = frame.Previous; return; }
            for (var active = owner._current.Value; active is not null; active = active.Previous)
                if (ReferenceEquals(active, frame))
                    throw new InvalidOperationException("宿主租户作用域必须按嵌套顺序释放");
            // The frame has already been released in this asynchronous flow.
        }
    }
}
