using System.Data.Common;

namespace Approval.Core.Persistence;

internal static class ApprovalDatabaseErrors
{
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
