using System.Security.Cryptography;
using System.Text.Json;
using Certificate.Api.Certificates;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Certificate.Api.Tests;

public sealed class CertificateMigrationTests
{
    [Fact]
    public async Task Legacy_upgrade_builds_a_ready_copy_without_changing_source_or_inventing_history()
    {
        using var directory = new TestDirectory();
        var original = Path.Combine(directory.DirectoryPath, "legacy.db");
        var update = await CreateLegacyAsync(original);
        var originalBytes = await File.ReadAllBytesAsync(original);
        var targetDirectory = Path.Combine(directory.DirectoryPath, "upgraded");
        Directory.CreateDirectory(targetDirectory);
        await using (var db = Context(original))
        {
            var result = await new CertificateDatabase(db, TimeProvider.System).UpgradeCopyAsync(Path.Combine(targetDirectory, "pilot.db"));
            Assert.Equal(1, result.Certificates);
            Assert.Equal(2, result.Requests);
        }
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original));
        await using var host = await CertificateHost.StartAsync(targetDirectory);
        var detail = await host.GetAsync($"/api/certificate-requests/{update}");
        Assert.False(detail.GetProperty("request").GetProperty("hasHistoricalBaseline").GetBoolean());
        Assert.Empty(detail.GetProperty("changes").EnumerateArray());
        Assert.Equal("版本二", detail.GetProperty("request").GetProperty("snapshot").GetProperty("name").GetString());
        var reservation = await host.DbAsync(db => db.CertificateNumberReservations.AsNoTracking().SingleAsync());
        Assert.Equal("REG-LEGACY", reservation.NormalizedCertNo);
        Assert.Null(reservation.ActiveRequestId);
        Assert.Equal(await host.DbAsync(db => db.Certificates.Select(c => c.Id).SingleAsync()), reservation.CertificateId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("schema")]
    [InlineData("orphan")]
    [InlineData("snapshot")]
    [InlineData("definition")]
    [InlineData("missing-certificate")]
    public async Task Legacy_preflight_rejects_unsafe_data_before_creating_destination(string scenario)
    {
        using var directory = new TestDirectory();
        var original = Path.Combine(directory.DirectoryPath, "legacy.db");
        await CreateLegacyAsync(original);
        await using (var db = Context(original))
        {
            switch (scenario)
            {
                case "duplicate":
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Certificates (Id, CertNo, Name, Holder, CertType, IssueDate, ExpireDate, Status, ConcurrencyStamp) VALUES ({Guid.NewGuid()}, {' ' + "REG-LEGACY" + ' '}, {"dup"}, {"owner"}, {"Normal"}, {"2026-01-01"}, {"2029-01-01"}, {"Active"}, {0})");
                    break;
                case "schema": await db.Database.ExecuteSqlRawAsync("ALTER TABLE Certificates ADD COLUMN Unrecognized TEXT"); break;
                case "orphan": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CertificateRequests SET CertificateId = {Guid.NewGuid()}"); break;
                case "snapshot": await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CertificateRequests SET Snapshot = {JsonSerializer.Serialize(CertificateHost.ValidData(), JsonSerializerOptions.Web)}"); break;
                case "definition": await db.Database.ExecuteSqlRawAsync("UPDATE ApprovalInstances SET DefinitionVersion = 999"); break;
                case "missing-certificate": await db.Database.ExecuteSqlRawAsync("UPDATE CertificateRequests SET CertificateId = NULL"); break;
            }
        }
        var before = SHA256.HashData(await File.ReadAllBytesAsync(original));
        var destination = Path.Combine(directory.DirectoryPath, "must-not-exist.db");
        await using (var db = Context(original))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CertificateDatabase(db, TimeProvider.System).UpgradeCopyAsync(destination));
        Assert.False(File.Exists(destination));
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(original)));
    }

    [Fact]
    public async Task Startup_refuses_legacy_database_without_modifying_it()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.DirectoryPath, "pilot.db");
        await CreateLegacyAsync(path);
        var before = await File.ReadAllBytesAsync(path);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CertificateHost.StartAsync(directory.DirectoryPath));
        Assert.Contains("EnsureCreated", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private static AppDbContext Context(string path) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={path};Pooling=False").Options);

    [Fact]
    public async Task Startup_rejects_unknown_migration_history_without_changing_database()
    {
        using var directory = new TestDirectory();
        await using (var host = await CertificateHost.StartAsync(directory.DirectoryPath))
            await host.DbAsync(db => db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('20990101000000_Unknown', '10.0.14')"));
        var path = Path.Combine(directory.DirectoryPath, "pilot.db");
        var before = await File.ReadAllBytesAsync(path);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CertificateHost.StartAsync(directory.DirectoryPath));
        Assert.Contains("不认识的迁移", error.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private static async Task<Guid> CreateLegacyAsync(string path)
    {
        await using var db = Context(path);
        var baseline = db.Database.GetMigrations().First();
        await db.GetService<IMigrator>().MigrateAsync(baseline);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory");
        var certificate = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Certificates (Id, CertNo, Name, Holder, CertType, IssueDate, ExpireDate, Status, ConcurrencyStamp) VALUES ({certificate}, {"reg-legacy"}, {"版本二"}, {"企业"}, {"Normal"}, {"2026-01-01"}, {"2029-01-01"}, {"Active"}, {1})");
        Guid last = default;
        foreach (var operation in new[] { "Create", "Update" })
        {
            var definition = Guid.NewGuid();
            var instance = Guid.NewGuid();
            var request = Guid.NewGuid();
            var key = "certificate." + operation.ToLowerInvariant();
            var flow = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Flows", key + ".json"));
            var json = JsonSerializer.Serialize(CertificateHost.ValidData("reg-legacy") with { Name = operation == "Create" ? "版本一" : "版本二" });
            const string time = "2026-11-01 01:00:00";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ApprovalDefinitions (Id, [Key], Version, Name, DefinitionJson, IsPublished, CreatedAt) VALUES ({definition}, {key}, {1}, {key}, {flow}, {true}, {time})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ApprovalInstances (Id, DefinitionId, DefinitionKey, DefinitionVersion, BusinessType, BusinessKey, InitiatorId, Status, Round, FormDataJson, CreatedAt, CompletedAt, ConcurrencyStamp, LogSequence) VALUES ({instance}, {definition}, {key}, {1}, {"Certificate"}, {request.ToString()}, {"alice"}, {"Approved"}, {0}, {json}, {time}, {time}, {1}, {0})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CertificateRequests (Id, CertificateId, Operation, Snapshot, Status, ApplicantId, CreatedAt, CompletedAt) VALUES ({request}, {certificate}, {operation}, {json}, {"Approved"}, {"alice"}, {time}, {time})");
            last = request;
        }
        return last;
    }
}
