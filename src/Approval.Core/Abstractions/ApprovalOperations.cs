namespace Approval.Core.Abstractions;

/// <summary>由宿主定义稳定命令/资源范围。Payload 只用于摘要；Result 必须是可序列化的结果 DTO。</summary>
public sealed record ApprovalCommand(string Kind, string Scope, string? OperationId, object? Payload,
    string Permission, ApprovalResource? Resource = null)
{
    internal bool AllowUnkeyed { get; init; }
}

/// <summary>完整业务委托在新作用域内运行；失败重试不复用 DbContext。不要捕获原作用域的服务/实体。</summary>
public interface IApprovalCommandExecutor
{
    Task<T> ExecuteAsync<T>(ApprovalCommand command, Func<IServiceProvider, CancellationToken, Task<T>> operation, CancellationToken ct = default);
}

/// <summary>同一数据库的显式工作单元；嵌套调用不提交外层事务。异常后作用域作废。</summary>
public interface IApprovalUnitOfWork
{
    bool IsActive { get; }

    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);

    void AfterCommit(Func<Task> callback);

    Task SetResultInstanceAsync(Guid instanceId, CancellationToken ct = default);
}
