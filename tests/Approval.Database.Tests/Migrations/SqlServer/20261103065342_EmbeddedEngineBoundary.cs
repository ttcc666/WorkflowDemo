using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Approval.Database.Tests.Migrations.SqlServer;

/// <summary>退出默认维护模型管理，保留旧表、约束和数据；不执行历史数据删除。</summary>
public partial class EmbeddedEngineBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Archives, maintenance jobs/audits, holds and deletion intents remain host-owned.
        // Their existing foreign keys and attachment metadata remain intact.
        // The new model snapshot excludes these entities. See docs/upgrade-0.3.md.
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No schema/data was removed by Up, so the previous model can be restored as-is.
        // Host-controlled purges performed separately require the host's database backup.
    }
}
