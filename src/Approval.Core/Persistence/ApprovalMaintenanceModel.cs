using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Persistence;

internal static class ApprovalMaintenanceModel
{
    internal static void Configure(ModelBuilder model, string prefix)
    {
        model.Entity<ApprovalArchiveRecord>(b =>
        {
            b.ToTable(prefix + "Archives"); b.HasKey(x => x.Id);
            b.Property(x => x.Target).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.DefinitionKey).HasMaxLength(100); b.Property(x => x.BusinessType).HasMaxLength(100);
            b.Property(x => x.BusinessKey).HasMaxLength(200); b.Property(x => x.UserId).HasMaxLength(256);
            b.Property(x => x.SnapshotHash).HasMaxLength(64); b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.HasIndex(x => new { x.TenantId, x.Target, x.ResourceId, x.SourceVersion }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.ArchivedAt });
        });
        model.Entity<ApprovalMaintenanceJob>(b =>
        {
            b.ToTable(prefix + "MaintenanceJobs"); b.HasKey(x => x.Id);
            b.Property(x => x.Target).HasConversion<string>().HasMaxLength(30); b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); b.Property(x => x.ActorId).HasMaxLength(256);
            b.Property(x => x.PolicyHash).HasMaxLength(64); b.Property(x => x.LastErrorCode).HasMaxLength(64);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        });
        model.Entity<ApprovalRetentionHold>(b =>
        {
            b.ToTable(prefix + "RetentionHolds"); b.HasKey(x => x.Id);
            b.Property(x => x.Target).HasConversion<string>().HasMaxLength(30); b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.ActorId).HasMaxLength(256); b.HasIndex(x => new { x.TenantId, x.Target, x.ResourceId }).IsUnique();
        });
        model.Entity<ApprovalFileDeletion>(b =>
        {
            b.ToTable(prefix + "FileDeletions"); b.HasKey(x => x.Id);
            b.Property(x => x.StorageKey).HasMaxLength(200); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.LastErrorCode).HasMaxLength(64); b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.HasIndex(x => new { x.TenantId, x.FileId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAt });
        });
        model.Entity<ApprovalMaintenanceAudit>(b =>
        {
            b.ToTable(prefix + "MaintenanceAudits"); b.HasKey(x => x.Id);
            b.Property(x => x.Target).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Action).HasMaxLength(30); b.Property(x => x.ActorId).HasMaxLength(256); b.Property(x => x.Reason).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.JobId, x.Batch }).IsUnique();
        });
        model.Entity<ApprovalFile>().Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        model.Entity<ApprovalOperation>().Property(x => x.IsTombstone).IsConcurrencyToken();
        model.Entity<ApprovalFile>().HasOne<ApprovalArchiveRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ArchiveId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
