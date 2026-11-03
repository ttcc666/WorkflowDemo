using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Database.Tests.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AtomicOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "ApprovalOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalBusinessSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "ApprovalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LookupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HashVersion = table.Column<int>(type: "int", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalOperations", x => x.Id);
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
                name: "IX_ApprovalOperations_TenantId_LookupHash",
                table: "ApprovalOperations",
                columns: new[] { "TenantId", "LookupHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalBusinessSlots");

            migrationBuilder.DropTable(
                name: "ApprovalOperations");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "ApprovalOutbox");
        }
    }
}
