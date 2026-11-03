using Approval.Forms.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task InitialMigration_CreatesCurrentSchemaAndRoundTripsAttachmentMetadata()
    {
        await ClearDataAsync();
        await using var db = fixture.Create();
        await db.Database.OpenConnectionAsync();

        async Task<string[]> NamesAsync(string query, int ordinal = 0)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(ordinal));
            return names.ToArray();
        }

        var tables = await NamesAsync(fixture.Provider switch
        {
            "Sqlite" => "SELECT name FROM sqlite_master WHERE type = 'table'",
            "MySql" => "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()",
            "SqlServer" => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo'",
            "PostgreSql" => "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            _ => throw new InvalidOperationException("Unsupported provider")
        });
        Assert.Contains("ApprovalFiles", tables);
        foreach (var removed in new[] { "ApprovalArchives", "ApprovalMaintenanceJobs", "ApprovalMaintenanceAudits", "ApprovalRetentionHolds", "ApprovalFileDeletions" })
            Assert.DoesNotContain(removed, tables);

        var columns = await NamesAsync(fixture.Provider switch
        {
            "Sqlite" => "SELECT name FROM pragma_table_info('ApprovalFiles')",
            "MySql" => "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ApprovalFiles'",
            "SqlServer" => "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'ApprovalFiles'",
            "PostgreSql" => "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ApprovalFiles'",
            _ => throw new InvalidOperationException("Unsupported provider")
        });
        Assert.DoesNotContain("ArchiveId", columns);
        Assert.DoesNotContain("DeletionPending", columns);
        foreach (var current in new[] { "Id", "InstanceId", "DraftId", "StorageKey", "UploadedBy", "ConcurrencyStamp" })
            Assert.Contains(current, columns);

        var fileId = Guid.NewGuid();
        await using (var storage = fixture.CreateStorage())
        {
            storage.Add(new ApprovalFile { Id = fileId, FileName = "发票😀.pdf", ContentType = "application/pdf",
                StorageKey = "2026/11/" + fileId.ToString("N"), UploadedBy = "alice", UploadedAt = Timestamp,
                Sha256 = new string('b', 64), Size = 12 });
            await storage.SaveChangesAsync();
        }
        var file = await db.Set<ApprovalFile>().AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.Equal("发票😀.pdf", file.FileName);
        Assert.Equal("2026/11/" + fileId.ToString("N"), file.StorageKey);
        Assert.Equal("alice", file.UploadedBy);
        Assert.Equal(Timestamp, file.UploadedAt);
        Assert.Equal(12, file.Size);
        Assert.Equal(new string('b', 64), file.Sha256);
        Assert.Null(file.InstanceId);
        Assert.Null(file.DraftId);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
