using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

internal static class ApprovalDefinitionAvailability
{
    internal static IQueryable<ApprovalDefinition> ForNewStarts(DbContext db) => db.Set<ApprovalDefinition>().Where(d => d.IsPublished &&
        db.Set<ApprovalDefinitionManagement>().Any(m => m.Key == d.Key && m.IsEnabled && m.ActiveDefinitionId == d.Id));

    internal static IQueryable<ApprovalDefinition> LatestPublished(DbContext db) => db.Set<ApprovalDefinition>().Where(d =>
        d.IsPublished && d.Version == db.Set<ApprovalDefinition>().Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version));
}
