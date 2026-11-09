using System.Data.Common;
using System.Linq.Expressions;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Approval.MultiTenantHost;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options, TenantScopes scopes) : DbContext(options), IApprovalPartitionContext
{
    public const string TenantProperty = "TenantId";
    public string PartitionKey { get; } = scopes.Current;
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<DeliveryReceipt> Receipts => Set<DeliveryReceipt>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);
        options.UseApproval().AddInterceptors(new TenantCommandGuard(scopes));
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureApproval();
        model.Entity<Expense>(b =>
        {
            b.ToTable("HostExpenses"); b.HasKey(e => e.Id);
            b.Property(e => e.BusinessKey).HasMaxLength(100); b.HasIndex(e => e.BusinessKey).IsUnique();
            b.Property(e => e.ApplicantId).HasMaxLength(100); b.Property(e => e.Status).HasMaxLength(20);
            b.HasOne<ApprovalInstance>().WithMany().HasForeignKey(e => e.InstanceId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<DeliveryReceipt>(b =>
        {
            b.ToTable("HostDeliveryReceipts"); b.HasKey(r => r.Id);
            b.Property(r => r.EventType).HasMaxLength(100);
            b.HasOne<ApprovalInstance>().WithMany().HasForeignKey(r => r.InstanceId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var entity in model.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToArray())
        {
            var builder = model.Entity(entity.ClrType);
            // UPDATE/DELETE must verify the stored partition too, including entities attached by ID.
            builder.Property<string>(TenantProperty).HasMaxLength(64).IsRequired().IsConcurrencyToken().HasValueGenerator<TenantValueGenerator>()
                .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            var parameter = Expression.Parameter(entity.ClrType, "row");
            var property = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], parameter, Expression.Constant(TenantProperty));
            builder.HasQueryFilter(Expression.Lambda(Expression.Equal(property, Expression.Property(Expression.Constant(this), nameof(PartitionKey))), parameter));
        }
        model.ConfigureApprovalPartition(TenantProperty, typeof(Expense), typeof(DeliveryReceipt));
    }
    private void ValidatePartitionWrites()
    {
        if (!string.Equals(PartitionKey, scopes.Current, StringComparison.Ordinal))
            throw new ApprovalException("DbContext 不能切换宿主租户") { Code = ApprovalErrorCode.Forbidden };
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Metadata.FindProperty(TenantProperty) is null) continue;
            var tenant = entry.Property(TenantProperty);
            if (!Equals(tenant.CurrentValue, PartitionKey) || entry.State != EntityState.Added && !Equals(tenant.OriginalValue, PartitionKey))
                throw new ApprovalException("不能伪造或更改实体租户") { Code = ApprovalErrorCode.Forbidden };
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidatePartitionWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { ValidatePartitionWrites(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
}

public sealed class TenantValueGenerator : ValueGenerator<string>
{
    public override bool GeneratesTemporaryValues => false;
    public override bool GeneratesStableValues => true;
    public override string Next(EntityEntry entry) => ((IApprovalPartitionContext)entry.Context).PartitionKey;
}

/// <summary>每次数据库命令检查上下文，覆盖普通、编译查询和写入，不依赖查询首次编译。</summary>
public sealed class TenantCommandGuard(TenantScopes scopes) : DbCommandInterceptor
{
    private void Validate(CommandEventData data)
    {
        if (data.CommandSource == CommandSource.Migrations) return;
        if (data.Context is IApprovalPartitionContext context && !string.Equals(context.PartitionKey, scopes.Current, StringComparison.Ordinal))
            throw new ApprovalException("DbContext 不能跨租户执行数据库命令") { Code = ApprovalErrorCode.Forbidden };
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Validate(data); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { Validate(data); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) { Validate(data); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default) { Validate(data); return ValueTask.FromResult(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result) { Validate(data); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default) { Validate(data); return ValueTask.FromResult(result); }
}

public static class TenantDatabase
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var contexts = services.GetRequiredService<TenantScopes>();
        var first = services.GetRequiredService<TenantDirectory>().Tenants.First(t => t.IsActive);
        using var tenant = contexts.ForTenant(first.Id).Enter();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.MigrateAsync(ct);
    }
}

public sealed class TenantDesignFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();
        var directory = new TenantDirectory(config);
        var scopes = new TenantScopes(directory);
        using var tenant = scopes.ForTenant(directory.Tenants.First(t => t.IsActive).Id).Enter();
        return new(new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(config.GetConnectionString("Approval")).Options, scopes);
    }
}
