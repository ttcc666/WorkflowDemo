using Microsoft.EntityFrameworkCore;
namespace Approval.Core.Persistence;
public interface IApprovalHostContext { DbContext Context { get; } }
internal sealed class ApprovalHostContext<TContext>(TContext db) : IApprovalHostContext where TContext : DbContext
{ public DbContext Context => db; }
