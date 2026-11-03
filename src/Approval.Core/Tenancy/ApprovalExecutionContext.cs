using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Tenancy;

public static class ApprovalTenantIds
{
    /// <summary>稳定、不透明的 ASCII 小写标识，避免数据库排序规则和存储路径歧义；不做静默归一化。</summary>
    public static string Validate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ApprovalException("租户标识须为 1–64 位小写字母、数字、点、下划线或连字符") { Code = ApprovalErrorCode.Unauthorized };
        return value;
    }
}

public sealed class ApprovalExecutionContext(ApprovalOptions options, IApprovalContextResolver? resolver = null)
    : IApprovalTenantContext, IApprovalSystemActorContext
{
    private ApprovalExecutionIdentity? _identity;
    private ApprovalExecutionIdentity Identity
    {
        get
        {
            if (_identity is not null) return _identity;
            var identity = resolver?.Resolve() ?? (options.MultiTenant
                ? throw new ApprovalException("缺少可信租户及操作人上下文") { Code = ApprovalErrorCode.Unauthorized }
                : new ApprovalExecutionIdentity(options.FixedTenantId ?? throw new InvalidOperationException("单租户模式须配置 FixedTenantId"), null));
            ApprovalTenantIds.Validate(identity.TenantId);
            if (options.MultiTenant && string.IsNullOrWhiteSpace(identity.ActorId))
                throw new ApprovalException("缺少可信操作人") { Code = ApprovalErrorCode.Unauthorized };
            if (!options.MultiTenant && identity.TenantId != options.FixedTenantId)
                throw new ApprovalException("租户与宿主配置不匹配") { Code = ApprovalErrorCode.Forbidden };
            return _identity = identity;
        }
    }
    public string TenantId => Identity.TenantId;
    public string ActorId => Identity.ActorId ?? throw new ApprovalException("缺少可信操作人") { Code = ApprovalErrorCode.Unauthorized };
    public bool IsSystem => Identity.IsSystem;
    private IReadOnlySet<string> _allowedPermissions = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> AllowedPermissions => _allowedPermissions;
    /// <summary>宿主可信启动/维护代码显式初始化系统主体；不可从 HTTP 输入绑定。</summary>
    internal void InitializeTrusted(ApprovalExecutionIdentity identity, IReadOnlyCollection<string> permissions)
    {
        if (_identity is not null) throw new InvalidOperationException("审批上下文已经初始化");
        if (!options.MultiTenant && identity.TenantId != options.FixedTenantId) throw new InvalidOperationException("命令租户与固定租户不一致");
        _identity = identity with { TenantId = ApprovalTenantIds.Validate(identity.TenantId) };
        _allowedPermissions = permissions.ToHashSet(StringComparer.Ordinal);
    }
    public void InitializeSystem(string tenantId, string actorId, IReadOnlyCollection<string> permissions)
    {
        if (_identity is not null) throw new InvalidOperationException("不能重置已初始化的审批身份");
        if (string.IsNullOrWhiteSpace(actorId) || permissions.Any(p => !ApprovalPermissions.All.Contains(p))) throw new ArgumentException("无效系统主体或权限码");
        if (!options.MultiTenant && tenantId != options.FixedTenantId) throw new InvalidOperationException("系统主体与固定租户不匹配");
        _identity = new(ApprovalTenantIds.Validate(tenantId), actorId, true);
        _allowedPermissions = permissions.ToHashSet(StringComparer.Ordinal);
    }
    internal void InitializeBackground(string tenantId)
    {
        if (_identity is not null) throw new InvalidOperationException("不能切换已初始化作用域的租户");
        if (!options.MultiTenant && tenantId != options.FixedTenantId) throw new InvalidOperationException("后台租户与固定租户不匹配");
        InitializeSystem(tenantId, "approval-background", [ApprovalPermissions.OutboxDispatch, ApprovalPermissions.TaskTimeout, ApprovalPermissions.FileCleanup, ApprovalPermissions.MaintenanceRun]);
    }
}

internal sealed class FixedApprovalTenantCatalog(ApprovalOptions options) : IApprovalTenantCatalog
{
    public Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (options.MultiTenant) throw new InvalidOperationException("多租户后台必须注册 IApprovalTenantCatalog");
        return Task.FromResult<IReadOnlyList<ApprovalTenant>>([new(options.FixedTenantId!)]);
    }
}

public sealed class ApprovalTenantScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    internal ApprovalTenantScope(AsyncServiceScope scope) => _scope = scope;
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;
    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}

internal sealed class ApprovalTenantScopeFactory(IServiceScopeFactory scopes) : IApprovalTenantScopeFactory
{
    public async Task<ApprovalTenantScope> CreateAsync(string tenantId, CancellationToken ct = default)
    {
        ApprovalTenantIds.Validate(tenantId);
        var scope = scopes.CreateAsyncScope();
        try
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IApprovalTenantCatalog>();
            if (!(await catalog.GetTenantsAsync(ct)).Any(t => t.Enabled && t.TenantId == tenantId))
                throw new ApprovalException("租户不存在或已停用") { Code = ApprovalErrorCode.Forbidden };
            scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeBackground(tenantId);
            return new(scope);
        }
        catch { await scope.DisposeAsync(); throw; }
    }
}
