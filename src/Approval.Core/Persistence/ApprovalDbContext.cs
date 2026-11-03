using System.Linq.Expressions;
using System.Data.Common;
using System.Text.RegularExpressions;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Core.Persistence;

/// <summary>宿主拥有业务模型和共享事务；审批实体与业务实体由同一上下文提交。</summary>
public abstract class ApprovalDbContext : DbContext
{
    protected ApprovalDbContext(DbContextOptions options) : base(options) { }
    protected ApprovalDbContext() { }
    internal ApprovalTransactionState TransactionState { get; } = new();
    protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        ConfigureApprovalOptions(optionsBuilder);
        optionsBuilder.AddInterceptors(ApprovalQueryGuard.Instance, ApprovalCommandGuard.Instance, TransactionGuard.Instance);
    }
    /// <summary>需要在上下文内选择 Provider 时覆盖此方法，审批事务与不可变历史保护由基类安装。</summary>
    protected virtual void ConfigureApprovalOptions(DbContextOptionsBuilder optionsBuilder) { }
    private void ValidateWrites()
    {
        if (TransactionState.Failed) throw new InvalidOperationException("审批操作失败后当前作用域已作废，不能继续保存");
        if (TransactionState.ExtensionDepth > 0) throw new InvalidOperationException("生命周期/业务回调不得自行 SaveChanges；由审批工作单元统一提交");
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ApprovalFile>().Where(e => e.State == EntityState.Modified))
            entry.Entity.ConcurrencyStamp = checked(entry.OriginalValues.GetValue<long>(nameof(ApprovalFile.ConcurrencyStamp)) + 1);
        if (ChangeTracker.Entries<ApprovalDefinition>().Any(e => e.State == EntityState.Modified && e.OriginalValues.GetValue<bool>(nameof(ApprovalDefinition.IsPublished))))
            throw new InvalidOperationException("发布版本不可修改，请保存设计草稿并发布新版本");
        if (ChangeTracker.Entries<ApprovalDefinitionAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("治理审计不可改写或删除");
        if (ChangeTracker.Entries<ApprovalLog>().Any(e => e.State == EntityState.Modified
            || e.State == EntityState.Deleted && !TransactionState.AllowLogDeletion))
            throw new InvalidOperationException("审批历史只允许追加");
        if (ChangeTracker.Entries<ApprovalMaintenanceAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("维护审计不可改写或删除");
    }
    public sealed override int SaveChanges() => SaveChanges(true);
    public sealed override Task<int> SaveChangesAsync(CancellationToken ct = default) => SaveChangesAsync(true, ct);
    public sealed override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidateWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public sealed override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { ValidateWrites(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
    private sealed class TransactionGuard : DbTransactionInterceptor
    {
        public static readonly TransactionGuard Instance = new();
        private static void Check(TransactionEventData data)
        { if (data.Context is ApprovalDbContext db && db.TransactionState.ExtensionCommitDepth > 0) throw new InvalidOperationException("扩展不得自行提交数据库事务"); }
        public override InterceptionResult TransactionCommitting(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        { Check(eventData); return result; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { Check(eventData); return ValueTask.FromResult(result); }
    }

    private sealed class ApprovalCommandGuard : DbCommandInterceptor
    {
        public static readonly ApprovalCommandGuard Instance = new();
        private static void Validate(DbCommand command, CommandEventData data)
        {
            if (data.CommandSource == CommandSource.ExecuteSqlRaw && data.Context is { } context &&
                context.Model.GetEntityTypes().Where(t => typeof(IApprovalEntity).IsAssignableFrom(t.ClrType))
                    .Any(t => command.CommandText.Contains(t.GetTableName()!, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("审批表禁止通过 ExecuteSqlRaw 写入；离线维护请使用独立连接及显式映射工具");
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Validate(command, eventData); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        { Validate(command, eventData); return ValueTask.FromResult(result); }
    }

    private sealed class ApprovalQueryGuard : IQueryExpressionInterceptor
    {
        public static readonly ApprovalQueryGuard Instance = new();
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        { new GuardVisitor().Visit(queryExpression); return queryExpression; }
        private sealed class GuardVisitor : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync")
                {
                    var target = RootType(node.Arguments[0]);
                    if (target == typeof(ApprovalDefinition) || target == typeof(ApprovalDefinitionAudit) || target == typeof(ApprovalLog))
                        throw new InvalidOperationException("发布版本与治理审计禁止批量改写或删除");
                }
                return base.VisitMethodCall(node);
            }
            private static Type? RootType(Expression source) => source switch
            {
                Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression root => root.EntityType.ClrType,
                MethodCallExpression call when call.Arguments.Count > 0 => RootType(call.Arguments[0]),
                UnaryExpression unary => RootType(unary.Operand),
                _ => null
            };
        }
    }
}
