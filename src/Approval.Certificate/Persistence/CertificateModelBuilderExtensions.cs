using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public static class CertificateModelBuilderExtensions
{
    public static void ConfigureCertificates(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CertificateEntity>(b =>
        {
            b.ToTable("Certificates");
            b.HasIndex(x => x.CertNo).IsUnique();
            b.Property(x => x.NormalizedCertNo).HasMaxLength(100);
            b.Property(x => x.RegisteredBy).HasMaxLength(100);
            b.HasIndex(x => x.NormalizedCertNo).IsUnique();
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<CertificateRequest>(b =>
        {
            b.ToTable("CertificateRequests");
            b.Property(x => x.Operation).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.OwnsOne(x => x.Snapshot, s => s.ToJson());
            b.OwnsOne(x => x.BeforeSnapshot, s => s.ToJson());
            b.Property(x => x.NormalizedCertNo).HasMaxLength(100);
            b.Property(x => x.IdempotencyKey).HasMaxLength(32);
            b.Property(x => x.RequestHash).HasMaxLength(64);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.HasIndex(x => new { x.ApplicantId, x.Operation, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.CertificateId, x.CreatedAt });
            b.HasIndex(x => x.ApprovalInstanceId).IsUnique();
            b.HasOne<ApprovalInstance>().WithMany().HasForeignKey(x => x.ApprovalInstanceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CertificateEntity>().WithMany().HasForeignKey(x => x.CertificateId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CertificateRequest>().WithMany().HasForeignKey(x => x.SourceRequestId).OnDelete(DeleteBehavior.Restrict);
            b.UseUtcDateTimes();
        });

        modelBuilder.Entity<CertificateNumberReservation>(b =>
        {
            b.ToTable("CertificateNumberReservations", t => t.HasCheckConstraint("CK_CertificateNumberReservation_Owner",
                "\"ActiveRequestId\" IS NOT NULL OR \"CertificateId\" IS NOT NULL"));
            b.HasKey(x => x.NormalizedCertNo);
            b.Property(x => x.NormalizedCertNo).HasMaxLength(100);
            b.HasIndex(x => x.ActiveRequestId).IsUnique();
            b.HasIndex(x => x.CertificateId).IsUnique();
            b.HasOne<CertificateRequest>().WithMany().HasForeignKey(x => x.ActiveRequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CertificateEntity>().WithMany().HasForeignKey(x => x.CertificateId).OnDelete(DeleteBehavior.Restrict);
            b.UseUtcDateTimes();
        });
    }
}
