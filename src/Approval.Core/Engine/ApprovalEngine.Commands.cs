using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Core.Engine;

public sealed partial class ApprovalEngine
{
    private async Task<ApprovalInstance> StartCommandAsync(StartApprovalRequest request, CancellationToken ct)
    {
        if (request.OperationId is null)
        {
            return await _unit.ExecuteCommandAsync(token => StartAsyncCore(request, token), ct);
        }
        if (_unit.IsActive || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("带 OperationId 的发起必须在干净作用域调用；宿主业务准备请放入 IApprovalCommandExecutor 委托");
        var body = request with { OperationId = null };
        var id = await _commands.ExecuteAsync(new("instance.start", request.BusinessType + "/" + request.BusinessKey, request.OperationId,
            body, ApprovalPermissions.InstanceStart, new()
            {
                DefinitionKey = request.DefinitionKey,
                DefinitionId = request.DefinitionId,
                BusinessType = request.BusinessType,
                BusinessKey = request.BusinessKey
            }),
            async (sp, token) => (await sp.GetRequiredService<ApprovalEngine>().StartAsync(body, token)).Id, ct);
        return await _db.Set<ApprovalInstance>().AsNoTracking().Include(i => i.Tasks).Include(i => i.Logs).AsSplitQuery().SingleAsync(i => i.Id == id, ct);
    }
}
