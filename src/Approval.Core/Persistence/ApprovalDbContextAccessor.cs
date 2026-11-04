using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Persistence;

/// <summary>引擎通过它访问宿主 DbContext（由 AddApprovalWorkflow&lt;TContext&gt; 注册）。</summary>
public sealed class ApprovalDbContextAccessor(DbContext context,
    Approval.Core.Authorization.ApprovalAuthorizationService authorization,
    Approval.Core.Abstractions.IApprovalCommandExecutor commands,
    Approval.Core.Abstractions.IApprovalActorContext actor)
{
    internal ApprovalUnitOfWork UnitOfWork { get; } = new(context);
    internal Approval.Core.Abstractions.IApprovalCommandExecutor Commands { get; } = commands;
    internal string ActorId => actor.ActorId;
    internal bool IsSystem => actor.IsSystem;
    internal Approval.Core.Authorization.IApprovalAuthorization Authorization { get; set; } = authorization.Inner;
    public DbContext Context { get; } = context;
}
