using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.MultiTenantHost.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenantApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitions", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalDefinitions_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LookupHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    HashVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangeNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_TenantId_DefinitionId",
                        columns: x => new { x.TenantId, x.DefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionAudits_ApprovalDefinitions_TenantId_SourceDefinitionId",
                        columns: x => new { x.TenantId, x.SourceDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionManagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DraftJson = table.Column<string>(type: "TEXT", nullable: true),
                    DraftSourceDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    DraftRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedDraftRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    LastVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActiveDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDefinitionManagement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_TenantId_ActiveDefinitionId",
                        columns: x => new { x.TenantId, x.ActiveDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ApprovalDefinitionManagement_ApprovalDefinitions_TenantId_DraftSourceDefinitionId",
                        columns: x => new { x.TenantId, x.DraftSourceDefinitionId },
                        principalTable: "ApprovalDefinitions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefinitionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    BusinessType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InitiatorId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CurrentNodeId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StateJson = table.Column<string>(type: "TEXT", nullable: true),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    FormDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "INTEGER", nullable: false),
                    LogSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LookupHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AssigneeKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AssigneeValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalCarbonCopies", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalCarbonCopies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ApprovalCarbonCopies_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OperatorId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Event = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AssigneeKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ParentTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    HandledBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRemindAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemindCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRemindedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "HostDeliveryReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostDeliveryReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostDeliveryReceipts_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "HostExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ApplicantId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostExpenses_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ApprovalCarbonCopyReads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CarbonCopyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalCarbonCopyReads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalCarbonCopyReads_ApprovalCarbonCopies_TenantId_CarbonCopyId",
                        columns: x => new { x.TenantId, x.CarbonCopyId },
                        principalTable: "ApprovalCarbonCopies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ApprovalCarbonCopyReads_TenantId_CarbonCopyId_UserId",
                table: "ApprovalCarbonCopyReads",
                columns: new[] { "TenantId", "CarbonCopyId", "UserId" },
                unique: true);

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
                name: "IX_ApprovalDefinitionManagement_TenantId_Category",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_DraftSourceDefinitionId",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "DraftSourceDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_Key",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_TenantId_Category",
                table: "ApprovalDefinitions",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_TenantId_Key_Version",
                table: "ApprovalDefinitions",
                columns: new[] { "TenantId", "Key", "Version" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_HostDeliveryReceipts_TenantId_InstanceId",
                table: "HostDeliveryReceipts",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostExpenses_TenantId_BusinessKey",
                table: "HostExpenses",
                columns: new[] { "TenantId", "BusinessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostExpenses_TenantId_InstanceId",
                table: "HostExpenses",
                columns: new[] { "TenantId", "InstanceId" });
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
                name: "HostDeliveryReceipts");

            migrationBuilder.DropTable(
                name: "HostExpenses");

            migrationBuilder.DropTable(
                name: "ApprovalCarbonCopies");

            migrationBuilder.DropTable(
                name: "ApprovalInstances");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitions");
        }
    }
}
