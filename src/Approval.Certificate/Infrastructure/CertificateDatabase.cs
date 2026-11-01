using System.Data;
using System.Text.Json;
using Approval.Core.Domain;
using Certificate.Api.Certificates;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Certificate.Api.Infrastructure;

public sealed record DatabaseReadiness(bool Ready, IReadOnlyList<string> Problems);
public sealed record DatabaseCopyResult(string Destination, int Certificates, int Requests);

/// <summary>新库由迁移创建；旧库只在显式维护操作中升级副本，源库始终以只读连接访问。</summary>
public sealed class CertificateDatabase(AppDbContext db, TimeProvider clock)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var builder = ConnectionString();
        if (builder.Mode != SqliteOpenMode.Memory && builder.DataSource != ":memory:")
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(builder.DataSource))!);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;", ct);
            var tables = await TablesAsync(connection, ct);
            if (tables.Count > 0 && !await HasHistoryAsync(connection, ct))
                throw new InvalidOperationException("检测到旧 EnsureCreated 数据库。请先预检并升级到新文件；不会自动修改或删除原库。");
            var known = db.Database.GetMigrations().ToHashSet();
            if ((await db.Database.GetAppliedMigrationsAsync(ct)).Any(id => !known.Contains(id)))
                throw new InvalidOperationException("数据库包含当前程序不认识的迁移，拒绝自动修改。");
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0 && tables.Contains("CertificateRequests")
                && (await ScalarAsync(connection, "SELECT COUNT(*) FROM CertificateRequests", ct) > 0
                    || await ScalarAsync(connection, "SELECT COUNT(*) FROM Certificates", ct) > 0))
                throw new InvalidOperationException("有业务数据的数据库需要显式升级副本，不能在启动时自动迁移。");
            if (pending.Count > 0) await db.Database.MigrateAsync(ct);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", ct);
            var readiness = await CheckAsync(ct);
            if (!readiness.Ready) throw new InvalidOperationException("注册证数据库未就绪：" + string.Join("；", readiness.Problems));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async Task<DatabaseReadiness> CheckAsync(CancellationToken ct = default)
    {
        var problems = new List<string>();
        if (!await db.Database.CanConnectAsync(ct)) return new(false, ["数据库不可连接"]);
        var known = db.Database.GetMigrations().ToHashSet();
        if ((await db.Database.GetAppliedMigrationsAsync(ct)).Any(id => !known.Contains(id)))
            return new(false, ["数据库包含当前程序不认识的迁移"]);
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return new(false, ["数据库迁移未完成"]);
        if (await db.Certificates.AnyAsync(c => c.NormalizedCertNo == null
            || !db.CertificateNumberReservations.Any(r => r.CertificateId == c.Id && r.NormalizedCertNo == c.NormalizedCertNo), ct))
            problems.Add("正式证书缺少证号占用");
        if (await db.CertificateRequests.AnyAsync(r => r.ApprovalInstanceId == null || r.NormalizedCertNo == null, ct))
            problems.Add("历史申请尚未完成关联回填");
        if (await db.CertificateRequests.AnyAsync(r => r.Operation == CertOperation.Create && r.Status == RequestStatus.Reviewing
            && !db.CertificateNumberReservations.Any(n => n.ActiveRequestId == r.Id && n.NormalizedCertNo == r.NormalizedCertNo), ct))
            problems.Add("审核中申请缺少证号占用");
        if (await db.CertificateNumberReservations.AnyAsync(r => r.ActiveRequestId != null && r.CertificateId != null, ct))
            problems.Add("证号存在冲突归属");
        if (await db.Certificates.AnyAsync(c => c.PendingRequestId != null && !db.CertificateRequests.Any(r =>
            r.Id == c.PendingRequestId && r.CertificateId == c.Id && r.Status == RequestStatus.Reviewing && r.Operation != CertOperation.Create), ct))
            problems.Add("证书编辑锁与活动申请不一致");
        if (await db.CertificateRequests.AnyAsync(r => r.Status == RequestStatus.Reviewing && r.Operation != CertOperation.Create
            && !db.Certificates.Any(c => c.Id == r.CertificateId && c.PendingRequestId == r.Id), ct))
            problems.Add("活动申请缺少对应证书编辑锁");
        if (await db.CertificateNumberReservations.AnyAsync(n => n.ActiveRequestId != null && !db.CertificateRequests.Any(r =>
            r.Id == n.ActiveRequestId && r.NormalizedCertNo == n.NormalizedCertNo && r.Status == RequestStatus.Reviewing && r.Operation == CertOperation.Create), ct))
            problems.Add("证号占用指向非活动的新增申请");
        return new(problems.Count == 0, problems);
    }

    public async Task<DatabaseCopyResult> BackupAsync(string destination, CancellationToken ct = default)
    {
        await using var source = await OpenSourceAsync(ct);
        await IntegrityAsync(source, ct);
        return await CopyAsync(source, destination, ct);
    }

    public async Task<DatabaseCopyResult> UpgradeCopyAsync(string destination, CancellationToken ct = default)
    {
        await using var source = await OpenSourceAsync(ct);
        await IntegrityAsync(source, ct);
        var known = db.Database.GetMigrations().ToList();
        var applied = await AppliedAsync(source, ct);
        if (applied.Any(id => !known.Contains(id)) || !known.Take(applied.Count).SequenceEqual(applied))
            throw new InvalidOperationException("迁移历史不属于当前版本，拒绝升级。");
        var schemaVersion = applied.LastOrDefault() ?? known.First();
        await VerifySchemaAsync(source, schemaVersion, ct);
        var data = await ReadLegacyAsync(source, ct);
        await CopyAsync(source, destination, ct);

        await using var target = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.GetFullPath(destination)};Pooling=False;Foreign Keys=True").Options);
        if (applied.Count == 0)
        {
            var history = target.GetService<IHistoryRepository>();
            await target.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
            await target.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(known.First(), "10.0.14")), ct);
        }
        await target.Database.MigrateAsync(ct);
        await using var transaction = await target.Database.BeginTransactionAsync(ct);
        var certificates = await target.Certificates.ToListAsync(ct);
        var requests = await target.CertificateRequests.ToListAsync(ct);
        var reservations = await target.CertificateNumberReservations.ToDictionaryAsync(r => r.NormalizedCertNo, ct);
        foreach (var certificate in certificates)
        {
            var number = CertificateDataValidator.NormalizeNumber(certificate.CertNo);
            if (certificate.NormalizedCertNo is not null && certificate.NormalizedCertNo != number)
                throw new InvalidOperationException("证号规范化键不一致，拒绝自动修正。");
            certificate.NormalizedCertNo = number;
            certificate.RegisteredBy ??= requests.Where(r => r.CertificateId == certificate.Id && r.Operation == CertOperation.Create
                && r.Status == RequestStatus.Approved).OrderBy(r => r.CreatedAt).Select(r => r.ApplicantId).FirstOrDefault();
            if (!reservations.ContainsKey(number)) target.CertificateNumberReservations.Add(new CertificateNumberReservation
            {
                NormalizedCertNo = number,
                CertificateId = certificate.Id,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
        }
        foreach (var request in requests)
        {
            if (request.ApprovalInstanceId is { } existing && existing != data.InstanceIds[request.Id])
                throw new InvalidOperationException("已有的申请实例关联不一致，拒绝自动修正。");
            request.NormalizedCertNo = CertificateDataValidator.NormalizeNumber(request.Snapshot.CertNo);
            request.ApprovalInstanceId = data.InstanceIds[request.Id];
            // 旧数据没有提交前快照，保持 null，不能用现值伪造历史。
            if (request.Operation == CertOperation.Create && request.Status == RequestStatus.Reviewing
                && !reservations.ContainsKey(request.NormalizedCertNo))
                target.CertificateNumberReservations.Add(new CertificateNumberReservation
                {
                    NormalizedCertNo = request.NormalizedCertNo,
                    ActiveRequestId = request.Id,
                    CreatedAt = request.CreatedAt
                });
        }
        await target.SaveChangesAsync(ct);
        var ready = await new CertificateDatabase(target, clock).CheckAsync(ct);
        if (!ready.Ready) throw new InvalidOperationException("升级副本校验失败：" + string.Join("；", ready.Problems));
        await transaction.CommitAsync(ct);
        return new(Path.GetFullPath(destination), certificates.Count, requests.Count);
    }

    private SqliteConnectionStringBuilder ConnectionString() => new(db.Database.GetConnectionString());

    private async Task<SqliteConnection> OpenSourceAsync(CancellationToken ct)
    {
        var builder = ConnectionString();
        builder.DataSource = Path.GetFullPath(builder.DataSource);
        if (!File.Exists(builder.DataSource)) throw new InvalidOperationException("源数据库不存在。");
        builder.Mode = SqliteOpenMode.ReadOnly;
        builder.Pooling = false;
        var source = new SqliteConnection(builder.ToString());
        try { await source.OpenAsync(ct); return source; }
        catch { await source.DisposeAsync(); throw; }
    }

    private async Task<DatabaseCopyResult> CopyAsync(SqliteConnection source, string destination, CancellationToken ct)
    {
        var path = Path.GetFullPath(destination);
        if (string.Equals(path, Path.GetFullPath(source.DataSource), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标必须是新的数据库文件，不能覆盖源库。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // CreateNew 原子拒绝覆盖已有文件。
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        await using var target = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        await target.OpenAsync(ct);
        source.BackupDatabase(target);
        await IntegrityAsync(target, ct);
        return new(path, checked((int)await ScalarAsync(target, "SELECT COUNT(*) FROM Certificates", ct)),
            checked((int)await ScalarAsync(target, "SELECT COUNT(*) FROM CertificateRequests", ct)));
    }

    private async Task VerifySchemaAsync(SqliteConnection source, string version, CancellationToken ct)
    {
        await using var expected = new SqliteConnection("Data Source=:memory:");
        await expected.OpenAsync(ct);
        var script = db.GetService<IMigrator>().GenerateScript(null, version, MigrationsSqlGenerationOptions.NoTransactions);
        await ExecuteAsync(expected, script, ct);
        if (await SchemaAsync(source, ct) != await SchemaAsync(expected, ct))
            throw new InvalidOperationException("源库结构与已知基线不一致，请人工核查后再升级。");
    }

    private sealed record LegacyData(Dictionary<Guid, Guid> InstanceIds);

    private static async Task<LegacyData> ReadLegacyAsync(SqliteConnection source, CancellationToken ct)
    {
        var certs = new Dictionary<Guid, (string Number, Guid? Pending)>();
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT Id, CertNo, PendingRequestId FROM Certificates";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var number = reader.IsDBNull(1) ? "" : CertificateDataValidator.NormalizeNumber(reader.GetString(1));
                if (number.Length is 0 or > 100 || !occupied.Add(number)) throw new InvalidOperationException("旧库存在空白、超长或重复的规范化证号。");
                certs.Add(reader.GetGuid(0), (number, reader.IsDBNull(2) ? null : reader.GetGuid(2)));
            }
        }
        var definitions = new Dictionary<Guid, (string Key, int Version)>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT Id, [Key], Version FROM ApprovalDefinitions";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) definitions.Add(reader.GetGuid(0), (reader.GetString(1), reader.GetInt32(2)));
        }
        var instances = new Dictionary<Guid, List<(Guid Id, string Status)>>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT Id, BusinessKey, Status, DefinitionId, DefinitionKey, DefinitionVersion FROM ApprovalInstances WHERE BusinessType = 'Certificate'";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!Guid.TryParse(reader.GetString(1), out var key)) throw new InvalidOperationException("证书审批业务标识无效。");
                if (!definitions.TryGetValue(reader.GetGuid(3), out var definition) || definition.Key != reader.GetString(4)
                    || definition.Version != reader.GetInt32(5)) throw new InvalidOperationException("审批实例的流程版本关联不一致。");
                if (!instances.TryGetValue(key, out var list)) instances[key] = list = [];
                list.Add((reader.GetGuid(0), reader.GetString(2)));
            }
        }
        var bindings = new Dictionary<Guid, Guid>();
        var activeLocks = new Dictionary<Guid, Guid>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT Id, CertificateId, Operation, Snapshot, Status FROM CertificateRequests";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var certificateId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
                var operation = reader.GetString(2);
                var json = reader.GetString(3);
                using var document = JsonDocument.Parse(json);
                // 已知 EF JSON 存储使用 CLR 属性名。格式不匹配会在实体物化时产生空值，必须在复制前拒绝。
                if (document.RootElement.ValueKind != JsonValueKind.Object || typeof(CertificateData).GetProperties()
                    .Any(p => !document.RootElement.TryGetProperty(p.Name, out _)))
                    throw new InvalidOperationException("申请快照格式与已知存储模型不一致。");
                var snapshot = JsonSerializer.Deserialize<CertificateData>(json)
                    ?? throw new InvalidOperationException("申请快照为空。");
                var number = CertificateDataValidator.NormalizeNumber(snapshot.CertNo ?? "");
                var status = reader.GetString(4);
                if (number.Length is 0 or > 100 || !Enum.TryParse<CertOperation>(operation, out _)
                    || !Enum.TryParse<RequestStatus>(status, out _)) throw new InvalidOperationException("申请包含无效证号、操作或状态。");
                if (!instances.TryGetValue(id, out var matching) || matching.Count != 1)
                    throw new InvalidOperationException("申请缺少唯一的审批实例关联。");
                var expectedStatus = status == "Reviewing" ? "Running" : status;
                if (matching[0].Status != expectedStatus && !(status == "Rejected" && matching[0].Status == "Terminated"))
                    throw new InvalidOperationException("申请状态与审批结果不一致。");
                bindings.Add(id, matching[0].Id);
                if ((operation != "Create" || status == "Approved") && certificateId is null)
                    throw new InvalidOperationException("申请缺少正式证书关联。");
                if (operation == "Create" && status == "Reviewing" && certificateId is not null)
                    throw new InvalidOperationException("审核中的新增申请已错误关联正式证书。");
                if (certificateId is { } certId && (!certs.TryGetValue(certId, out var cert) || cert.Number != number))
                    throw new InvalidOperationException("申请存在无效证书引用。");
                if (status == "Reviewing" && operation == "Create" && !occupied.Add(number))
                    throw new InvalidOperationException("旧库存在同号的运行中新增申请或已登记证书。");
                if (status == "Reviewing" && operation != "Create")
                {
                    if (certificateId is not { } locked || certs[locked].Pending != id || !activeLocks.TryAdd(locked, id))
                        throw new InvalidOperationException("证书锁与活动申请不一致。");
                }
            }
        }
        if (certs.Any(c => c.Value.Pending is { } pending && (!activeLocks.TryGetValue(c.Key, out var owner) || owner != pending))
            || instances.Keys.Any(id => !bindings.ContainsKey(id)))
            throw new InvalidOperationException("旧库存在孤立的证书锁或审批实例。");
        return new(bindings);
    }

    private static async Task<string> SchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        var shape = new List<string>();
        foreach (var table in (await TablesAsync(connection, ct)).Order())
        {
            shape.Add(table);
            foreach (var sql in new[]
            {
                "SELECT name, type, [notnull], coalesce(dflt_value,''), pk FROM pragma_table_info($table) ORDER BY name",
                "SELECT [table], [from], [to], on_update, on_delete FROM pragma_foreign_key_list($table) ORDER BY [table], [from]",
                "SELECT il.[unique], ii.name, il.partial FROM pragma_index_list($table) il JOIN pragma_index_info(il.name) ii WHERE il.origin != 'pk' ORDER BY ii.name, il.[unique]"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$table", table);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) shape.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
                shape.Add(";");
            }
        }
        return string.Join("\n", shape);
    }

    private static async Task<List<string>> TablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        var names = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            if (!name.StartsWith("sqlite_", StringComparison.Ordinal) && !name.StartsWith("__EF", StringComparison.Ordinal)) names.Add(name);
        }
        return names;
    }

    private static async Task<bool> HasHistoryAsync(SqliteConnection connection, CancellationToken ct) =>
        await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'", ct) == 1;

    private static async Task<List<string>> AppliedAsync(SqliteConnection connection, CancellationToken ct)
    {
        var result = new List<string>();
        if (!await HasHistoryAsync(connection, ct)) return result;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task IntegrityAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if (!Equals(await command.ExecuteScalarAsync(ct), "ok")) throw new InvalidOperationException("SQLite 完整性检查失败。");
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("SQLite 外键完整性检查失败。");
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
