using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.ReferenceHost.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "longtext", nullable: false),
                    Category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DefinitionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    FormDataJson = table.Column<string>(type: "longtext", nullable: false),
                    Comment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDrafts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    LookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    HashVersion = table.Column<int>(type: "int", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOperations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceDefinitionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: true),
                    ChangeNote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    DefinitionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ApprovalDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_SourceDefinitio~",
                        column: x => x.SourceDefinitionId,
                        principalTable: "ApprovalDefinitions",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionManagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DraftJson = table.Column<string>(type: "longtext", nullable: true),
                    DraftSourceDefinitionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    PublishedDraftRevision = table.Column<long>(type: "bigint", nullable: true),
                    LastVersion = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActiveDefinitionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionManagement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_ActiveDefin~",
                        column: x => x.ActiveDefinitionId,
                        principalTable: "ApprovalDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_DraftSource~",
                        column: x => x.DraftSourceDefinitionId,
                        principalTable: "ApprovalDefinitions",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DefinitionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "int", nullable: false),
                    BusinessType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    InitiatorId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CurrentNodeId = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    StateJson = table.Column<string>(type: "longtext", nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    FormDataJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false),
                    LogSequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalInstances_ApprovalDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ApprovalDefinitions",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalBusinessSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    LookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBusinessSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBusinessSlots_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalCarbonCopies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NodeId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    AssigneeKind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AssigneeValue = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalCarbonCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalCarbonCopies_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    FileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    UploadedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DraftId = table.Column<Guid>(type: "char(36)", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalFiles_ApprovalDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ApprovalDrafts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApprovalFiles_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    NodeName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    OperatorId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "longtext", nullable: true),
                    Details = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalLogs_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    Event = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NodeId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalMessages_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Target = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: true),
                    NodeId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastError = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalOutbox_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NodeId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    NodeName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AssigneeKind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    AssigneeValue = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    HandledBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextRemindAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RemindCount = table.Column<int>(type: "int", nullable: false),
                    LastRemindedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalCarbonCopyReads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CarbonCopyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalCarbonCopyReads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalCarbonCopyReads_ApprovalCarbonCopies_CarbonCopyId",
                        column: x => x.CarbonCopyId,
                        principalTable: "ApprovalCarbonCopies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBusinessSlots_InstanceId",
                table: "ApprovalBusinessSlots",
                column: "InstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalBusinessSlots_LookupHash",
                table: "ApprovalBusinessSlots",
                column: "LookupHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies",
                columns: new[] { "AssigneeKind", "AssigneeValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_InstanceId",
                table: "ApprovalCarbonCopies",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopyReads_CarbonCopyId_UserId",
                table: "ApprovalCarbonCopyReads",
                columns: new[] { "CarbonCopyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_DefinitionId",
                table: "ApprovalDefinitionAudits",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_Key_Revision",
                table: "ApprovalDefinitionAudits",
                columns: new[] { "Key", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionAudits_SourceDefinitionId",
                table: "ApprovalDefinitionAudits",
                column: "SourceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_ActiveDefinitionId",
                table: "ApprovalDefinitionManagement",
                column: "ActiveDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_Category",
                table: "ApprovalDefinitionManagement",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_DraftSourceDefinitionId",
                table: "ApprovalDefinitionManagement",
                column: "DraftSourceDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_Key",
                table: "ApprovalDefinitionManagement",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_Category",
                table: "ApprovalDefinitions",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_Key_Version",
                table: "ApprovalDefinitions",
                columns: new[] { "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDrafts_UserId_UpdatedAt",
                table: "ApprovalDrafts",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_DraftId",
                table: "ApprovalFiles",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_InstanceId",
                table: "ApprovalFiles",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFiles_UploadedAt",
                table: "ApprovalFiles",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_BusinessType_BusinessKey",
                table: "ApprovalInstances",
                columns: new[] { "BusinessType", "BusinessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_DefinitionId",
                table: "ApprovalInstances",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_InitiatorId_CreatedAt",
                table: "ApprovalInstances",
                columns: new[] { "InitiatorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_InitiatorId_Status",
                table: "ApprovalInstances",
                columns: new[] { "InitiatorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_Status_CreatedAt",
                table: "ApprovalInstances",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_InstanceId_Sequence",
                table: "ApprovalLogs",
                columns: new[] { "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalLogs_OperatorId_Action",
                table: "ApprovalLogs",
                columns: new[] { "OperatorId", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_InstanceId",
                table: "ApprovalMessages",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOperations_LookupHash",
                table: "ApprovalOperations",
                column: "LookupHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_InstanceId",
                table: "ApprovalOutbox",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_Status_NextAttemptAt",
                table: "ApprovalOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_AssigneeKind_AssigneeValue_Status",
                table: "ApprovalTasks",
                columns: new[] { "AssigneeKind", "AssigneeValue", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_HandledBy",
                table: "ApprovalTasks",
                column: "HandledBy");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_InstanceId",
                table: "ApprovalTasks",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_Status_NextRemindAt",
                table: "ApprovalTasks",
                columns: new[] { "Status", "NextRemindAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalBusinessSlots");

            migrationBuilder.DropTable(
                name: "ApprovalCarbonCopyReads");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitionAudits");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitionManagement");

            migrationBuilder.DropTable(
                name: "ApprovalFiles");

            migrationBuilder.DropTable(
                name: "ApprovalLogs");

            migrationBuilder.DropTable(
                name: "ApprovalMessages");

            migrationBuilder.DropTable(
                name: "ApprovalOperations");

            migrationBuilder.DropTable(
                name: "ApprovalOutbox");

            migrationBuilder.DropTable(
                name: "ApprovalTasks");

            migrationBuilder.DropTable(
                name: "ApprovalCarbonCopies");

            migrationBuilder.DropTable(
                name: "ApprovalDrafts");

            migrationBuilder.DropTable(
                name: "ApprovalInstances");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitions");
        }
    }
}
