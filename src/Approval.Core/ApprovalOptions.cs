namespace Approval.Core;

public sealed class ApprovalOptions
{
    public ApprovalLifecycleOptions Lifecycle { get; } = new();
    public ApprovalBackgroundOptions Background { get; } = new();
    /// <summary>严格模式要求提交标识；默认 false 是旧客户端过渡模式，不提供无键请求防重保障。</summary>
    public bool RequireOperationId { get; set; }
    public TimeSpan CommitVerificationTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public string AdminRole { get; set; } = "Admin";
    public List<string> DefinitionReaders { get; } = [];
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

    public ApprovalOutboxOptions Outbox { get; } = new();

    public ApprovalFileOptions Files { get; } = new();

    /// <summary>超时扫描间隔。</summary>
    public TimeSpan TimeoutScanInterval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>旧定义的超时自动审批仅在宿主显式允许时执行；默认只提醒。</summary>
    public bool EnableAutomaticTimeoutDecisions { get; set; }
}

/// <summary>表单附件：本地存储目录、大小与扩展名限制、未绑定文件的保留时间。</summary>
public sealed class ApprovalFileOptions
{
    /// <summary>本地存储目录（<see cref="Files.LocalApprovalFileStore"/>）；相对路径相对于内容根目录。不要放在 wwwroot 下。</summary>
    public string RootPath { get; set; } = "App_Data/approval-files";

    public int MaxFileSizeMb { get; set; } = 20;

    /// <summary>允许上传的扩展名（不含点，不区分大小写）。</summary>
    public List<string> AllowedExtensions { get; set; } =
        ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "png", "jpg", "jpeg", "gif", "zip"];

    /// <summary>上传后未绑定到草稿或实例的文件保留多久后清理。</summary>
    public TimeSpan UnboundRetention { get; set; } = TimeSpan.FromHours(24);

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}

public sealed class ApprovalOutboxOptions
{
    /// <summary>没有新消息时的轮询间隔；审批操作写入消息后会立即唤醒投递任务。</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 50;

    /// <summary>最多投递次数（含第一次），超过后标记为失败，可在投递记录中手动重试。</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>第 n 次失败后等待 BaseRetryDelay × 2^(n-1)，不超过 MaxRetryDelay。</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

}
