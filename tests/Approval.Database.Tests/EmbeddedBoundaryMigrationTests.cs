using Approval.Core.Domain;
using Approval.Forms.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task EmbeddedBoundaryMigration_PreservesLegacyArchiveAndAttachmentData()
    {
        await ClearDataAsync();
        await using var db = fixture.Create();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(db.Database.GetMigrations().First());
        await db.Database.OpenConnectionAsync();
        var sql = db.GetService<ISqlGenerationHelper>();
        string Q(string name) => sql.DelimitIdentifier(name);
        var archiveId = Guid.NewGuid();
        const string snapshot = "{\"history\":\"原始审批归档😀\"}";
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            var names = new[] { "Id", "Target", "ResourceId", "SourceVersion", "ArchivedAt", "SnapshotJson", "SnapshotHash", "ConcurrencyStamp" };
            command.CommandText = $"INSERT INTO {Q("ApprovalArchives")} ({string.Join(",", names.Select(Q))}) VALUES (@id,@target,@resource,@version,@created,@snapshot,@hash,@stamp)";
            foreach (var (name, value) in new (string, object)[] { ("id", archiveId), ("target", "Instances"), ("resource", Guid.NewGuid()), ("version", 1L), ("created", Timestamp), ("snapshot", snapshot), ("hash", new string('a', 64)), ("stamp", 1L) })
            { var parameter = command.CreateParameter(); parameter.ParameterName = "@" + name; parameter.Value = value; command.Parameters.Add(parameter); }
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var fileId = Guid.NewGuid();
        db.Add(new ApprovalFile { Id = fileId, ArchiveId = archiveId, FileName = "history.pdf", ContentType = "application/pdf", StorageKey = "host/retained.pdf", UploadedBy = "legacy-user", UploadedAt = Timestamp, Sha256 = new string('b', 64), Size = 12 });
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.GetTableName() == "ApprovalArchives");
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"SELECT {Q("SnapshotJson")} FROM {Q("ApprovalArchives")} WHERE {Q("Id")} = @id";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = archiveId; command.Parameters.Add(parameter);
            Assert.Equal(snapshot, await command.ExecuteScalarAsync());
        }
        var file = await db.Set<ApprovalFile>().AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.Equal(archiveId, file.ArchiveId); Assert.Equal("host/retained.pdf", file.StorageKey); Assert.Equal(12, file.Size); Assert.False(file.DeletionPending);
    }
}
