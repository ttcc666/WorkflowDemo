using Approval.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Approval.Core.Persistence;

/// <summary>EF 分区访问的共同边界；不替代数据库账号权限。</summary>
internal static class ApprovalPartitionGuard
{
    private sealed record Binding(string Key);
    private static readonly ConditionalWeakTable<DbContext, Binding> Bindings = new();

    internal static bool IsPartitioned(DbContext db) => db.Model.FindAnnotation(ApprovalPartitionModelExtensions.PartitionAnnotation) is not null;

    internal static void ValidateContext(DbContext db)
    {
        if (!IsPartitioned(db)) return;
        if (db is not IApprovalPartitionContext context || string.IsNullOrWhiteSpace(context.PartitionKey))
            throw new InvalidOperationException("审批分区 DbContext 必须提供非空的可信 PartitionKey");
        // An ordinary pool does not recreate constructor-injected tenant services on each lease.
        if (db.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Any(e => e.MaxPoolSize is not null))
            throw new InvalidOperationException("审批分区 DbContext 不支持池化，请使用每个宿主范围独立的 DbContext");
        var binding = Bindings.GetValue(db, _ =>
        {
            foreach (var entity in db.Model.GetEntityTypes())
            {
                if (entity.GetSqlQuery() is not null || entity.GetFunctionName() is not null)
                    throw new InvalidOperationException("审批分区 DbContext 禁止映射原生 SQL 或数据库函数查询源");
                if (entity.FindAnnotation(ApprovalPartitionModelExtensions.PartitionAnnotation)?.Value is not string property) continue;
                if (!entity.GetRootType().GetDeclaredQueryFilters().Any(filter =>
                    filter.Expression is { } expression && RequiresPartition(expression.Body, expression.Parameters[0], property, db.GetType())))
                    throw new InvalidOperationException("审批分区过滤器必须以 AND 约束实体分区属性等于当前 DbContext.PartitionKey");
                var field = entity.FindProperty(property)!;
                if (!field.IsConcurrencyToken || field.IsNullable)
                    throw new InvalidOperationException("审批分区属性必须非空且参与并发写入条件");
            }
            return new(context.PartitionKey);
        });
        if (!string.Equals(binding.Key, context.PartitionKey, StringComparison.Ordinal))
            throw new InvalidOperationException("审批分区 DbContext 的 PartitionKey 不可切换");
        var services = db.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Single().ApplicationServiceProvider;
        if (services?.GetService(typeof(IApprovalHostScopeProvider)) is IApprovalHostScopeProvider scopes
            && !string.Equals(scopes.Capture().PartitionKey, binding.Key, StringComparison.Ordinal))
            throw new ApprovalException("DbContext 不能跨宿主分区执行数据库命令") { Code = ApprovalErrorCode.Forbidden };
    }

    private static bool RequiresPartition(Expression expression, ParameterExpression row, string property, Type contextType)
    {
        // Only an unconditional equality (possibly under AND) is a proof. OR, helper methods,
        // soft-delete-only filters and arbitrary captured tenant values cannot prove the boundary.
        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso } and)
            return RequiresPartition(and.Left, row, property, contextType) || RequiresPartition(and.Right, row, property, contextType);
        return expression is BinaryExpression { NodeType: ExpressionType.Equal } equal
            && (IsPartition(equal.Left, row, property) && IsContextKey(equal.Right, contextType)
                || IsPartition(equal.Right, row, property) && IsContextKey(equal.Left, contextType));
    }

    private static bool IsPartition(Expression expression, ParameterExpression row, string property) => expression switch
    {
        MemberExpression member => member.Member.Name == property && member.Expression == row,
        MethodCallExpression call => call.Method.DeclaringType == typeof(EF) && call.Method.Name == nameof(EF.Property)
            && call.Arguments[0] == row && call.Arguments[1] is ConstantExpression { Value: string name } && name == property,
        _ => false
    };

    private static bool IsContextKey(Expression expression, Type contextType) => expression is MemberExpression
        { Member.Name: nameof(IApprovalPartitionContext.PartitionKey), Expression: ConstantExpression { Value: DbContext context } }
        && context is IApprovalPartitionContext && context.GetType() == contextType;

    internal static void ValidateWrites(DbContext db)
    {
        if (!IsPartitioned(db)) return;
        ValidateContext(db);
        var partition = ((IApprovalPartitionContext)db).PartitionKey;
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Metadata.FindAnnotation(ApprovalPartitionModelExtensions.PartitionAnnotation)?.Value is not string property) continue;
            var value = entry.Property(property);
            if (!Equals(value.CurrentValue, partition) || entry.State != EntityState.Added && !Equals(value.OriginalValue, partition))
                throw new InvalidOperationException("不能伪造或更改实体分区");
        }
    }
}
