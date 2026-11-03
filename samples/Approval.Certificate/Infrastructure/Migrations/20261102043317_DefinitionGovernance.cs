using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Certificate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DefinitionGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDefinitionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangeNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DefinitionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DraftJson = table.Column<string>(type: "TEXT", nullable: true),
                    DraftSourceDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DraftRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedDraftRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    LastVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActiveDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
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
                name: "IX_ApprovalDefinitionManagement_TenantId_DraftSourceDefinitionId",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "DraftSourceDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDefinitionManagement_TenantId_Key",
                table: "ApprovalDefinitionManagement",
                columns: new[] { "TenantId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDefinitionAudits");

            migrationBuilder.DropTable(
                name: "ApprovalDefinitionManagement");
        }
    }
}
