using System;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Database.Tests.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class TenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RequireApprovalTenantMapping();
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;", suppressTransaction: true);
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalCarbonCopies_ApprovalInstances_InstanceId",
                table: "ApprovalCarbonCopies");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalLogs_ApprovalInstances_InstanceId",
                table: "ApprovalLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalTasks_ApprovalInstances_InstanceId",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_AssigneeKind_AssigneeValue_Status",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_HandledBy",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_InstanceId",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_Status_NextRemindAt",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalOutbox_InstanceId",
                table: "ApprovalOutbox");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalOutbox_Status_NextAttemptAt",
                table: "ApprovalOutbox");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalMessages_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalLogs_InstanceId_Sequence",
                table: "ApprovalLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalLogs_OperatorId_Action",
                table: "ApprovalLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_BusinessType_BusinessKey",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_InitiatorId_CreatedAt",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_InitiatorId_Status",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_Status_CreatedAt",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_DraftId",
                table: "ApprovalFiles");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_InstanceId",
                table: "ApprovalFiles");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_UploadedAt",
                table: "ApprovalFiles");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalDrafts_UserId_UpdatedAt",
                table: "ApprovalDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalDefinitions_Key_Version",
                table: "ApprovalDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalCarbonCopies_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalCarbonCopies_InstanceId",
                table: "ApprovalCarbonCopies");



            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalTasks",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalOutbox",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);



            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalLogs",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalInstances",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalFiles",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalDrafts",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalDefinitions",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "ApprovalCarbonCopies",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ApprovalInstances_TenantId_Id",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ApprovalDrafts_TenantId_Id",
                table: "ApprovalDrafts",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ApprovalDefinitions_TenantId_Id",
                table: "ApprovalDefinitions",
                columns: new[] { "TenantId", "Id" });



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
                name: "IX_ApprovalOutbox_TenantId_InstanceId",
                table: "ApprovalOutbox",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_TenantId_Status_NextAttemptAt",
                table: "ApprovalOutbox",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_TenantId_InstanceId",
                table: "ApprovalMessages",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_TenantId_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages",
                columns: new[] { "TenantId", "UserId", "IsRead", "CreatedAt" });



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
                name: "IX_ApprovalDrafts_TenantId_UserId_UpdatedAt",
                table: "ApprovalDrafts",
                columns: new[] { "TenantId", "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_TenantId_Key_Version",
                table: "ApprovalDefinitions",
                columns: new[] { "TenantId", "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies",
                columns: new[] { "TenantId", "AssigneeKind", "AssigneeValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_InstanceId",
                table: "ApprovalCarbonCopies",
                columns: new[] { "TenantId", "InstanceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalCarbonCopies_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalCarbonCopies",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalFiles_ApprovalDrafts_TenantId_DraftId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "DraftId" },
                principalTable: "ApprovalDrafts",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalFiles_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalFiles",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalInstances_ApprovalDefinitions_TenantId_DefinitionId",
                table: "ApprovalInstances",
                columns: new[] { "TenantId", "DefinitionId" },
                principalTable: "ApprovalDefinitions",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalLogs_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalLogs",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalMessages_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalMessages",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalOutbox_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalOutbox",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalTasks_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "InstanceId" },
                principalTable: "ApprovalInstances",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;", suppressTransaction: true);
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalCarbonCopies_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalCarbonCopies");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalFiles_ApprovalDrafts_TenantId_DraftId",
                table: "ApprovalFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalFiles_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalInstances_ApprovalDefinitions_TenantId_DefinitionId",
                table: "ApprovalInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalLogs_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalMessages_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalOutbox_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalOutbox");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalTasks_ApprovalInstances_TenantId_InstanceId",
                table: "ApprovalTasks");



            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_TenantId_AssigneeKind_AssigneeValue_Status",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_TenantId_HandledBy",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_TenantId_InstanceId",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_TenantId_Status_NextRemindAt",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalOutbox_TenantId_InstanceId",
                table: "ApprovalOutbox");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalOutbox_TenantId_Status_NextAttemptAt",
                table: "ApprovalOutbox");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalMessages_TenantId_InstanceId",
                table: "ApprovalMessages");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalMessages_TenantId_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages");



            migrationBuilder.DropIndex(
                name: "IX_ApprovalLogs_TenantId_InstanceId_Sequence",
                table: "ApprovalLogs");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalLogs_TenantId_OperatorId_Action",
                table: "ApprovalLogs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ApprovalInstances_TenantId_Id",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_TenantId_BusinessType_BusinessKey",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_TenantId_DefinitionId",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_TenantId_InitiatorId_CreatedAt",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_TenantId_InitiatorId_Status",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalInstances_TenantId_Status_CreatedAt",
                table: "ApprovalInstances");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_TenantId_DraftId",
                table: "ApprovalFiles");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_TenantId_InstanceId",
                table: "ApprovalFiles");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalFiles_TenantId_UploadedAt",
                table: "ApprovalFiles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ApprovalDrafts_TenantId_Id",
                table: "ApprovalDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalDrafts_TenantId_UserId_UpdatedAt",
                table: "ApprovalDrafts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ApprovalDefinitions_TenantId_Id",
                table: "ApprovalDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalDefinitions_TenantId_Key_Version",
                table: "ApprovalDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalCarbonCopies_TenantId_InstanceId",
                table: "ApprovalCarbonCopies");



            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalTasks", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalOutbox", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalMessages", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);



            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalLogs", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalInstances", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalFiles", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalDrafts", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalDefinitions", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(name: "TenantId", table: "ApprovalCarbonCopies", type: "TEXT", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 64);

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

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_InstanceId",
                table: "ApprovalOutbox",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalOutbox_Status_NextAttemptAt",
                table: "ApprovalOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalMessages_UserId_IsRead_CreatedAt",
                table: "ApprovalMessages",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

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
                name: "IX_ApprovalInstances_BusinessType_BusinessKey",
                table: "ApprovalInstances",
                columns: new[] { "BusinessType", "BusinessKey" });

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
                name: "IX_ApprovalDrafts_UserId_UpdatedAt",
                table: "ApprovalDrafts",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitions_Key_Version",
                table: "ApprovalDefinitions",
                columns: new[] { "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_AssigneeKind_AssigneeValue",
                table: "ApprovalCarbonCopies",
                columns: new[] { "AssigneeKind", "AssigneeValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalCarbonCopies_InstanceId",
                table: "ApprovalCarbonCopies",
                column: "InstanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalCarbonCopies_ApprovalInstances_InstanceId",
                table: "ApprovalCarbonCopies",
                column: "InstanceId",
                principalTable: "ApprovalInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalLogs_ApprovalInstances_InstanceId",
                table: "ApprovalLogs",
                column: "InstanceId",
                principalTable: "ApprovalInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalTasks_ApprovalInstances_InstanceId",
                table: "ApprovalTasks",
                column: "InstanceId",
                principalTable: "ApprovalInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
