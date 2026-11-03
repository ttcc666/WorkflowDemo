using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Database.Tests.Migrations.MySql
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
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalBusinessSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LookupHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BusinessType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    BusinessKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    InstanceId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalBusinessSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalBusinessSlots_ApprovalInstances_TenantId_InstanceId",
                        columns: x => new { x.TenantId, x.InstanceId },
                        principalTable: "ApprovalInstances",
                        principalColumns: new[] { "TenantId", "Id" });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TenantId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
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
