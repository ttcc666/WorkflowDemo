using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Authorization;

/// <summary>规范资源归属和数据库范围；能力判断与可信操作人验证由统一授权入口负责。</summary>
internal sealed class ApprovalResourceReader(
    DbContext db, IApprovalQueryScope scope, ApprovalOptions options, IEnumerable<IApprovalResourceResolver> resolvers)
{
    internal async Task<ApprovalResource> ResolveAsync(ApprovalResource resource, ApprovalPermissionSubject subject, CancellationToken ct)
    {
        static ApprovalException Missing() => new("资源不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
        foreach (var resolver in resolvers)
        { using var callback = new ApprovalUnitOfWork(db).Extension(); resource = await resolver.ResolveAsync(resource, ct); }
        Guid? instanceId = resource.InstanceId;
        if (resource.TaskId is { } taskId)
            instanceId = (await db.Set<ApprovalTask>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct) ?? throw Missing()).InstanceId;
        if (resource.OutboxId is { } outboxId)
            instanceId = (await db.Set<ApprovalOutboxMessage>().Where(m => m.Id == outboxId)
                .Select(m => new { m.InstanceId }).SingleOrDefaultAsync(ct) ?? throw Missing()).InstanceId;
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

    internal async Task<IQueryable<ApprovalInstance>> ApplyScopeAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> query, CancellationToken ct)
    {
        using var callback = new ApprovalUnitOfWork(db).Extension();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PermissionTimeout);
        try { return await scope.ApplyAsync(subject, query, timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ApprovalException("审批数据权限服务暂不可用", ex) { Code = ApprovalErrorCode.AuthorizationUnavailable }; }
    }
}
