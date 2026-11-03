namespace Approval.Core.Domain;

/// <summary>
/// 附件元数据，内容由 <see cref="Files.IApprovalFileStore"/> 保存。未绑定（草稿、实例都为空）时只有上传者可访问，
/// 超过保留时间后被后台清理；绑定实例后按实例的查看权限访问。
/// </summary>
public class ApprovalFile : IApprovalTenantEntity
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TenantId { get; set; } = string.Empty;

    public Guid Id { get; set; }

    /// <summary>清洗后的原始文件名（仅用于显示与下载时的文件名）。</summary>
    public required string FileName { get; set; }

    /// <summary>按扩展名确定的类型（不信任客户端提供的类型）。</summary>
    public required string ContentType { get; set; }

    public long Size { get; set; }

    /// <summary>SHA-256（十六进制小写）。</summary>
    public required string Sha256 { get; set; }

    public required string StorageKey { get; set; }
    public required string UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid? InstanceId { get; set; }
    public Guid? DraftId { get; set; }
    public Guid? ArchiveId { get; set; }
    public bool DeletionPending { get; set; }
    public long ConcurrencyStamp { get; set; }
}
