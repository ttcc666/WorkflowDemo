using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>归档为独立历史数据集；宿主须显式表达其查询范围，过滤必须先于分页和导出。</summary>
public interface IApprovalArchiveQueryScope
{
    Task<IQueryable<ApprovalArchiveRecord>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalArchiveRecord> query, CancellationToken ct);
}
