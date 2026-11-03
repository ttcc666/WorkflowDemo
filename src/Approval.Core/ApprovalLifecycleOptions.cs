using Approval.Core.Domain;

namespace Approval.Core;

public sealed class ApprovalLifecycleOptions
{
    public bool Enabled { get; set; }
    public bool AllowDeletion { get; set; }
    public TimeSpan ArchiveAfter { get; set; } = TimeSpan.FromDays(365);
    public TimeSpan? PurgeAfter { get; set; }
    public TimeSpan? ReadMessagesAfter { get; set; }
    public TimeSpan? SucceededOutboxAfter { get; set; }
    /// <summary>失败消息只生成归档快照，不按年龄删除。</summary>
    public TimeSpan? FailedOutboxAfter { get; set; }
    public TimeSpan? BusinessDraftsAfter { get; set; }
    public TimeSpan? DesignDraftsAfter { get; set; }
    /// <summary>清除结果正文但永久保留键及请求摘要。</summary>
    public TimeSpan? OperationsAfter { get; set; }
    public TimeSpan? UnboundFilesAfter { get; set; }
    public TimeSpan? ArchivedPayloadsAfter { get; set; }
    public int BatchSize { get; set; } = 50;
    public int MaxBatchesPerRun { get; set; } = 10;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan? Retention(MaintenanceTarget target, MaintenanceMode mode) => target switch
    {
        MaintenanceTarget.Instances => mode == MaintenanceMode.Archive ? ArchiveAfter : PurgeAfter,
        MaintenanceTarget.ReadMessages => ReadMessagesAfter,
        MaintenanceTarget.SucceededOutbox => SucceededOutboxAfter,
        MaintenanceTarget.FailedOutbox => FailedOutboxAfter,
        MaintenanceTarget.BusinessDrafts => BusinessDraftsAfter,
        MaintenanceTarget.DesignDrafts => DesignDraftsAfter,
        MaintenanceTarget.Operations => OperationsAfter,
        MaintenanceTarget.UnboundFiles => UnboundFilesAfter,
        MaintenanceTarget.ArchivedPayloads => ArchivedPayloadsAfter,
        _ => null
    };
}
public sealed class ApprovalBackgroundOptions
{
    public int AssignmentSampleSize { get; set; } = 50;
}
