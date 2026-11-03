using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Database.Tests.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class LifecycleMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTombstone",
                table: "ApprovalOperations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchiveId",
                table: "ApprovalFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyStamp",
                table: "ApprovalFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "DeletionPending",
                table: "ApprovalFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ApprovalArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefinitionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BusinessType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BusinessKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PayloadExpiredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConcurrencyStamp = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalArchives", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalArchives_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalFileDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalFileDeletions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalMaintenanceAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Batch = table.Column<int>(type: "INTEGER", nullable: true),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMaintenanceAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalMaintenanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SystemRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    PolicyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Cutoff = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedBatches = table.Column<int>(type: "INTEGER", nullable: false),
                    Processed = table.Column<int>(type: "INTEGER", nullable: false),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMaintenanceJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRetentionHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRetentionHolds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_TenantId_ArchiveId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "ArchiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalArchives_TenantId_ArchivedAt",
                table: "ApprovalArchives",
                columns: new[] { "TenantId", "ArchivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalArchives_TenantId_Target_ResourceId_SourceVersion",
                table: "ApprovalArchives",
                columns: new[] { "TenantId", "Target", "ResourceId", "SourceVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFileDeletions_TenantId_FileId",
                table: "ApprovalFileDeletions",
                columns: new[] { "TenantId", "FileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFileDeletions_TenantId_Status_NextAttemptAt",
                table: "ApprovalFileDeletions",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMaintenanceAudits_TenantId_JobId_Batch",
                table: "ApprovalMaintenanceAudits",
                columns: new[] { "TenantId", "JobId", "Batch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMaintenanceJobs_TenantId_Status_CreatedAt",
                table: "ApprovalMaintenanceJobs",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRetentionHolds_TenantId_Target_ResourceId",
                table: "ApprovalRetentionHolds",
                columns: new[] { "TenantId", "Target", "ResourceId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalFiles_ApprovalArchives_TenantId_ArchiveId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "ArchiveId" },
                principalTable: "ApprovalArchives",
                principalColumns: new[] { "TenantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalFiles_ApprovalArchives_TenantId_ArchiveId",
                table: "ApprovalFiles");

            migrationBuilder.DropTable(
                name: "ApprovalArchives");

            migrationBuilder.DropTable(
                name: "ApprovalFileDeletions");

            migrationBuilder.DropTable(
                name: "ApprovalMaintenanceAudits");

            migrationBuilder.DropTable(
                name: "ApprovalMaintenanceJobs");

            migrationBuilder.DropTable(
                name: "ApprovalRetentionHolds");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_TenantId_ArchiveId",
                table: "ApprovalFiles");

            migrationBuilder.DropColumn(
                name: "IsTombstone",
                table: "ApprovalOperations");

            migrationBuilder.DropColumn(
                name: "ArchiveId",
                table: "ApprovalFiles");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "ApprovalFiles");

            migrationBuilder.DropColumn(
                name: "DeletionPending",
                table: "ApprovalFiles");
        }
    }
}
