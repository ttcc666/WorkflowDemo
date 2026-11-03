using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

internal static class ApprovalDefinitionAvailability
{
    internal static IQueryable<ApprovalDefinition> ForNewStarts(DbContext db) => db.Set<ApprovalDefinition>().Where(d => d.IsPublished &&
        db.Set<ApprovalDefinitionManagement>().Any(m => m.Key == d.Key && m.IsEnabled && m.ActiveDefinitionId == d.Id));
}
