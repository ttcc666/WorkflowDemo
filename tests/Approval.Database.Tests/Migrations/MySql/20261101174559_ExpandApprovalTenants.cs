using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Approval.Database.Tests;
namespace Approval.Database.Tests.Migrations.MySql;
[DbContext(typeof(MySqlContractContext))]
[Migration("20261101174559_ExpandApprovalTenants")]
public sealed partial class ExpandApprovalTenants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalTasks", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalOutbox", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalMessages", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalLogs", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalInstances", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalFiles", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalDrafts", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalDefinitions", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TenantId", table: "ApprovalCarbonCopies", type: "varchar(64)", maxLength: 64, nullable: true);
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
