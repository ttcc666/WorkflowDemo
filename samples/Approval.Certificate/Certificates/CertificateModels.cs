namespace Certificate.Api.Certificates;

public enum CertificateStatus { Active, Revoked }

public enum CertOperation { Create, Update, Revoke }

public enum RequestStatus { Reviewing, Approved, Rejected, Withdrawn, Terminated }

/// <summary>正式证书：只有审批通过后才会被修改。</summary>
public class CertificateEntity
{
    public Guid Id { get; set; }
    public required string CertNo { get; set; }
    // 由宿主服务规范化后保存；就绪检查验证持久化的一致性。
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
