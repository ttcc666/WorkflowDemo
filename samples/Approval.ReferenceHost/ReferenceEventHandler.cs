using Approval.Core.Abstractions;
namespace Approval.ReferenceHost;
/// <summary>宿主决定外部副作用；此示例接收业务事件并写入宿主日志。</summary>
public sealed class ReferenceEventHandler(ILogger<ReferenceEventHandler> logger) : IApprovalEventHandler
{
    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        logger.LogInformation("Host approval event {EventId}: {EventType} / {BusinessKey}", approvalEvent.Id, approvalEvent.EventType, approvalEvent.BusinessKey);
        return Task.CompletedTask;
    }
}
