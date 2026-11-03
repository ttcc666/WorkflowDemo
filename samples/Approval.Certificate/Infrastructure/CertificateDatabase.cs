using Certificate.Api.Certificates;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Infrastructure;

public sealed record DatabaseReadiness(bool Ready, IReadOnlyList<string> Problems);
public sealed record DatabaseCopyResult(string Destination, int Certificates, int Requests);

/// <summary>新库由迁移创建；提供当前库就绪检查和只读源备份。</summary>
public sealed class CertificateDatabase(AppDbContext db)
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
            await db.Database.MigrateAsync(ct);
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
            problems.Add("申请缺少审批关联或规范化证号");
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
