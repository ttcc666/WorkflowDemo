using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.MultiTenant.Api.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class InitialReferenceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BusinessType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BusinessKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayloadExpiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalArchives", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalArchives_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalDefinitions_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefinitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FormDataJson = table.Column<string>(type: "text", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDrafts", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalDrafts_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalFileDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalFileDeletions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalMaintenanceAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Batch = table.Column<int>(type: "integer", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Target = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMaintenanceAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalMaintenanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SystemRequested = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Cutoff = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBatches = table.Column<int>(type: "integer", nullable: false),
                    Processed = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMaintenanceJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HashVersion = table.Column<int>(type: "integer", nullable: false),
                    ResultVersion = table.Column<int>(type: "integer", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsTombstone = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRetentionHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRetentionHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: true),
                    ChangeNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_TenantId_Defin~",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_TenantId_Sourc~",
                        columns: x => new { x.TenantId, x.SourceDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionManagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DraftJson = table.Column<string>(type: "text", nullable: true),
                    DraftSourceDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    PublishedDraftRevision = table.Column<long>(type: "bigint", nullable: true),
                    LastVersion = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionManagement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_TenantId_A~",
                        columns: x => new { x.TenantId, x.ActiveDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_TenantId_D~",
                        columns: x => new { x.TenantId, x.DraftSourceDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InitiatorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentNodeId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StateJson = table.Column<string>(type: "text", nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    FormDataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false),
                    LogSequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalInstances", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalInstances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApprovalInstances_ApprovalDefinitions_TenantId_DefinitionId",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalBusinessSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LookupHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBusinessSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBusinessSlots_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalCarbonCopies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssigneeKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssigneeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalCarbonCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalCarbonCopies_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchiveId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionPending = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalFiles_ApprovalArchives_TenantId_ArchiveId",
                        columns: x => new { x.TenantId, x.ArchiveId },
                        principalTable: "ApprovalArchives",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalFiles_ApprovalDrafts_TenantId_DraftId",
                        columns: x => new { x.TenantId, x.DraftId },
                        principalTable: "ApprovalDrafts",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalFiles_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NodeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalLogs_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Event = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalMessages_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalOutbox_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NodeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssigneeKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    AssigneeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    HandledBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRemindAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemindCount = table.Column<int>(type: "integer", nullable: false),
                    LastRemindedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ApprovalBusinessSlots_TenantId_InstanceId",
                table: "ApprovalBusinessSlots",
                columns: new[] { "TenantId", "InstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBusinessSlots_TenantId_LookupHash",
                table: "ApprovalBusinessSlots",
                columns: new[] { "TenantId", "LookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies",
                columns: new[] { "TenantId", "AssigneeKind", "AssigneeValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_InstanceId",
                table: "ApprovalCarbonCopies",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_TenantId_DefinitionId",
                table: "ApprovalDefinitionAudits",
                columns: new[] { "TenantId", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_TenantId_Key_Revision",
                table: "ApprovalDefinitionAudits",
                columns: new[] { "TenantId", "Key", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_TenantId_SourceDefinitionId",
                table: "ApprovalDefinitionAudits",
                columns: new[] { "TenantId", "SourceDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_ActiveDefinitionId",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "ActiveDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_DraftSourceDefinition~",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "DraftSourceDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_Key",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_TenantId_Key_Version",
                table: "ApprovalDefinitions",
                columns: new[] { "TenantId", "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDrafts_TenantId_UserId_UpdatedAt",
                table: "ApprovalDrafts",
                columns: new[] { "TenantId", "UserId", "UpdatedAt" });

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
                name: "IX_ApprovalFiles_TenantId_ArchiveId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "ArchiveId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_TenantId_DraftId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_TenantId_InstanceId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_TenantId_UploadedAt",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_TenantId_BusinessType_BusinessKey",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "BusinessType", "BusinessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_TenantId_DefinitionId",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_TenantId_InitiatorId_CreatedAt",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "InitiatorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_TenantId_InitiatorId_Status",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "InitiatorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_TenantId_Status_CreatedAt",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_TenantId_InstanceId_Sequence",
                table: "ApprovalLogs",
                columns: new[] { "TenantId", "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_TenantId_OperatorId_Action",
                table: "ApprovalLogs",
                columns: new[] { "TenantId", "OperatorId", "Action" });

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
                name: "IX_ApprovalMessages_TenantId_InstanceId",
                table: "ApprovalMessages",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_TenantId_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages",
                columns: new[] { "TenantId", "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOperations_TenantId_LookupHash",
                table: "ApprovalOperations",
                columns: new[] { "TenantId", "LookupHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_TenantId_InstanceId",
                table: "ApprovalOutbox",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_TenantId_Status_NextAttemptAt",
                table: "ApprovalOutbox",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRetentionHolds_TenantId_Target_ResourceId",
                table: "ApprovalRetentionHolds",
                columns: new[] { "TenantId", "Target", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_AssigneeKind_AssigneeValue_Status",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "AssigneeKind", "AssigneeValue", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_HandledBy",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "HandledBy" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_InstanceId",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_Status_NextRemindAt",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "Status", "NextRemindAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalBusinessSlots");

            migrationBuilder.DropTable(
                name: "ApprovalCarbonCopies");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitionAudits");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitionManagement");

            migrationBuilder.DropTable(
                name: "ApprovalFileDeletions");

            migrationBuilder.DropTable(
                name: "ApprovalFiles");

            migrationBuilder.DropTable(
                name: "ApprovalLogs");

            migrationBuilder.DropTable(
                name: "ApprovalMaintenanceAudits");

            migrationBuilder.DropTable(
                name: "ApprovalMaintenanceJobs");

            migrationBuilder.DropTable(
                name: "ApprovalMessages");

            migrationBuilder.DropTable(
                name: "ApprovalOperations");

            migrationBuilder.DropTable(
                name: "ApprovalOutbox");

            migrationBuilder.DropTable(
                name: "ApprovalRetentionHolds");

            migrationBuilder.DropTable(
                name: "ApprovalTasks");

            migrationBuilder.DropTable(
                name: "ApprovalArchives");

            migrationBuilder.DropTable(
                name: "ApprovalDrafts");

            migrationBuilder.DropTable(
                name: "ApprovalInstances");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitions");
        }
    }
}
