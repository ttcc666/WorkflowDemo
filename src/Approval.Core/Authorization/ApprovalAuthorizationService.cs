using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Authorization;

internal interface IApprovalAuthorization
{
    Task RequireAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default);
    Task<bool> CanAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default);
    Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId = null, CancellationToken ct = default);
    Task<IQueryable<ApprovalInstance>> ScopeAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default);
    Task<ApprovalResource> ResolveResourceAsync(ApprovalResource resource, CancellationToken ct) => Task.FromResult(resource);
    Task RequireRecordedAsync(string permission, ApprovalResource resource, CancellationToken ct) => RequireAsync(permission, resource, ct: ct);
}

/// <summary>宿主/HTTP 能力入口。权限 provider 不能绕过可信操作人或后台权限边界。</summary>
public sealed class ApprovalAuthorizationService
{
    internal IApprovalAuthorization Inner { get; }
    internal ApprovalAuthorizationService(IApprovalAuthorization inner) => Inner = inner;
    public Task RequireAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default) => Inner.RequireAsync(permission, resource, userId, ct);
    public Task<bool> CanAsync(string permission, ApprovalResource? resource = null, CancellationToken ct = default) => Inner.CanAsync(permission, resource, ct: ct);
    public Task<IQueryable<ApprovalInstance>> ScopeInstancesAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default) => Inner.ScopeAsync(query, ct);
    public async Task<IReadOnlyDictionary<string, bool>> CapabilitiesAsync(ApprovalResource? resource = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var codes = ApprovalPermissions.All.Where(c => !c.StartsWith("approval.system.", StringComparison.Ordinal));
        if (resource?.TaskId is not null) codes = codes.Where(c => c.StartsWith("approval.task.", StringComparison.Ordinal));
        else if (resource?.InstanceId is not null) codes = codes.Where(c => c.StartsWith("approval.instance.", StringComparison.Ordinal));
        foreach (var code in codes)
            result[code] = await CanAsync(code, resource, ct);
        return result;
    }
}

internal sealed class DefaultApprovalAuthorization(
    DbContext db, IApprovalActorContext actor,
    IApprovalPermissionProvider provider, IApprovalQueryScope scope, ApprovalOptions options, IEnumerable<IApprovalResourceResolver> resolvers) : IApprovalAuthorization
{
    private readonly ApprovalResourceReader _resources = new(db, scope, options, resolvers);
    private ApprovalPermissionSubject Subject(string? userId = null)
    {
        var id = actor.ActorId; // Every host operation requires a trusted actor.
        if (string.IsNullOrWhiteSpace(id)) throw new ApprovalException("缺少可信操作人") { Code = ApprovalErrorCode.Unauthorized };
        if (userId is not null && !string.Equals(userId, id, StringComparison.Ordinal))
            throw new ApprovalException("操作人参数与当前身份不一致") { Code = ApprovalErrorCode.Forbidden };
        return new(id, actor.IsSystem);
    }
    public async Task RequireAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default)
    {
        if (!await CanAsync(permission, resource, userId, ct))
            throw new ApprovalException("没有执行此审批操作的权限") { Code = ApprovalErrorCode.Forbidden };
    }
    public async Task<bool> CanAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!ApprovalPermissions.All.Contains(permission)) throw new ArgumentException("未知审批权限码", nameof(permission));
        var subject = Subject(userId);
        var resolved = await _resources.ResolveAsync(resource ?? new(), subject, ct); // Resolve actual resource ownership before asking the host.
        return await AuthorizeResolvedAsync(permission, subject, resolved, ct);
    }
    public Task<ApprovalResource> ResolveResourceAsync(ApprovalResource resource, CancellationToken ct) => _resources.ResolveAsync(resource, Subject(), ct);
    public async Task RequireRecordedAsync(string permission, ApprovalResource resource, CancellationToken ct)
    {
        // Only the command executor can call this, with a committed record scoped to the same actor/command.
        // Deleted resources (e.g. a draft) no longer resolve, but current ACLs still apply to the trusted snapshot.
        if (!ApprovalPermissions.All.Contains(permission)) throw new ArgumentException("未知权限码", nameof(permission));
        if (!await AuthorizeResolvedAsync(permission, Subject(), resource, ct)) throw new ApprovalException("无权读取原操作结果") { Code = ApprovalErrorCode.Forbidden };
    }
    private async Task<bool> AuthorizeResolvedAsync(string permission, ApprovalPermissionSubject subject, ApprovalResource resolved, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (actor.IsSystem && (actor is not IApprovalSystemActorContext system || !system.AllowedPermissions.Contains(permission))) return false;
        if (!actor.IsSystem && permission.StartsWith("approval.system.", StringComparison.Ordinal)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PermissionTimeout);
        using var callback = new ApprovalUnitOfWork(db).Extension();
        try { return await provider.AuthorizeAsync(new(subject, permission, resolved), timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ApprovalException("审批权限服务暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
    }
    public async Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId = null, CancellationToken ct = default)
    {
        Subject(viewer.UserId);
        return viewer with { IsAdmin = await CanAsync(ApprovalPermissions.InstanceReadAll, new() { InstanceId = instanceId }, viewer.UserId, ct) };
    }
    public async Task<IQueryable<ApprovalInstance>> ScopeAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default)
    {
        var result = await _resources.ApplyScopeAsync(Subject(), query, ct);
        return query.Where(i => result.Select(x => x.Id).Contains(i.Id));
    }
}

internal sealed class MissingApprovalAuthorization : IApprovalAuthorization
{
    public static readonly MissingApprovalAuthorization Instance = new();
    private static Exception Missing() => new ApprovalException("请通过已注册的审批作用域取得服务及可信操作人上下文") { Code = ApprovalErrorCode.Unauthorized };
    public Task RequireAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default) => Task.FromException(Missing());
    public Task<bool> CanAsync(string permission, ApprovalResource? resource = null, string? userId = null, CancellationToken ct = default) => Task.FromException<bool>(Missing());
    public Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId = null, CancellationToken ct = default) => Task.FromException<ApprovalViewer>(Missing());
    public Task<IQueryable<ApprovalInstance>> ScopeAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default) => Task.FromException<IQueryable<ApprovalInstance>>(Missing());
}

public sealed class DefaultApprovalPermissionProvider : IApprovalPermissionProvider
{
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(request.Subject.IsSystem); }
}
internal sealed class DefaultApprovalQueryScope : IApprovalQueryScope
{
    public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) => Task.FromResult(instances);
}
