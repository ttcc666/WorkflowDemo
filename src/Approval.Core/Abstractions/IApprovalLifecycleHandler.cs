namespace Approval.Core.Abstractions;

/// <summary>
/// 宿主开始审批前的本地校验/准备。运行在共享审批事务内；异常使整段操作回滚。
/// 可修改宿主业务实体，返回标题与申请数据的准备结果；不自行保存/提交，不执行远程副作用。
/// </summary>
public interface IApprovalLifecycleHandler
{
    Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct);
}
