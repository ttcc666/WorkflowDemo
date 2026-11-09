using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace Approval.Core.Persistence;

internal static class ApprovalDatabaseErrors
{
    public static bool LogSequence(DbUpdateException exception, DbContext db, Guid? instanceId = null)
    {
        var instances = exception.Entries.Select(e => e.Entity).OfType<ApprovalLog>().Select(l => l.InstanceId).Distinct().ToArray();
        return instances.Length == 1 && (instanceId is null || instances[0] == instanceId)
            && UniqueConstraint<ApprovalLog>(exception, db, [nameof(ApprovalLog.InstanceId), nameof(ApprovalLog.Sequence)]);
    }

    public static bool OperationConflict(DbUpdateException error, DbContext db) =>
        UniqueConstraint<ApprovalOperation>(error, db, [nameof(ApprovalOperation.LookupHash)]);

    public static bool BusinessSlotConflict(DbUpdateException error, DbContext db) =>
        UniqueConstraint<ApprovalBusinessSlot>(error, db, [nameof(ApprovalBusinessSlot.LookupHash)])
        || UniqueConstraint<ApprovalBusinessSlot>(error, db, [nameof(ApprovalBusinessSlot.InstanceId)]);

    public static bool GovernanceConflict(DbUpdateException error, DbContext db) =>
        UniqueConstraint<ApprovalDefinition>(error, db, [nameof(ApprovalDefinition.Key), nameof(ApprovalDefinition.Version)])
        || UniqueConstraint<ApprovalDefinitionManagement>(error, db, [nameof(ApprovalDefinitionManagement.Key)])
        || UniqueConstraint<ApprovalDefinitionAudit>(error, db, [nameof(ApprovalDefinitionAudit.Key), nameof(ApprovalDefinitionAudit.Revision)]);

    public static bool CarbonCopyReadConflict(DbUpdateException error, DbContext db) =>
        UniqueConstraint<ApprovalCarbonCopyRead>(error, db, [nameof(ApprovalCarbonCopyRead.CarbonCopyId), nameof(ApprovalCarbonCopyRead.UserId)])
        || UniqueConstraint<ApprovalCarbonCopyRead>(error, db, [nameof(ApprovalCarbonCopyRead.Id)], primary: true);

    private static bool UniqueConstraint<T>(DbUpdateException error, DbContext db, string[] properties, bool primary = false)
    {
        if (error is DbUpdateConcurrencyException || !error.Entries.Any(e => e.Entity is T)) return false;
        var entity = db.Model.FindEntityType(typeof(T));
        if (!primary && entity?.FindAnnotation(ApprovalPartitionModelExtensions.PartitionAnnotation)?.Value is string partition)
            properties = [partition, .. properties];
        var table = entity?.GetTableName();
        var constraint = primary ? entity?.FindPrimaryKey()?.GetName() : entity?.GetIndexes()
            .FirstOrDefault(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(properties))?.GetDatabaseName();
        if (entity is null || table is null || constraint is null) return false;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var columns = properties.Select(p => table + "." + entity.FindProperty(p)!.GetColumnName(store))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();

        for (Exception? current = error.InnerException; current is not null; current = current.InnerException)
        {
            if (current is not DbException) continue;
            var type = current.GetType();
            if (type.GetProperty("SqlState")?.GetValue(current) is "23505")
                return Equals(type.GetProperty("ConstraintName")?.GetValue(current), constraint)
                    && Equals(type.GetProperty("TableName")?.GetValue(current), table)
                    && (entity.GetSchema() is not { } schema || Equals(type.GetProperty("SchemaName")?.GetValue(current), schema));
            if (type.GetProperty("Number")?.GetValue(current) is int number)
            {
                if (number == 1062)
                {
                    // MySQL ends the message with the quoted key. The preceding duplicate value is user data.
                    var key = Regex.Match(current.Message, "['`](?<id>[^'`\\r\\n]+)['`]\\s*$", RegexOptions.CultureInvariant)
                        .Groups["id"].Value.Split('.');
                    var index = primary ? "PRIMARY" : constraint;
                    return key[^1].Equals(index, StringComparison.OrdinalIgnoreCase)
                        && (key.Length >= 2 ? key[^2].Equals(table, StringComparison.OrdinalIgnoreCase)
                            : error.Entries.All(e => e.Entity is T));
                }
                // Drivers expose constraint identity in quoted tokens, not separate properties.
                // Require model identifiers, provider code and EF entries, not translated prose.
                var identifiers = Regex.Matches(current.Message, "['`](?<id>[^'`]+)['`]", RegexOptions.CultureInvariant)
                    .Select(m => m.Groups["id"].Value).ToArray();
                if (number is 2601 or 2627)
                    return identifiers.Contains(constraint, StringComparer.OrdinalIgnoreCase)
                        && identifiers.Any(id => id.Split('.').Last().Equals(table, StringComparison.OrdinalIgnoreCase));
            }
            if (type.GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is int code && code == (primary ? 1555 : 2067))
            {
                var failed = Regex.Match(current.Message, "UNIQUE constraint failed: (?<columns>[^'\\r\\n]+)",
                    RegexOptions.CultureInvariant).Groups["columns"].Value.Split(',').Select(c => c.Trim()).Order(StringComparer.OrdinalIgnoreCase);
                return failed.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    // Diagnostic predicate only; business translation requires a model-owned constraint above.
    public static bool Unique(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not DbException) continue;
            var type = current.GetType();
            if (type.GetProperty("SqlState")?.GetValue(current) is "23505") return true;
            if (type.GetProperty("Number")?.GetValue(current) is int number && number is 1062 or 2601 or 2627) return true;
            if (type.GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is int code && code is 1555 or 2067) return true;
        }
        return false;
    }
}
