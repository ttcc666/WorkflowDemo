namespace Approval.Core.Abstractions;

/// <summary>启用审批分区的宿主 DbContext 在创建时固定的范围标识。</summary>
public interface IApprovalPartitionContext
{
    string PartitionKey { get; }
}

/// <summary>宿主捕获的可信执行上下文；进入后必须在当前异步调用链中保持，释放时恢复原上下文。</summary>
public interface IApprovalHostScope
{
    string? PartitionKey { get; }
    IDisposable Enter();
}

/// <summary>在命令入口捕获宿主上下文，并提供唯一审批 Worker 本轮要处理的宿主范围。</summary>
public interface IApprovalHostScopeProvider
{
    IApprovalHostScope Capture();
    Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default);
}
