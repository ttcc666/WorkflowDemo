using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>
/// 宿主开始审批前的本地校验/准备。运行在共享审批事务内；异常使整段操作回滚。
/// 只修改跟踪实体或申请快照，不自行保存/提交，不执行远程副作用。
/// </summary>
public interface IApprovalLifecycleHandler
{
    Task OnStartingAsync(ApprovalInstance instance, CancellationToken ct);
}
