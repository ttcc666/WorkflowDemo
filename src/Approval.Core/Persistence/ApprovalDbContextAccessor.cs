using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Persistence;

/// <summary>引擎通过它访问宿主 DbContext（由 AddApprovalWorkflow&lt;TContext&gt; 注册）。</summary>
public sealed class ApprovalDbContextAccessor(DbContext context)
{
    public DbContext Context { get; } = context;
}
