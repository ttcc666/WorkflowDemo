using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Persistence;

/// <summary>引擎通过它访问宿主 DbContext（由 AddApprovalWorkflow&lt;TContext&gt; 注册）。</summary>
public sealed class ApprovalDbContextAccessor(ApprovalDbContext context,
    Approval.Core.Authorization.ApprovalAuthorizationService? authorization = null,
    Approval.Core.Abstractions.IApprovalCommandExecutor? commands = null,
    Approval.Core.Abstractions.IApprovalActorContext? actor = null)
{
    internal ApprovalUnitOfWork UnitOfWork { get; } = new(context);
    internal Approval.Core.Abstractions.IApprovalCommandExecutor? Commands { get; } = commands;
    internal string ActorId => actor?.ActorId ?? throw new InvalidOperationException("治理操作需要可信操作人");
    internal bool IsSystem => actor?.IsSystem == true;
    internal Approval.Core.Authorization.IApprovalAuthorization Authorization { get; set; } = authorization?.Inner ?? Approval.Core.Authorization.MissingApprovalAuthorization.Instance;
    public DbContext Context { get; } = context;
}
