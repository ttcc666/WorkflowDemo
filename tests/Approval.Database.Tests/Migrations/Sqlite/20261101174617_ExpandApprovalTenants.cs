using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Approval.Database.Tests;
namespace Approval.Database.Tests.Migrations.Sqlite;
[DbContext(typeof(SqliteContractContext))]
[Migration("20261101174617_ExpandApprovalTenants")]
public sealed partial class ExpandApprovalTenants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalTasks", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalOutbox", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalMessages", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalLogs", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalInstances", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalFiles", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalDrafts", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalDefinitions", type: "TEXT", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalCarbonCopies", type: "TEXT", maxLength: 64, nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalTasks");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalOutbox");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalMessages");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalLogs");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalInstances");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalFiles");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalDrafts");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalDefinitions");
        migrationBuilder.DropColumn(name: "TenantId", table: "ApprovalCarbonCopies");
    }
}
