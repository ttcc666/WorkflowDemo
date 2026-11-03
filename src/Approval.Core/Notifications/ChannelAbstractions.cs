using Approval.Core.Definitions;

namespace Approval.Core.Notifications;

/// <summary>投递给外部渠道的一条消息。</summary>
/// <param name="Id">消息 ID（Outbox ID），重试时不变，可作为幂等键。</param>
/// <param name="Recipients">接收人用户 ID（角色已展开）。</param>
public sealed record ChannelMessage(
    Guid Id,
    NotifyEvent Event,
    Guid? InstanceId,
    string? NodeId,
    string Title,
    string Content,
    IReadOnlyList<string> Recipients,
    string? Link);

/// <summary>
/// 宿主通知适配器。消息先写入 Outbox，事务提交后由后台任务调用 <see cref="SendAsync"/>，
/// 抛出异常视为失败并按退避策略重试。站内信（<see cref="InAppChannel.Name"/>）由框架内置，不需要实现本接口。
/// </summary>
public interface IApprovalChannel
{
    /// <summary>渠道名，流程定义中通知规则的 Channels 引用此名称。</summary>
    string Name { get; }

    string DisplayName { get; }

    Task SendAsync(ChannelMessage message, CancellationToken ct);
}

public static class InAppChannel
{
    public const string Name = "InApp";
    public const string DisplayName = "站内信";
}

public sealed record ChannelInfo(string Name, string DisplayName);
