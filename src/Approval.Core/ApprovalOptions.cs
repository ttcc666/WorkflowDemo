namespace Approval.Core;

public sealed class ApprovalOptions
{
    public ApprovalBackgroundOptions Background { get; } = new();

    public ApprovalStatisticsOptions Statistics { get; } = new();

    public TimeSpan CommitVerificationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>是否允许发起人审批自己的申请（职责分离，默认不允许）。</summary>
    public bool AllowInitiatorApprove { get; set; }

    /// <summary>是否允许同一人在同一流程中审批多个节点（默认允许）。</summary>
    public bool AllowSameUserMultipleNodes { get; set; } = true;

    /// <summary>同一业务单据（BusinessType + BusinessKey）同时只能有一个进行中的流程。</summary>
    public bool SingleRunningInstancePerBusiness { get; set; } = true;

    /// <summary>流程结束时找不到对应 BusinessType 的业务回调是否报错。</summary>
    public bool RequireBusinessHandler { get; set; }

    /// <summary>已有人审批通过后发起人是否仍可撤回（默认不可以）。</summary>
    public bool AllowWithdrawAfterApproval { get; set; }

    /// <summary>模板全局变量，如 <c>appBaseUrl</c>，可在消息模板中以 <c>{appBaseUrl}</c> 引用。</summary>
    public Dictionary<string, string> GlobalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>审批详情页地址格式，<c>{0}</c> 为实例 ID；设置后模板可使用 <c>{链接}</c>。</summary>
    public string? InstanceUrlFormat { get; set; }

    /// <summary>消息中时间的显示时区（数据库统一存 UTC）。</summary>
    public TimeZoneInfo DisplayTimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>是否注册宿主中的单个后台执行器（Outbox 投递、超时扫描）。</summary>
    public bool EnableBackgroundServices { get; set; } = true;

    /// <summary>宿主提供的通知路由提示；不注册任何发送器。</summary>
    public List<Notifications.ChannelInfo> NotificationTargets { get; } = [];

    public ApprovalOutboxOptions Outbox { get; } = new();

    /// <summary>超时扫描间隔。</summary>
    public TimeSpan TimeoutScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>超时自动审批仅在宿主显式允许时执行；默认只提醒。</summary>
    public bool EnableAutomaticTimeoutDecisions { get; set; }
}

/// <summary>统计读取预算；超限时拒绝整次查询，避免返回不完整指标。</summary>
public sealed class ApprovalStatisticsOptions
{
    public int MaxTaskRows { get; set; } = 250_000;
    public int MaxBacklogRows { get; set; } = 100_000;

    internal void Validate()
    {
        if (MaxTaskRows is <= 0 or int.MaxValue || MaxBacklogRows is <= 0 or int.MaxValue)
            throw new InvalidOperationException("统计任务与积压读取预算必须大于零且小于 int.MaxValue");
    }
}

public sealed class ApprovalOutboxOptions
{
    /// <summary>是否允许后台或手动投递。关闭时事件仍持久化为 Pending，宿主不会收到事件。</summary>
    public bool EnableDispatcher { get; set; } = true;

    /// <summary>没有新消息时的轮询间隔；审批操作写入消息后会立即唤醒投递任务。</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 50;

    /// <summary>最多投递次数（含第一次），超过后标记为失败，可在投递记录中手动重试。</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>第 n 次失败后等待 BaseRetryDelay × 2^(n-1)，不超过 MaxRetryDelay。</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
}
