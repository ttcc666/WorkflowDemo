using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public enum CertificateStatus { Active, Revoked }

public enum CertOperation { Create, Update, Revoke }

public enum RequestStatus { Reviewing, Approved, Rejected, Withdrawn }

/// <summary>正式证书：只有审批通过后才会被修改。</summary>
public class CertificateEntity
{
    public Guid Id { get; set; }
    public required string CertNo { get; set; }
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
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; }
    public required string ApplicantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 审批表与业务表共用同一个 DbContext，审批状态与业务数据在同一事务中提交
        modelBuilder.ConfigureApproval();

        modelBuilder.Entity<CertificateEntity>(b =>
        {
            b.ToTable("Certificates");
            b.HasIndex(x => x.CertNo).IsUnique();
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<CertificateRequest>(b =>
        {
            b.ToTable("CertificateRequests");
            b.Property(x => x.Operation).HasConversion<string>();
            b.Property(x => x.Status).HasConversion<string>();
            b.OwnsOne(x => x.Snapshot, s => s.ToJson());
            b.UseUtcDateTimes();
        });
    }
}
