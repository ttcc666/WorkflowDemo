namespace Approval.Forms.Attachments;

/// <summary>
/// 附件元数据，内容由 <see cref="IApprovalFileStore"/> 保存。上传者之外的访问要求实例查看权限和可见文件引用。
/// 实例绑定记录文件归属，供当前和历史引用使用。文件生命周期由宿主管理。
/// </summary>
public class ApprovalFile : Approval.Core.Abstractions.IApprovalTrackedRevision
{
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
    public long ConcurrencyStamp { get; set; }
}
