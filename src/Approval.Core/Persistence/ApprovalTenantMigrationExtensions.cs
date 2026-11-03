using Microsoft.EntityFrameworkCore.Migrations;

namespace Approval.Core.Persistence;

public static class ApprovalTenantMigrationExtensions
{
    /// <summary>维护窗口内，在增加 NOT NULL 和同租户关联前执行。未映射数据使迁移失败，不猜测归属。</summary>
    public static void RequireApprovalTenantMapping(this MigrationBuilder migration, string prefix = "Approval")
    {
        if (prefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))) throw new ArgumentException("无效表前缀", nameof(prefix));
        var provider = migration.ActiveProvider ?? throw new InvalidOperationException("迁移缺少 Provider");
        string Quote(string name) => provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) ? $"`{name}`" : $"\"{name}\"";
        const string guard = "__ApprovalTenantMappingGuard";
        var create = $"CREATE TABLE {Quote(guard)} ({Quote("Value")} int NOT NULL)";
        migration.Sql(provider.Contains("SqlServer", StringComparison.Ordinal)
            ? $"IF OBJECT_ID(N'{guard}', N'U') IS NULL {create}"
            : create.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS"));
        foreach (var suffix in new[] { "Definitions", "Instances", "Tasks", "Logs", "CarbonCopies", "Drafts", "Files", "Messages", "Outbox" })
        {
            var column = Quote("TenantId");
            var invalid = provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) ? $"NOT REGEXP_LIKE({column}, '^[a-z0-9._-]{{1,64}}$', 'c')"
                : provider.Contains("Npgsql", StringComparison.Ordinal) ? $"{column} !~ '^[a-z0-9._-]{{1,64}}$'"
                : provider.Contains("SqlServer", StringComparison.Ordinal) ? $"DATALENGTH({column}) NOT BETWEEN 2 AND 128 OR {column} COLLATE Latin1_General_100_BIN2 LIKE '%[^a-z0-9._-]%'"
                : $"length({column}) NOT BETWEEN 1 AND 64 OR {column} GLOB '*[^a-z0-9._-]*'";
            migration.Sql($"INSERT INTO {Quote(guard)} ({Quote("Value")}) SELECT NULL FROM {Quote(prefix + suffix)} WHERE {column} IS NULL OR {column} = '' OR {invalid}");
        }
        foreach (var (child, foreign, parent) in new[]
        {
            ("Instances", "DefinitionId", "Definitions"), ("Tasks", "InstanceId", "Instances"), ("Logs", "InstanceId", "Instances"),
            ("CarbonCopies", "InstanceId", "Instances"), ("Files", "InstanceId", "Instances"), ("Files", "DraftId", "Drafts"),
            ("Messages", "InstanceId", "Instances"), ("Outbox", "InstanceId", "Instances")
        })
            migration.Sql($"INSERT INTO {Quote(guard)} ({Quote("Value")}) SELECT NULL FROM {Quote(prefix + child)} c LEFT JOIN {Quote(prefix + parent)} p ON c.{Quote(foreign)} = p.{Quote("Id")} WHERE c.{Quote(foreign)} IS NOT NULL AND (p.{Quote("Id")} IS NULL OR c.{Quote("TenantId")} <> p.{Quote("TenantId")})");
        migration.Sql($"DROP TABLE {Quote(guard)}");
    }
}
