using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Certificate.Api.Infrastructure;

/// <summary>迁移工具只构造模型，不启动 Web 服务、发布流程或打开业务数据库。</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=certificate-design.db").Options);
}
