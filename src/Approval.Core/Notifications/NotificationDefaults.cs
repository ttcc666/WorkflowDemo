using Approval.Core.Definitions;

namespace Approval.Core.Notifications;

/// <summary>通知规则未填写标题 / 内容时使用的默认模板，以及通知专用的接收人类型。</summary>
public static class NotificationDefaults
{
    /// <summary>接收人类型：事件对应的当前审批人（到达节点时为新待办的处理人，转交时为转交对象，超时为超时待办的处理人）。</summary>
    public const string AssigneesRecipient = "Assignees";

    /// <summary>接收人类型：本流程中已审批同意过的人。</summary>
    public const string ApproversRecipient = "Approvers";

    public static IReadOnlyList<string> NotificationRecipientTypes { get; } = [AssigneesRecipient, ApproversRecipient];

    public static IReadOnlyDictionary<NotifyEvent, string> Titles { get; } = new Dictionary<NotifyEvent, string>
    {
        [NotifyEvent.Enter] = "待审批：{标题}",
        [NotifyEvent.Approved] = "审批进度：{标题}",
        [NotifyEvent.Rejected] = "审批驳回：{标题}",
        [NotifyEvent.Transferred] = "转交待办：{标题}",
        [NotifyEvent.Timeout] = "审批催办：{标题}",
        [NotifyEvent.Completed] = "审批{结果}：{标题}"
    };

    public static IReadOnlyDictionary<NotifyEvent, string> Contents { get; } = new Dictionary<NotifyEvent, string>
    {
        [NotifyEvent.Enter] = "【待审批】{标题}，申请人 {申请人}，当前环节：{节点}",
        [NotifyEvent.Approved] = "【已同意】{标题}，{操作人} 同意了 {节点}",
        [NotifyEvent.Rejected] = "【已驳回】{标题}，{操作人} 在 {节点} 驳回：{意见}",
        [NotifyEvent.Transferred] = "【转交】{标题}，{操作人} 将 {节点} 转交给你",
        [NotifyEvent.Timeout] = "【催办】{标题} 在 {节点} 已超过处理时限（截止 {截止时间}），请尽快处理",
        [NotifyEvent.Completed] = "【{结果}】{标题}"
    };

    /// <summary>抄送节点未配置通知规则时，默认给抄送人发站内信。</summary>
    public static NotificationRule CcRule { get; } = new()
    {
        Events = [NotifyEvent.Enter],
        Recipients = [new ApproverRule(AssigneesRecipient, "")],
        Channels = [InAppChannel.Name],
        Title = "抄送：{标题}",
        Content = "【抄送】{标题}，申请人 {申请人}"
    };

    /// <summary>节点设置了“超时提醒”但没有配置 Timeout 通知规则时，默认给超时待办的处理人发站内信。</summary>
    public static NotificationRule TimeoutRule { get; } = new()
    {
        Events = [NotifyEvent.Timeout],
        Recipients = [new ApproverRule(AssigneesRecipient, "")],
        Channels = [InAppChannel.Name]
    };

    /// <summary>各类节点允许配置的通知事件；流程级规则允许全部事件（节点事件对所有审批节点生效）。</summary>
    public static IReadOnlyList<NotifyEvent> AllowedEvents(NodeType? nodeType) => nodeType switch
    {
        null or NodeType.Approval => Enum.GetValues<NotifyEvent>(),
        NodeType.Cc or NodeType.Action => [NotifyEvent.Enter],
        NodeType.End => [NotifyEvent.Completed],
        _ => []
    };
}
