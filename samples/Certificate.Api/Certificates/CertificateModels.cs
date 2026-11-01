using Approval.Core.Persistence;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public enum CertificateStatus { Active, Revoked }

public enum CertOperation { Create, Update, Revoke }

public enum RequestStatus { Reviewing, Approved, Rejected, Withdrawn, Terminated }

/// <summary>正式证书：只有审批通过后才会被修改。</summary>
public class CertificateEntity
{
    public Guid Id { get; set; }
    public required string CertNo { get; set; }
    // 旧库升级期间可以为空；就绪检查会阻止未完成回填的数据库承接业务。
    public string? NormalizedCertNo { get; set; }
    public string? RegisteredBy { get; set; }
    public required string Name { get; set; }
    public required string Holder { get; set; }
    public required string CertType { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpireDate { get; set; }
    public CertificateStatus Status { get; set; }

    /// <summary>进行中的申请单（编辑锁）。</summary>
    public Guid? PendingRequestId { get; set; }

    public long ConcurrencyStamp { get; set; }
}

/// <summary>
/// 证书变更申请单：审核期间拟变更的数据存放在 Snapshot 中，审批的对象就是这张单据。
/// 与审批实例的关联：BusinessType = "Certificate"，BusinessKey = 申请单 Id。
/// </summary>
public class CertificateRequest
{
    public Guid Id { get; set; }

    /// <summary>新增时为空，审批通过后回填。</summary>
    public Guid? CertificateId { get; set; }

    public CertOperation Operation { get; set; }
    public required CertificateData Snapshot { get; set; }
    public CertificateData? BeforeSnapshot { get; set; }
    public string? NormalizedCertNo { get; set; }
    public Guid? ApprovalInstanceId { get; set; }
    public Guid? SourceRequestId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public long ConcurrencyStamp { get; set; }
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; }
    public required string ApplicantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>证号从审核中到正式登记的连续占用；主键约束是并发防重的最终保证。</summary>
public class CertificateNumberReservation
{
    public required string NormalizedCertNo { get; set; }
    public Guid? ActiveRequestId { get; set; }
    public Guid? CertificateId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record CertificateData(
    string CertNo, string Name, string Holder, string CertType, DateOnly IssueDate, DateOnly ExpireDate)
{
    public static CertificateData From(CertificateEntity c) => new(c.CertNo, c.Name, c.Holder, c.CertType, c.IssueDate, c.ExpireDate);

    public void ApplyTo(CertificateEntity c)
    {
        c.CertNo = CertNo;
        c.Name = Name;
        c.Holder = Holder;
        c.CertType = CertType;
        c.IssueDate = IssueDate;
        c.ExpireDate = ExpireDate;
    }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CertificateEntity> Certificates => Set<CertificateEntity>();
    public DbSet<CertificateRequest> CertificateRequests => Set<CertificateRequest>();
    public DbSet<CertificateNumberReservation> CertificateNumberReservations => Set<CertificateNumberReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 审批表与业务表共用同一个 DbContext，审批状态与业务数据在同一事务中提交
        modelBuilder.ConfigureApproval();

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
