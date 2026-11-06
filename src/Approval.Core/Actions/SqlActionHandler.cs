using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine.Extensions;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Actions;

internal sealed record SqlActionRegistration(ApprovalSqlActionQuery Query, Func<IServiceProvider, DbConnection> OpenConnection)
{
    internal SemaphoreSlim Slots { get; } = new(4);
}

internal sealed class SqlActionHandler(IServiceProvider services, ApprovalDbContextAccessor accessor,
    IEnumerable<SqlActionRegistration> registrations) : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    private sealed record Config(string DataSource, string QueryId, Dictionary<string, JsonElement>? Parameters);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private const int MaxParameterLength = 4096;
    private readonly Dictionary<(string Source, string Query), SqlActionRegistration> _queries =
        registrations.ToDictionary(item => (item.Query.DataSource, item.Query.QueryId));

    public ApprovalActionDescriptor Descriptor => new("Sql", "SQL 查询", "执行宿主登记的只读查询",
        [ActionExecutionMode.Sync], ConfigSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object", required = new[] { "dataSource", "queryId" },
            properties = new
            {
                dataSource = new { type = "string", @enum = _queries.Keys.Select(k => k.Source).Distinct().Order().ToArray() },
                queryId = new { type = "string", @enum = _queries.Keys.Select(k => k.Query).Distinct().Order().ToArray() },
                parameters = new { type = "object" }
            },
            queries = _queries.Values.OrderBy(r => r.Query.DataSource).ThenBy(r => r.Query.QueryId)
                .Select(r => new { r.Query.DataSource, r.Query.QueryId, parameters = r.Query.Parameters.ToDictionary(
                    p => p.Key, p => p.Value.ToString()), r.Query.MaxRows, r.Query.TimeoutSeconds })
        }, Json));

    public bool ProvidesOutput => false;

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config)
    {
        try
        {
            var value = Read(config);
            if (version != 1 || !_queries.TryGetValue((value.DataSource, value.QueryId), out var registration))
                return ["SQL 数据源或查询未注册"];
            var parameters = value.Parameters ?? [];
            if (!registration.Query.Parameters.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(parameters.Keys))
                return ["SQL 参数必须与已注册查询一致"];
            foreach (var (name, parameter) in parameters)
            {
                if (parameter.ValueKind == JsonValueKind.String && parameter.GetString()!.Contains("{{", StringComparison.Ordinal))
                {
                    if (!ActionVariables.IsTemplate(parameter.GetString()!)) return ["SQL 参数引用路径无效"];
                }
                else _ = ParameterValue(parameter, registration.Query.Parameters[name]);
            }
            return [];
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        { return ["SQL 查询参数配置无效"]; }
    }

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        var value = Read(config);
        var registration = _queries[(value.DataSource, value.QueryId)];
        var query = registration.Query;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(query.TimeoutSeconds));
        var token = timeout.Token;
        await registration.Slots.WaitAsync(token);
        try
        {
            var connection = registration.OpenConnection(services);
            if (ReferenceEquals(connection, accessor.Context.Database.GetDbConnection()))
                throw new InvalidOperationException("SQL 动作需要独立只读连接");
            await using var ownedConnection = connection;
            if (connection.State != ConnectionState.Closed) throw new InvalidOperationException("SQL 连接工厂必须返回新的关闭连接");
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = query.CommandText;
            command.CommandTimeout = query.TimeoutSeconds;
            var root = ActionVariables.Snapshot(context);
            foreach (var (name, type) in query.Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.DbType = type;
                parameter.Value = ParameterValue(ActionVariables.Bind(value.Parameters![name], root), type);
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, token);
            var rows = await ReadRowsAsync(reader, query.MaxRows, token);
            token.ThrowIfCancellationRequested();
            return new(true, JsonSerializer.SerializeToElement(new { rows, count = rows.Count }));
        }
        finally { registration.Slots.Release(); }
    }

    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = Read(config);
        var query = _queries[(value.DataSource, value.QueryId)].Query;
        return Task.FromResult(new ApprovalActionResult(true, JsonSerializer.SerializeToElement(new
        {
            query.DataSource, query.QueryId, query.MaxRows, parameters = query.Parameters.Keys.ToArray()
        }, Json)));
    }

    private static async Task<List<JsonElement>> ReadRowsAsync(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        if (reader.FieldCount is < 1 or > 64) throw new InvalidOperationException("SQL 列数超限");
        // SQLite's GetTextReader uses native origin metadata and can crash on computed columns.
        var useSqliteScalar = reader.GetType().FullName == "Microsoft.Data.Sqlite.SqliteDataReader";
        var rows = new List<JsonElement>();
        var bytes = 64;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows) throw new InvalidOperationException("SQL 返回行数超限");
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                object? cell = null;
                if (!await reader.IsDBNullAsync(i, ct))
                {
                    var type = reader.GetFieldType(i);
                    if (type == typeof(string))
                        cell = await ReadTextAsync(reader, i, useSqliteScalar, ct);
                    else if (type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) ||
                             type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan) ||
                             type == typeof(DateOnly) || type == typeof(TimeOnly))
                        cell = reader.GetValue(i);
                    else throw new InvalidOperationException("SQL 输出列必须是标量");
                }
                row.Add(reader.GetName(i), cell);
            }
            var output = JsonSerializer.SerializeToElement(row);
            bytes += output.GetRawText().Length + 1;
            if (bytes > ApprovalActionValidation.MaxOutputBytes) throw new InvalidOperationException("SQL 结果大小超限");
            rows.Add(output);
        }
        return rows;
    }

    private static async Task<string> ReadTextAsync(DbDataReader reader, int index, bool useSqliteScalar, CancellationToken ct)
    {
        if (useSqliteScalar)
        {
            var value = reader.GetString(index);
            if (value.Length > MaxParameterLength) throw new InvalidOperationException("SQL 字段长度超限");
            return value;
        }
        using var text = reader.GetTextReader(index);
        var buffer = new char[MaxParameterLength + 1];
        var count = await text.ReadBlockAsync(buffer.AsMemory(), ct);
        if (count > MaxParameterLength) throw new InvalidOperationException("SQL 字段长度超限");
        return new string(buffer, 0, count);
    }

    private static Config Read(JsonElement json) => json.Deserialize<Config>(Json) ?? throw new JsonException();

    private static object ParameterValue(JsonElement value, DbType type)
    {
        if (value.ValueKind == JsonValueKind.Null) return DBNull.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Length > MaxParameterLength) throw new FormatException();
            switch (type)
            {
                case DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength: return text;
                case DbType.Guid when Guid.TryParse(text, out var id): return id;
                case DbType.Date or DbType.DateTime or DbType.DateTime2: return value.GetDateTime();
                case DbType.DateTimeOffset: return value.GetDateTimeOffset();
            }
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            switch (type)
            {
                case DbType.Byte: return value.GetByte();
                case DbType.Int16: return value.GetInt16();
                case DbType.Int32: return value.GetInt32();
                case DbType.Int64: return value.GetInt64();
                case DbType.Single: return value.GetSingle();
                case DbType.Double: return value.GetDouble();
                case DbType.Decimal or DbType.Currency: return value.GetDecimal();
            }
        }
        if (type == DbType.Boolean && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        throw new FormatException("SQL 参数类型不匹配");
    }
}
