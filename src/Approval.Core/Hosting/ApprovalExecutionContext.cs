using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Hosting;

public sealed class ApprovalExecutionContext(IApprovalContextResolver? resolver = null) : IApprovalSystemActorContext
{
    private ApprovalExecutionIdentity? _identity;
    private ApprovalExecutionIdentity Identity => _identity ??= resolver?.Resolve()
        ?? throw new ApprovalException("缺少可信操作人上下文") { Code = ApprovalErrorCode.Unauthorized };
    public string ActorId => !string.IsNullOrWhiteSpace(Identity.ActorId) ? Identity.ActorId
        : throw new ApprovalException("缺少可信操作人") { Code = ApprovalErrorCode.Unauthorized };
    public bool IsSystem => Identity.IsSystem;
    public IReadOnlySet<string> AllowedPermissions { get; private set; } = new HashSet<string>();
    internal void InitializeTrusted(ApprovalExecutionIdentity identity, IReadOnlyCollection<string> permissions)
    {
        if (_identity is not null) throw new InvalidOperationException("审批身份已经初始化，不能切换作用域身份");
        if (string.IsNullOrWhiteSpace(identity.ActorId) || permissions.Any(p => !ApprovalPermissions.All.Contains(p)))
            throw new ArgumentException("无效操作人或权限码");
        _identity = identity; AllowedPermissions = permissions.ToHashSet(StringComparer.Ordinal);
    }
    public void InitializeSystem(string actorId, IReadOnlyCollection<string> permissions) => InitializeTrusted(new(actorId, true), permissions);
    internal void InitializeBackground() => InitializeSystem("approval-background",
        [ApprovalPermissions.OutboxDispatch, ApprovalPermissions.TaskTimeout]);
}
public sealed class ApprovalScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    internal ApprovalScope(AsyncServiceScope scope) => _scope = scope;
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;
    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}
internal sealed class ApprovalScopeFactory(IServiceScopeFactory scopes) : IApprovalScopeFactory
{
    public async Task<ApprovalScope> CreateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var scope = scopes.CreateAsyncScope();
        try { scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeBackground(); return new(scope); }
        catch { await scope.DisposeAsync(); throw; }
    }
}
