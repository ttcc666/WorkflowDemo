using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Database.Tests.Migrations.PostgreSql
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
                    DraftRevision = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
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
