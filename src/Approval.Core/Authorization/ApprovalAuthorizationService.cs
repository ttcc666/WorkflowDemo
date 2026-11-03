using Approval.Core.Abstractions;
using Approval.Core.Domain;
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

/// <summary>宿主/HTTP 能力入口。权限 provider 不能绕过租户、操作人或后台权限边界。</summary>
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
    IApprovalPermissionProvider provider, IApprovalQueryScope scope, ApprovalOptions options, IApprovalArchiveQueryScope archives) : IApprovalAuthorization
{
    private ApprovalPermissionSubject Subject(string? userId = null)
    {
        var id = actor.ActorId; // Missing trusted identity is always an error, also in single-tenant mode.
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
        var resolved = await ResolveAsync(resource ?? new(), subject, ct); // Resolve actual resource ownership before asking the host.
        return await AuthorizeResolvedAsync(permission, subject, resolved, ct);
    }
    public Task<ApprovalResource> ResolveResourceAsync(ApprovalResource resource, CancellationToken ct) => ResolveAsync(resource, Subject(), ct);
    public async Task RequireRecordedAsync(string permission, ApprovalResource resource, CancellationToken ct)
    {
        // Only the command executor can call this, with a committed record scoped to the same tenant/actor.
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
        try { return await provider.AuthorizeAsync(new(subject, permission, resolved), timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ApprovalException("审批权限服务暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
    }
    private async Task<ApprovalResource> ResolveAsync(ApprovalResource resource, ApprovalPermissionSubject subject, CancellationToken ct)
    {
        static ApprovalException Missing() => new("资源不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
        if (resource.ArchiveId is { } archiveId)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(options.PermissionTimeout);
            IQueryable<ApprovalArchiveRecord> visible;
            try { visible = await archives.ApplyAsync(subject, db.Set<ApprovalArchiveRecord>().AsNoTracking(), timeout.Token).WaitAsync(timeout.Token); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { throw new ApprovalException("归档数据范围暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
            var archived = await visible.SingleOrDefaultAsync(a => a.Id == archiveId, ct) ?? throw Missing();
            return new ApprovalResource { ArchiveId = archiveId, InstanceId = archived.InstanceId, DefinitionId = archived.DefinitionId,
                DefinitionKey = archived.DefinitionKey, BusinessType = archived.BusinessType, BusinessKey = archived.BusinessKey, TargetUserId = archived.UserId };
        }
        Guid? instanceId = resource.InstanceId;
        if (resource.TaskId is { } taskId)
            instanceId = (await db.Set<ApprovalTask>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw Missing()).InstanceId;
        if (resource.FileId is { } fileId)
            instanceId = (await db.Set<ApprovalFile>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, ct) ?? throw Missing()).InstanceId;
        if (resource.DraftId is { } draftId)
        {
            var draft = await db.Set<ApprovalDraft>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == draftId, ct) ?? throw Missing();
            resource = resource with { DefinitionKey = draft.DefinitionKey, TargetUserId = draft.UserId };
        }
        if (resource.OutboxId is { } outboxId)
            instanceId = (await db.Set<ApprovalOutboxMessage>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == outboxId, ct) ?? throw Missing()).InstanceId;
        if (resource.MessageId is { } messageId)
            instanceId = (await db.Set<ApprovalMessage>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, ct) ?? throw Missing()).InstanceId;
        if (instanceId is { } id)
        {
            if (resource.InstanceId is { } requested && requested != id) throw Missing();
            var instances = await ApplyScopeAsync(subject, db.Set<ApprovalInstance>().AsNoTracking(), ct);
            var instance = await db.Set<ApprovalInstance>().AsNoTracking().Where(i => instances.Select(x => x.Id).Contains(i.Id)).SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw Missing();
            resource = resource with { InstanceId = id, DefinitionId = instance.DefinitionId, DefinitionKey = instance.DefinitionKey, BusinessType = instance.BusinessType, BusinessKey = instance.BusinessKey };
        }
        if (resource.DefinitionId is { } definitionId)
        {
            var definition = await db.Set<ApprovalDefinition>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == definitionId, ct) ?? throw Missing();
            resource = resource with { DefinitionKey = definition.Key };
        }
        else if (!string.IsNullOrWhiteSpace(resource.DefinitionKey))
        {
            var definition = await db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.Key == resource.DefinitionKey && d.IsPublished)
                .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);
            if (definition is not null) resource = resource with { DefinitionId = definition.Id };
        }
        return resource;
    }
    public async Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId = null, CancellationToken ct = default)
    {
        Subject(viewer.UserId);
        return viewer with { IsAdmin = await CanAsync(ApprovalPermissions.InstanceReadAll, new() { InstanceId = instanceId }, viewer.UserId, ct) };
    }
    public async Task<IQueryable<ApprovalInstance>> ScopeAsync(IQueryable<ApprovalInstance> query, CancellationToken ct = default)
    {
        var result = await ApplyScopeAsync(Subject(), query, ct);
        return query.Where(i => result.Select(x => x.Id).Contains(i.Id));
    }
    private async Task<IQueryable<ApprovalInstance>> ApplyScopeAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PermissionTimeout);
        try { return await scope.ApplyAsync(subject, query, timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ApprovalException("审批数据权限服务暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
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

public sealed class DefaultApprovalPermissionProvider(IApprovalIdentityProvider identity, ApprovalOptions options) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        if (request.Subject.IsSystem) return true; // The mandatory gate has already checked the explicit system grant set.
        if (!ApprovalPermissions.All.Contains(request.Permission)) return false;
        if (!ApprovalPermissions.IsManagement(request.Permission) && !ApprovalPermissions.IsDesigner(request.Permission)) return true;
        if (await identity.IsInRoleAsync(request.Subject.ActorId, options.AdminRole, ct)) return true;
        if (!ApprovalPermissions.IsDesigner(request.Permission)) return false;
        foreach (var role in options.DefinitionReaders)
            if (await identity.IsInRoleAsync(request.Subject.ActorId, role, ct)) return true;
        return false;
    }
}
internal sealed class DefaultApprovalQueryScope : IApprovalQueryScope
{
    public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) => Task.FromResult(instances);
}
