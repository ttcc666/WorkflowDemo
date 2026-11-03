using System.Linq.Expressions;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Approval.Core.Persistence;

/// <summary>显式安装审批写保护；不接管宿主非审批实体的 SaveChanges。</summary>
public static class ApprovalPersistence
{
    private static readonly ConditionalWeakTable<DbContext, ApprovalTransactionState> States = new();
    internal static ApprovalTransactionState State(DbContext db)
    {
        var state = States.GetValue(db, context => new() { ContextId = context.ContextId });
        if (!state.ContextId.Equals(db.ContextId)) state.ResetLease(db.ContextId);
        return state;
    }
    public static DbContextOptionsBuilder UseApproval(this DbContextOptionsBuilder builder)
    {
        if (!builder.Options.Extensions.OfType<CoreOptionsExtension>().Any(e => e.Interceptors?.Contains(SaveGuard.Instance) == true))
            builder.AddInterceptors(SaveGuard.Instance, MaterializationGuard.Instance, ApprovalQueryGuard.Instance, ApprovalCommandGuard.Instance, TransactionGuard.Instance);
        return builder;
    }
    public static DbContextOptionsBuilder<TContext> UseApproval<TContext>(this DbContextOptionsBuilder<TContext> builder) where TContext : DbContext
    { UseApproval((DbContextOptionsBuilder)builder); return builder; }
    internal static void EnsureInstalled(DbContext db)
    {
        if (!db.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Any(e => e.Interceptors?.Contains(SaveGuard.Instance) == true))
            throw new InvalidOperationException("审批 DbContext 必须在 options 中调用 UseApproval() 安装事务与审计保护");
    }
    private static void ValidateWrites(DbContext db)
    {
        if (ApprovalPersistence.State(db).Failed) throw new InvalidOperationException("审批操作失败后当前作用域已作废，不能继续保存");
        if (ApprovalPersistence.State(db).ExtensionDepth > 0) throw new InvalidOperationException("生命周期/业务回调不得自行 SaveChanges；由审批工作单元统一提交");
        db.ChangeTracker.DetectChanges();
        var writes = ApprovalPersistence.State(db).Writes ??= new(db);
        writes.Validate();
        foreach (var entry in db.ChangeTracker.Entries<Approval.Core.Abstractions.IApprovalTrackedRevision>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.ConcurrencyStamp = checked(entry.OriginalValues.GetValue<long>(nameof(Approval.Core.Abstractions.IApprovalTrackedRevision.ConcurrencyStamp)) + 1);
            db.ChangeTracker.DetectChanges(); writes.RecordRevision(entry);
        }
        if (db.ChangeTracker.Entries<ApprovalDefinition>().Any(e => e.State == EntityState.Modified && e.OriginalValues.GetValue<bool>(nameof(ApprovalDefinition.IsPublished))))
            throw new InvalidOperationException("发布版本不可修改，请保存设计草稿并发布新版本");
        if (db.ChangeTracker.Entries<ApprovalDefinitionAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("治理审计不可改写或删除");
        if (db.ChangeTracker.Entries<ApprovalLog>().Any(e => e.State == EntityState.Modified
            || e.State == EntityState.Deleted))
            throw new InvalidOperationException("审批历史只允许追加");
        writes.Saving();
    }
    private sealed class MaterializationGuard : IMaterializationInterceptor
    {
        internal static readonly MaterializationGuard Instance = new();
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        { State(data.Context).Writes ??= new(data.Context); return entity; }
    }
    private sealed class SaveGuard : SaveChangesInterceptor
    {
        internal static readonly SaveGuard Instance = new();
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        { if (eventData.Context is { } db) ValidateWrites(db); return result; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { if (eventData.Context is { } db) ValidateWrites(db); return ValueTask.FromResult(result); }
        public override int SavedChanges(SaveChangesCompletedEventData data, int result)
        { if (data.Context is { } db) State(db).Writes?.Saved(); return result; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        { if (data.Context is { } db) State(db).Writes?.Saved(); return ValueTask.FromResult(result); }
        public override void SaveChangesFailed(DbContextErrorEventData data) { if (data.Context is { } db) State(db).Writes?.Failed(); }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken ct = default)
        { SaveChangesFailed(data); return Task.CompletedTask; }
        public override void SaveChangesCanceled(DbContextEventData data) { if (data.Context is { } db) State(db).Writes?.Failed(); }
        public override Task SaveChangesCanceledAsync(DbContextEventData data, CancellationToken ct = default)
        { SaveChangesCanceled(data); return Task.CompletedTask; }
    }
    internal sealed class TransactionGuard : DbTransactionInterceptor
    {
        public static readonly TransactionGuard Instance = new();
        private static void Check(TransactionEventData data)
        { if (data.Context is { } db && ApprovalPersistence.State(db).ExtensionCommitDepth > 0) throw new InvalidOperationException("扩展不得自行提交数据库事务"); }
        public override InterceptionResult TransactionCommitting(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        { Check(eventData); return result; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { Check(eventData); return ValueTask.FromResult(result); }
    }

    internal sealed class ApprovalCommandGuard : DbCommandInterceptor
    {
        public static readonly ApprovalCommandGuard Instance = new();
        private static void Validate(DbCommand command, CommandEventData data)
        {
            if (data.CommandSource == CommandSource.SaveChanges && data.Context is { } saving) State(saving).Writes?.VerifySaving();
            if (data.CommandSource is not (CommandSource.SaveChanges or CommandSource.Migrations) && data.Context is { } context
                && ApprovalSqlWriteGuard.WritesProtectedTable(context, command.CommandText))
                throw new InvalidOperationException("审批表禁止通过批量操作或原生 SQL 写入；可信离线维护须使用独立连接");
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Validate(command, eventData); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Validate(command, eventData); return ValueTask.FromResult(result); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Validate(command, data); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Validate(command, data); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
        { Validate(command, data); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default)
        { Validate(command, data); return ValueTask.FromResult(result); }
    }

    internal sealed class ApprovalQueryGuard : IQueryExpressionInterceptor
    {
        public static readonly ApprovalQueryGuard Instance = new();
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        { new GuardVisitor().Visit(queryExpression); return queryExpression; }
        internal sealed class GuardVisitor : ExpressionVisitor
        {
            private static readonly HashSet<System.Reflection.MethodInfo> BulkMethods = typeof(RelationalQueryableExtensions).GetMethods()
                .Where(m => m.IsGenericMethodDefinition && m.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync").ToHashSet();
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.IsGenericMethod && BulkMethods.Contains(node.Method.GetGenericMethodDefinition()))
                {
                    var target = node.Method.GetGenericArguments()[0];
                    if (ApprovalWriteAuthorization.Protected(target))
                        throw new InvalidOperationException("审批实体禁止通过 ExecuteUpdate / ExecuteDelete 批量改写或删除");
                }
                return base.VisitMethodCall(node);
            }
        }
    }
}
