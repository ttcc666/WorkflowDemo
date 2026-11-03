using System.Data;
using System.Data.Common;
using Approval.Core.Tenancy;

namespace Approval.Core.Persistence;

public sealed record ApprovalTenantMapping(string Table, Guid Id, string TenantId);
public sealed record ApprovalTenantMappingReport(IReadOnlyDictionary<string, int> Counts, IReadOnlyList<string> Problems, int Changed)
{
    public bool Ready => Problems.Count == 0;
    public IReadOnlyList<string> PotentialKeyCollisions { get; init; } = [];
}

/// <summary>离线维护工具。要求已执行 ExpandApprovalTenants、暂停旧服务并备份；不修改业务内容和实体 ID。</summary>
public sealed class ApprovalTenantBackfill(DbConnection connection, string provider, string tablePrefix = "Approval", bool singleRunningInstancePerBusiness = true)
{
    private static readonly string[] Suffixes = ["Definitions", "Instances", "Tasks", "Logs", "CarbonCopies", "Drafts", "Files", "Messages", "Outbox"];
    private string Quote(string name)
    {
        if (name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))) throw new ArgumentException("无效表名");
        return provider == "MySql" ? $"`{name}`" : $"\"{name}\"";
    }
    private sealed record Row(string Table, Guid Id, string? Existing, string Tenant, Dictionary<string, object?> Values);
    public Task<ApprovalTenantMappingReport> PreviewAsync(IReadOnlyList<ApprovalTenantMapping> mapping, string? fixedTenantId = null, CancellationToken ct = default)
        => RunAsync(mapping, fixedTenantId, false, ct);
    public Task<ApprovalTenantMappingReport> ApplyAsync(IReadOnlyList<ApprovalTenantMapping> mapping, string? fixedTenantId = null, CancellationToken ct = default)
        => RunAsync(mapping, fixedTenantId, true, ct);

    private async Task<ApprovalTenantMappingReport> RunAsync(IReadOnlyList<ApprovalTenantMapping> mapping, string? fixedTenant, bool apply, CancellationToken ct)
    {
        if (provider is not ("MySql" or "SqlServer" or "PostgreSql" or "Sqlite")) throw new ArgumentException("不支持的 Provider");
        if (fixedTenant is not null) ApprovalTenantIds.Validate(fixedTenant);
        if (fixedTenant is not null && mapping.Count > 0) throw new ArgumentException("不能混用整库租户映射和逐行映射");
        var map = new Dictionary<(string, Guid), string>();
        foreach (var item in mapping)
        {
            ApprovalTenantIds.Validate(item.TenantId);
            if (!Suffixes.Contains(item.Table, StringComparer.Ordinal)) throw new ArgumentException("映射表名须为已知表后缀");
            if (!map.TryAdd((item.Table, item.Id), item.TenantId)) throw new ArgumentException("存在重复行映射");
        }
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var transaction = apply ? await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var problems = new List<string>(); var rows = new List<Row>(); var counts = new Dictionary<string, int>();
            foreach (var table in Suffixes)
            {
                string[] extra = table switch
                {
                    "Definitions" => ["Key", "Version"],
                    "Instances" => ["DefinitionId", "BusinessType", "BusinessKey", "Status"],
                    "Tasks" or "Logs" or "CarbonCopies" or "Messages" or "Outbox" => ["InstanceId"],
                    "Files" => ["InstanceId", "DraftId"], _ => []
                };
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"SELECT {string.Join(",", new[] { "Id", "TenantId" }.Concat(extra).Select(Quote))} FROM {Quote(tablePrefix + table)}";
                await using var reader = await command.ExecuteReaderAsync(ct);
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count >= 100_000) throw new InvalidOperationException("超出维护工具单次 100000 行上限；请使用经审查的分批迁移适配器");
                    var id = Guid.Parse(Convert.ToString(reader.GetValue(0))!);
                    var existing = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var tenant = fixedTenant ?? map.GetValueOrDefault((table, id));
                    if (tenant is null) { problems.Add($"{table}/{id}: 缺少显式归属映射"); tenant = ""; }
                    if (!string.IsNullOrEmpty(existing) && existing != tenant) problems.Add($"{table}/{id}: 不允许重新分配已有租户");
                    var values = new Dictionary<string, object?>();
                    for (var i = 0; i < extra.Length; i++) values[extra[i]] = reader.IsDBNull(i + 2) ? null : reader.GetValue(i + 2);
                    rows.Add(new(table, id, existing, tenant, values)); count++;
                }
                counts[table] = count;
            }
            var lookup = rows.ToDictionary(r => (r.Table, r.Id));
            foreach (var key in map.Keys.Where(key => !lookup.ContainsKey(key))) problems.Add($"{key.Item1}/{key.Item2}: 映射引用不存在的行");
            foreach (var row in rows)
                foreach (var column in new[] { "DefinitionId", "InstanceId", "DraftId" })
                    if (row.Values.GetValueOrDefault(column) is { } reference)
                    {
                        var target = column == "DefinitionId" ? "Definitions" : column == "InstanceId" ? "Instances" : "Drafts";
                        if (!Guid.TryParse(Convert.ToString(reference), out var id) || !lookup.TryGetValue((target, id), out var parent))
                            problems.Add($"{row.Table}/{row.Id}: {column} 孤立关联");
                        else if (parent.Tenant != row.Tenant) problems.Add($"{row.Table}/{row.Id}: {column} 跨租户关联");
                    }
            foreach (var group in rows.Where(r => singleRunningInstancePerBusiness && r.Table == "Instances" && Convert.ToString(r.Values["Status"]) == "Running")
                         .GroupBy(r => (r.Tenant, Convert.ToString(r.Values["BusinessType"]), Convert.ToString(r.Values["BusinessKey"]))).Where(g => g.Count() > 1))
                problems.Add($"Instances: 租户 {group.Key.Tenant} 有重复进行中业务：{string.Join(",", group.Select(r => r.Id))}");
            var collisions = new List<string>();
            foreach (var group in rows.Where(r => r.Table == "Definitions").GroupBy(r => (r.Tenant,
                         Key: Convert.ToString(r.Values["Key"])!.Trim().ToUpperInvariant(), Version: Convert.ToInt32(r.Values["Version"])))
                         .Where(g => g.Select(r => Convert.ToString(r.Values["Key"])).Distinct(StringComparer.Ordinal).Count() > 1))
                collisions.Add($"Definitions: 大小写/空白归一化潜在碰撞：{string.Join(",", group.Select(r => r.Id))}");
            foreach (var group in rows.Where(r => r.Table == "Instances").GroupBy(r => (r.Tenant,
                         Type: Convert.ToString(r.Values["BusinessType"])!.Trim().ToUpperInvariant(), Key: Convert.ToString(r.Values["BusinessKey"])!.Trim().ToUpperInvariant()))
                         .Where(g => g.Select(r => (Convert.ToString(r.Values["BusinessType"]), Convert.ToString(r.Values["BusinessKey"]))).Distinct().Count() > 1))
                collisions.Add($"Instances: 大小写/空白归一化潜在碰撞：{string.Join(",", group.Select(r => r.Id))}");
            if (problems.Count > 0 || !apply) return new(counts, problems, 0) { PotentialKeyCollisions = collisions };
            var changed = 0;
            foreach (var row in rows.Where(r => string.IsNullOrEmpty(r.Existing)))
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"UPDATE {Quote(tablePrefix + row.Table)} SET {Quote("TenantId")} = @tenant WHERE {Quote("Id")} = @id AND ({Quote("TenantId")} IS NULL OR {Quote("TenantId")} = '')";
                var tenantParameter = command.CreateParameter(); tenantParameter.ParameterName = "@tenant"; tenantParameter.Value = row.Tenant; command.Parameters.Add(tenantParameter);
                var idParameter = command.CreateParameter(); idParameter.ParameterName = "@id";
                idParameter.Value = row.Id; command.Parameters.Add(idParameter);
                if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("回填期间数据已变化；事务已回滚，请暂停写入后重试");
                changed++;
            }
            await transaction!.CommitAsync(ct);
            return new(counts, [], changed) { PotentialKeyCollisions = collisions };
        }
        finally { if (opened) await connection.CloseAsync(); }
    }
}
