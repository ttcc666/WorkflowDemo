using System.Transactions;
using Approval.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Approval.Core.Persistence;

internal sealed class ApprovalTransactionState
{
    public bool Active, Failed;
    public bool DeferredRead;
    public bool Serializable;
    public bool RequireOperationId, SystemActor;
    public int ExtensionDepth;
    public int ExtensionCommitDepth;
    public Guid? InstanceId, DefinitionId;
    public string? OperationSeed;
    public Dictionary<string, Guid> Definitions = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> IdCounters = new(StringComparer.Ordinal);
    public readonly List<Func<Task>> Callbacks = [];
}

internal sealed class ApprovalUnitOfWork(DbContext db) : IApprovalUnitOfWork
{
    private ApprovalTransactionState State => ApprovalPersistence.State(db);
    public bool IsActive => State.Active;
    internal void Reference(Guid instance, Guid definition) { State.InstanceId = instance; State.DefinitionId = definition; }
    internal Guid NewId(string kind)
    {
        if (State.OperationSeed is null) return Guid.NewGuid();
        var index = State.IdCounters.GetValueOrDefault(kind); State.IdCounters[kind] = index + 1;
        return Guid.ParseExact(ApprovalCommandHash.Of(State.OperationSeed, kind, index.ToString(System.Globalization.CultureInfo.InvariantCulture))[..32], "N");
    }
    internal Guid? PinnedDefinition(string key) => IsActive && State.Definitions.TryGetValue(key, out var id) ? id : null;
    internal void PinDefinition(string key, Guid id) { if (IsActive) State.Definitions.TryAdd(key, id); }
    public async Task SetResultInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        CheckUsable();
        if (!IsActive) throw new InvalidOperationException("结果资源必须在工作单元内绑定");
        var instance = await db.Set<Approval.Core.Domain.ApprovalInstance>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new ApprovalException("结果实例不存在") { Code = ApprovalErrorCode.NotFound };
        Reference(instance.Id, instance.DefinitionId);
    }
    internal IDisposable Extension(bool allowSave = false)
    {
        State.ExtensionCommitDepth++;
        if (!allowSave) State.ExtensionDepth++;
        return new OnDispose(() => { State.ExtensionCommitDepth--; if (!allowSave) State.ExtensionDepth--; });
    }
    internal void CheckUsable()
    {
        if (State.Failed) throw new InvalidOperationException("审批操作失败后必须释放当前作用域，不能继续保存或重用实体图");
    }
    internal Task<T> ExecuteCommandAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        if (!IsActive && State.RequireOperationId && !State.SystemActor)
            throw new ApprovalException("写操作需要通过带稳定标识的命令协调器执行") { Code = ApprovalErrorCode.OperationIdRequired };
        return ExecuteAsync(operation, ct);
    }
    internal async Task ExecuteCommandAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        await ExecuteCommandAsync<object?>(async token => { await operation(token); return null; }, ct);
    public void AfterCommit(Func<Task> callback)
    {
        CheckUsable();
        if (!IsActive) throw new InvalidOperationException("提交后回调必须在审批工作单元内登记");
        State.Callbacks.Add(callback);
    }
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        await ExecuteAsync<object?>(async token => { await operation(token); return null; }, ct);
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        CheckUsable();
        if (State.ExtensionCommitDepth != 0) throw new InvalidOperationException("扩展不能重入审批写操作或自行提交工作单元");
        if (IsActive)
        {
            try { return await operation(ct); }
            catch { State.Failed = true; throw; }
        }
        if (Transaction.Current is not null || db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("裸外部事务不受支持，请通过 IApprovalUnitOfWork 或 IApprovalCommandExecutor 执行完整委托");
        var strategy = db.Database.CreateExecutionStrategy();
        if (strategy.RetriesOnFailure && ExecutionStrategy.Current is null)
            throw new InvalidOperationException("启用数据库自动重试时，请通过 IApprovalCommandExecutor 执行完整业务委托，以便每次使用新作用域");
        State.Active = true;
        using var observation = Approval.Core.Diagnostics.ApprovalInstrumentation.Transaction();
        if (State.OperationSeed is null) State.Definitions.Clear();
        State.InstanceId = null; State.DefinitionId = null; State.IdCounters.Clear();
        State.Callbacks.Clear();
        ApprovalTransaction ownedTransaction;
        try { ownedTransaction = await ApprovalTransaction.BeginAsync(db, State.DeferredRead, ct); }
        catch { State.Active = false; State.Failed = true; throw; }
        await using var lifetime = ownedTransaction;
        var transaction = ownedTransaction.Transaction;
        T result;
        try
        {
            result = await operation(ct);
            CheckUsable();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            State.Failed = true;
            State.Callbacks.Clear();
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* Preserve the original failure; executor verifies uncertain commits. */ }
            throw;
        }
        finally { State.Active = false; }
        await ownedTransaction.DisposeAsync();
        // Commit has completed. Cancellation of the original caller cannot roll it back.
        var callbacks = State.Callbacks.ToArray(); State.Callbacks.Clear();
        foreach (var callback in callbacks) { try { await callback(); } catch { /* In-process post-commit callbacks are best effort. Reliable delivery uses Outbox. */ } }
        return result;
    }
    private sealed class OnDispose(Action action) : IDisposable { public void Dispose() => action(); }
}
