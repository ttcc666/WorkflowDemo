using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Approval.Database.Tests;

// Each provider owns its migration chain. These contexts are test hosts, not a replacement
// for the application's DbContext or migration history.
public abstract class ContractDbContext : ApprovalDbContext
{
    protected ContractDbContext(Approval.Core.Abstractions.IApprovalTenantContext? tenant = null) : base(tenant ?? new Approval.Core.Abstractions.FixedApprovalTenantContext("database-tests")) { }
    protected abstract string Provider { get; }
    protected override void ConfigureApprovalOptions(DbContextOptionsBuilder options)
    {
        var connection = Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION");
        switch (Provider)
        {
            case "MySql": options.UseMySQL(connection ?? "Server=localhost;Database=approval_design;User ID=root;Password=design"); break;
            case "SqlServer": options.UseSqlServer(connection ?? "Server=localhost;Database=approval_design;Integrated Security=true;TrustServerCertificate=true"); break;
            case "PostgreSql": options.UseNpgsql(connection ?? "Host=localhost;Database=approval_design;Username=postgres;Password=design"); break;
            case "Sqlite": options.UseSqlite(connection ?? $"Data Source={DatabaseFixture.SqlitePath}"); break;
            default: throw new InvalidOperationException("Unknown provider.");
        }
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureApproval(this);
}

public sealed class MySqlContractContext(Approval.Core.Abstractions.IApprovalTenantContext? tenant = null) : ContractDbContext(tenant) { protected override string Provider => "MySql"; }
public sealed class SqlServerContractContext(Approval.Core.Abstractions.IApprovalTenantContext? tenant = null) : ContractDbContext(tenant) { protected override string Provider => "SqlServer"; }
public sealed class PostgreSqlContractContext(Approval.Core.Abstractions.IApprovalTenantContext? tenant = null) : ContractDbContext(tenant) { protected override string Provider => "PostgreSql"; }
public sealed class SqliteContractContext(Approval.Core.Abstractions.IApprovalTenantContext? tenant = null) : ContractDbContext(tenant) { protected override string Provider => "Sqlite"; }

public sealed class MySqlDesignFactory : IDesignTimeDbContextFactory<MySqlContractContext>
{ public MySqlContractContext CreateDbContext(string[] args) => new(); }
public sealed class SqlServerDesignFactory : IDesignTimeDbContextFactory<SqlServerContractContext>
{ public SqlServerContractContext CreateDbContext(string[] args) => new(); }
public sealed class PostgreSqlDesignFactory : IDesignTimeDbContextFactory<PostgreSqlContractContext>
{ public PostgreSqlContractContext CreateDbContext(string[] args) => new(); }
public sealed class SqliteDesignFactory : IDesignTimeDbContextFactory<SqliteContractContext>
{ public SqliteContractContext CreateDbContext(string[] args) => new(); }
