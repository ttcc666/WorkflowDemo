using Approval.Core.Abstractions;
using Approval.Core.Domain;

namespace Approval.Core.Authorization;

internal sealed class DefaultApprovalArchiveQueryScope(IApprovalQueryScope instances) : IApprovalArchiveQueryScope
{
    public Task<IQueryable<ApprovalArchiveRecord>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalArchiveRecord> query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A custom live-data scope cannot be reconstructed after the original row has been purged.
        // Require an explicit archive scope rather than silently broadening that user's data access.
        if (instances is not DefaultApprovalQueryScope)
            throw new ApprovalException("宿主使用了自定义实例范围，请显式配置归档数据范围") { Code = ApprovalErrorCode.AuthorizationUnavailable };
        return Task.FromResult(query);
    }
}
