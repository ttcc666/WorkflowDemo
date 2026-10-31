using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Approval.Core.Persistence;

public static class ApprovalModelBuilderExtensions
{
    /// <summary>
    /// 在宿主 DbContext 的 OnModelCreating 中调用，将审批表加入宿主模型，
    /// 使审批状态与业务数据共用同一个 DbContext 和事务。
    /// </summary>
    public static ModelBuilder ConfigureApproval(this ModelBuilder modelBuilder, string tablePrefix = "Approval")
    {
        modelBuilder.Entity<ApprovalDefinition>(b =>
        {
            b.ToTable($"{tablePrefix}Definitions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(100);
            b.Property(x => x.Name).HasMaxLength(200);
            b.HasIndex(x => new { x.Key, x.Version }).IsUnique();
        });

        modelBuilder.Entity<ApprovalInstance>(b =>
        {
            b.ToTable($"{tablePrefix}Instances");
            b.HasKey(x => x.Id);
            b.Property(x => x.DefinitionKey).HasMaxLength(100);
            b.Property(x => x.BusinessType).HasMaxLength(100);
            b.Property(x => x.BusinessKey).HasMaxLength(100);
            b.Property(x => x.Title).HasMaxLength(500);
            b.Property(x => x.InitiatorId).HasMaxLength(100);
            b.Property(x => x.CurrentNodeId).HasMaxLength(100);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.HasIndex(x => new { x.BusinessType, x.BusinessKey });
            b.HasIndex(x => new { x.InitiatorId, x.Status });
            b.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Logs).WithOne().HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalTask>(b =>
        {
            b.ToTable($"{tablePrefix}Tasks");
            b.HasKey(x => x.Id);
            b.Property(x => x.NodeId).HasMaxLength(100);
            b.Property(x => x.NodeName).HasMaxLength(200);
            b.Property(x => x.AssigneeValue).HasMaxLength(100);
            b.Property(x => x.HandledBy).HasMaxLength(100);
            b.Property(x => x.AssigneeKind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // 待办查询：按处理人 + 状态
            b.HasIndex(x => new { x.AssigneeKind, x.AssigneeValue, x.Status });
            // 超时扫描
            b.HasIndex(x => new { x.Status, x.NextRemindAt });
        });

        modelBuilder.Entity<ApprovalLog>(b =>
        {
            b.ToTable($"{tablePrefix}Logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.NodeId).HasMaxLength(100);
            b.Property(x => x.NodeName).HasMaxLength(200);
            b.Property(x => x.OperatorId).HasMaxLength(100);
            b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.InstanceId, x.Sequence }).IsUnique();
            b.HasIndex(x => new { x.OperatorId, x.Action });
        });

        modelBuilder.Entity<ApprovalMessage>(b =>
        {
            b.ToTable($"{tablePrefix}Messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).HasMaxLength(100);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Content).HasMaxLength(4000);
            b.Property(x => x.NodeId).HasMaxLength(100);
            b.Property(x => x.Event).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
        });

        modelBuilder.Entity<ApprovalOutboxMessage>(b =>
        {
            b.ToTable($"{tablePrefix}Outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Target).HasMaxLength(100);
            b.Property(x => x.NodeId).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            // 后台投递：按状态 + 下次执行时间取到期消息
            b.HasIndex(x => new { x.Status, x.NextAttemptAt });
            b.HasIndex(x => x.InstanceId);
        });

        foreach (var type in new[]
                 {
                     typeof(ApprovalDefinition), typeof(ApprovalInstance), typeof(ApprovalTask), typeof(ApprovalLog),
                     typeof(ApprovalMessage), typeof(ApprovalOutboxMessage)
                 })
            modelBuilder.Entity(type).UseUtcDateTimes();

        return modelBuilder;
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    /// <summary>
    /// 读取时把 DateTime 标记为 UTC。SQLite 等数据库不保存 Kind，否则序列化结果不带 "Z"，前端会把 UTC 误当本地时间。
    /// </summary>
    public static EntityTypeBuilder UseUtcDateTimes(this EntityTypeBuilder builder)
    {
        foreach (var property in builder.Metadata.GetProperties())
        {
            if (property.ClrType == typeof(DateTime)) property.SetValueConverter(UtcConverter);
            else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(NullableUtcConverter);
        }
        return builder;
    }
}
