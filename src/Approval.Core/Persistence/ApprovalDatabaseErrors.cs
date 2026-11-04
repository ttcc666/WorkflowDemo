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
        if (exception is DbUpdateConcurrencyException || instances.Length != 1 ||
            (instanceId is { } expected && instances[0] != expected)) return false;
        instanceId = instances[0];
        var entity = db.Model.FindEntityType(typeof(ApprovalLog));
        var table = entity?.GetTableName();
        var index = entity?.GetIndexes().FirstOrDefault(i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(ApprovalLog.InstanceId), nameof(ApprovalLog.Sequence)]))?.GetDatabaseName();
        if (entity is null || table is null || index is null) return false;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var columns = new[] { nameof(ApprovalLog.InstanceId), nameof(ApprovalLog.Sequence) }
            .Select(p => table + "." + entity.FindProperty(p)!.GetColumnName(store)).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is not DbException) continue;
            var type = current.GetType();
            if (type.GetProperty("SqlState")?.GetValue(current) is "23505")
                return Equals(type.GetProperty("ConstraintName")?.GetValue(current), index) &&
                    Equals(type.GetProperty("TableName")?.GetValue(current), table);
            if (type.GetProperty("Number")?.GetValue(current) is int number)
            {
                if (number == 1062)
                {
                    var key = Regex.Match(current.Message, "for key ['`](?<key>[^'`]+)['`]", RegexOptions.CultureInvariant).Groups["key"].Value;
                    var parts = key.Split('.');
                    return parts[^1].Equals(index, StringComparison.OrdinalIgnoreCase) &&
                        (parts.Length >= 2 ? parts[^2].Equals(table, StringComparison.OrdinalIgnoreCase) :
                            exception.Entries.All(e => e.Entity is ApprovalLog log && log.InstanceId == instanceId));
                }
                if (number is 2601 or 2627)
                {
                    var named = Regex.Match(current.Message, "(?:unique index|UNIQUE KEY constraint) '(?<index>[^']+)'",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["index"].Value;
                    var target = Regex.Match(current.Message, "object '(?<table>[^']+)'",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["table"].Value.Split('.')[^1];
                    return named.Equals(index, StringComparison.OrdinalIgnoreCase) && target.Equals(table, StringComparison.OrdinalIgnoreCase);
                }
            }
            if (type.GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is 2067)
            {
                var failed = Regex.Match(current.Message, "UNIQUE constraint failed: (?<columns>[^'\\r\\n]+)",
                    RegexOptions.CultureInvariant).Groups["columns"].Value.Split(',').Select(c => c.Trim()).Order(StringComparer.OrdinalIgnoreCase);
                return failed.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase);
            }
        }
        return false;
    }

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
