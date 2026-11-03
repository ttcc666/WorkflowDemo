namespace Approval.Core.Domain;

/// <summary>角色抄送的每位接收者独立已读状态；与通知消息互不依赖。</summary>
public sealed class ApprovalCarbonCopyRead : IApprovalEntity
{
    public Guid Id { get; set; }
    public Guid CarbonCopyId { get; set; }
    public required string UserId { get; set; }
    public DateTime ReadAt { get; set; }
}
