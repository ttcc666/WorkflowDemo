using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

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
        var state = State(db);
        if (state.Failed) throw new InvalidOperationException("审批操作失败后当前作用域已作废，不能继续保存");
        if (state.ExtensionDepth > 0)
        {
            // A callback may catch the exception while leaving pending changes in the tracker.
            state.Failed = true;
            throw new InvalidOperationException("生命周期/业务回调不得自行 SaveChanges；由审批工作单元统一提交");
        }
        db.ChangeTracker.DetectChanges();
        ApprovalPartitionGuard.ValidateWrites(db);
        var writes = state.Writes ??= new(db);
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

        public override void SaveChangesFailed(DbContextErrorEventData data)
        { if (data.Context is { } db) State(db).Writes?.Failed(); }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken ct = default)
        { SaveChangesFailed(data); return Task.CompletedTask; }

        public override void SaveChangesCanceled(DbContextEventData data)
        { if (data.Context is { } db) State(db).Writes?.Failed(); }

        public override Task SaveChangesCanceledAsync(DbContextEventData data, CancellationToken ct = default)
        { SaveChangesCanceled(data); return Task.CompletedTask; }
    }

    internal sealed class TransactionGuard : DbTransactionInterceptor
    {
        public static readonly TransactionGuard Instance = new();

        private static void RejectExtensionTransaction(DbContext? context, string message)
        {
            if (context is null) return;
            var state = State(context);
            if (state.ExtensionCommitDepth > 0)
            {
                state.Failed = true;
                throw new InvalidOperationException(message);
            }
            if (state.Failed) throw new InvalidOperationException("审批操作失败后当前作用域已作废，不能开启或提交数据库事务");
        }

        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result)
        { RejectExtensionTransaction(data.Context, "扩展不得自行开启数据库事务"); return result; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result, CancellationToken ct = default)
        { RejectExtensionTransaction(data.Context, "扩展不得自行开启数据库事务"); return ValueTask.FromResult(result); }

        public override InterceptionResult TransactionCommitting(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        { RejectExtensionTransaction(eventData.Context, "扩展不得自行提交数据库事务"); return result; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { RejectExtensionTransaction(eventData.Context, "扩展不得自行提交数据库事务"); return ValueTask.FromResult(result); }
    }

    internal sealed class ApprovalCommandGuard : DbCommandInterceptor
    {
        public static readonly ApprovalCommandGuard Instance = new();

        private static void Validate(DbCommand command, CommandEventData data)
        {
            if (data.Context is not { } context) return;
            if (data.CommandSource != CommandSource.Migrations && ApprovalPartitionGuard.IsPartitioned(context))
            {
                ApprovalPartitionGuard.ValidateContext(context);
                if (data.CommandSource is not (CommandSource.LinqQuery or CommandSource.SaveChanges or CommandSource.ExecuteUpdate or CommandSource.ExecuteDelete or CommandSource.ValueGenerator))
                    throw new InvalidOperationException("审批分区 DbContext 禁止原生 SQL 或未知来源命令；迁移使用 EF Migration，离线维护使用独立管理连接");
            }
            if (data.CommandSource == CommandSource.SaveChanges)
            {
                State(context).Writes?.VerifySaving();
                return;
            }
            if (data.CommandSource != CommandSource.Migrations && ApprovalSqlWriteGuard.WritesProtectedTable(context, command.CommandText))
            {
                if (State(context) is { ExtensionDepth: > 0 } state) state.Failed = true;
                throw new InvalidOperationException("审批表禁止通过批量操作或原生 SQL 写入；可信离线维护须使用独立连接");
            }
            if (data.CommandSource is CommandSource.ExecuteUpdate or CommandSource.ExecuteDelete or CommandSource.ExecuteSqlRaw
                && State(context) is { ExtensionDepth: > 0 } readOnly)
            {
                readOnly.Failed = true;
                throw new InvalidOperationException("只读扩展不得执行批量写入或 ExecuteSqlRaw");
            }
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
        {
            var partitioned = eventData.Context is { } db && ApprovalPartitionGuard.IsPartitioned(db);
            var guard = new GuardVisitor(eventData.Context);
            guard.Visit(queryExpression);
            // Navigation expansion and auto includes run after this interceptor. The partitioned
            // context, not the current shape of the expression, owns the filter boundary.
            if (guard.RemovesFilters && partitioned)
                throw new InvalidOperationException("审批分区 DbContext 禁止移除查询过滤器");
            if (partitioned && guard.RawSql)
                throw new InvalidOperationException("审批分区 DbContext 禁止原生 SQL 查询");
            return queryExpression;
        }

        internal sealed class GuardVisitor(DbContext? context = null) : ExpressionVisitor
        {
            private readonly DbContext? partitionContext = context is not null && ApprovalPartitionGuard.IsPartitioned(context) ? context : null;
            public bool RemovesFilters { get; private set; }
            public bool RawSql { get; private set; }

            protected override Expression VisitExtension(Expression node)
            {
                // Use an allow list of EF roots, not SQL text. Provider-specific/raw/function roots
                // cannot prove scope and are rejected, including composed SqlQuery/FromSql sources.
                if (partitionContext is not null && node is QueryRootExpression
                    && node.GetType() != typeof(EntityQueryRootExpression)
                    && node is not InlineQueryRootExpression and not ParameterQueryRootExpression)
                    RawSql = true;
                return base.VisitExtension(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                    && node.Method.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters))
                    RemovesFilters = true;
                if (node.Method.IsGenericMethod
                    && (node.Method.DeclaringType == typeof(RelationalQueryableExtensions) || node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions))
                    && node.Method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync")
                {
                    var target = node.Method.GetGenericArguments()[0];
                    if (ApprovalWriteAuthorization.Protected(target))
                    {
                        if (context is not null && State(context) is { ExtensionDepth: > 0 } state) state.Failed = true;
                        throw new InvalidOperationException("审批表禁止通过批量操作或原生 SQL 写入；可信离线维护须使用独立连接");
                    }
                    if (partitionContext is not null && node.Method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync")
                        ValidateSetters(node.Arguments[1]);
                }
                return base.VisitMethodCall(node);
            }

            private void ValidateSetters(Expression setters)
            {
                if (setters is not NewArrayExpression array || array.Expressions.Count == 0)
                    throw new InvalidOperationException("无法证明批量更新的分区安全性");
                foreach (var setter in array.Expressions)
                {
                    if (setter is not NewExpression pair || pair.Arguments.Count != 2)
                        throw new InvalidOperationException("无法证明批量更新的分区安全性");
                    var selector = pair.Arguments[0];
                    while (selector is UnaryExpression { NodeType: ExpressionType.Quote or ExpressionType.Convert } unary) selector = unary.Operand;
                    if (selector is not LambdaExpression lambda)
                        throw new InvalidOperationException("无法证明批量更新的分区安全性");
                    var body = lambda.Body;
                    while (body is UnaryExpression { NodeType: ExpressionType.Convert } convert) body = convert.Operand;
                    var property = body switch
                    {
                        MemberExpression member => member.Member.Name,
                        MethodCallExpression call when call.Method.DeclaringType == typeof(EF) && call.Method.Name == nameof(EF.Property)
                            && call.Arguments[1] is ConstantExpression { Value: string name } => name,
                        _ => null
                    };
                    if (property is null || partitionContext!.Model.GetEntityTypes().Any(e =>
                        e.FindAnnotation(ApprovalPartitionModelExtensions.PartitionAnnotation)?.Value is string partition && partition == property))
                        throw new InvalidOperationException("批量更新禁止修改分区属性或使用无法验证的属性选择器");
                }
            }
        }
    }
}
