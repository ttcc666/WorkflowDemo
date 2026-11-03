using Approval.Core;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Approval.Forms.Runtime;
using Approval.Forms.Attachments;
using Microsoft.EntityFrameworkCore;
namespace Approval.Forms;
public static class ApprovalFormsModelBuilderExtensions
{
    public static ModelBuilder ConfigureApprovalForms(this ModelBuilder modelBuilder, string tablePrefix = "Approval")
    {
        ApprovalWriteAuthorization.RegisterForms(typeof(ApprovalDraft), typeof(ApprovalFile));
        modelBuilder.Entity<ApprovalDraft>(b =>
        {
            b.ToTable($"{tablePrefix}Drafts");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).HasMaxLength(100);
            b.Property(x => x.DefinitionKey).HasMaxLength(100);
            b.Property(x => x.Comment).HasMaxLength(500);
            b.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });
        modelBuilder.Entity<ApprovalFile>(b =>
        {
            b.ToTable($"{tablePrefix}Files");
            b.HasKey(x => x.Id);
            b.Property(x => x.FileName).HasMaxLength(200);
            b.Property(x => x.ContentType).HasMaxLength(100);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.Property(x => x.StorageKey).HasMaxLength(200);
            b.Property(x => x.UploadedBy).HasMaxLength(100);
            b.HasIndex(x => x.InstanceId);
            b.HasIndex(x => x.DraftId);
            // 支持宿主按上传时间查询附件；审批 Worker 不清理文件。
            b.HasIndex(x => x.UploadedAt);
        });
        modelBuilder.Entity<ApprovalFile>().HasOne<ApprovalInstance>().WithMany()
            .HasForeignKey(x => new { x.InstanceId }).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ApprovalFile>().HasOne<ApprovalDraft>().WithMany()
            .HasForeignKey(x => new { x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ApprovalFile>().Property(f => f.ConcurrencyStamp).IsConcurrencyToken();
        modelBuilder.Entity<ApprovalFile>().UseUtcDateTimes();
        modelBuilder.Entity<ApprovalDraft>().UseUtcDateTimes();
        return modelBuilder;
    }
}
