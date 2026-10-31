using Approval.Core.Definitions;

namespace Approval.Core.Domain;

/// <summary>站内信，每个接收人一条；与审批数据在同一事务中写入。</summary>
public class ApprovalMessage
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public NotifyEvent Event { get; set; }
    public Guid? InstanceId { get; set; }
    public string? NodeId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public enum OutboxKind
{
    /// <summary>外部渠道通知（Webhook、企业微信、钉钉、邮件等），Target 为渠道名。</summary>
    Notification,
    /// <summary>异步动作，Target 为动作类型。</summary>
    Action
}

public enum OutboxStatus { Pending, Succeeded, Failed }

/// <summary>
/// 待投递消息（Outbox）：与审批数据在同一事务中写入，事务提交后由后台任务投递，失败按指数退避重试。
/// 投递为“至少一次”，接收方可用 <see cref="Id"/> 作为幂等键去重。
/// </summary>
public class ApprovalOutboxMessage
{
    public Guid Id { get; set; }
    public OutboxKind Kind { get; set; }
    public required string Target { get; set; }
    public Guid? InstanceId { get; set; }
    public string? NodeId { get; set; }

    /// <summary>便于排查的说明，如消息标题、动作名称。</summary>
    public string? Description { get; set; }

    public required string PayloadJson { get; set; }
    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }

    /// <summary>下次投递时间；投递中时为租约到期时间（进程崩溃后到期自动重试）。</summary>
    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>乐观并发令牌：多个后台实例同时抢占同一条消息时只有一个成功。</summary>
    public long ConcurrencyStamp { get; set; }
}
