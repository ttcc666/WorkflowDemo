using Approval.Core.Definitions;

namespace Approval.Core.Notifications;

/// <summary>投递给外部渠道的一条消息。</summary>
/// <param name="Id">消息 ID（Outbox ID），重试时不变，可作为幂等键。</param>
/// <param name="Recipients">接收人用户 ID（角色已展开）。</param>
public sealed record ApprovalNotificationRequest(
    Guid Id,
    NotifyEvent Event,
    Guid? InstanceId,
    string? NodeId,
    string Title,
    string Content,
    IReadOnlyList<string> Recipients,
    string? Link);

public static class InAppChannel
{
    public const string Name = "InApp";
    public const string DisplayName = "站内信";
}

public sealed record ChannelInfo(string Name, string DisplayName);
