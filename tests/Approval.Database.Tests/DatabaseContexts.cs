using Approval.Forms;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Approval.Database.Tests;

// Each provider owns its migration chain. These contexts are test hosts, not a replacement
// for the application's DbContext or migration history.
public abstract class ContractDbContext : DbContext
{
    protected ContractDbContext() { }
    protected abstract string Provider { get; }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);
        options.UseApproval();
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
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureApproval().ConfigureApprovalForms();
}

public sealed class MySqlContractContext() : ContractDbContext() { protected override string Provider => "MySql"; }
public sealed class SqlServerContractContext() : ContractDbContext() { protected override string Provider => "SqlServer"; }
public sealed class PostgreSqlContractContext() : ContractDbContext() { protected override string Provider => "PostgreSql"; }
public sealed class SqliteContractContext() : ContractDbContext() { protected override string Provider => "Sqlite"; }

public sealed class MySqlDesignFactory : IDesignTimeDbContextFactory<MySqlContractContext>
{ public MySqlContractContext CreateDbContext(string[] args) => new(); }

// Storage-level constraints are tested through a separate offline context. Application protection
// scenarios always use ContractDbContext/DI, with UseApproval installed.
internal sealed class DatabaseStorageContext(string provider) : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var connection = Environment.GetEnvironmentVariable("APPROVAL_TEST_CONNECTION");
        switch (provider)
        {
            case "MySql": options.UseMySQL(connection!); break;
            case "SqlServer": options.UseSqlServer(connection!); break;
            case "PostgreSql": options.UseNpgsql(connection!); break;
            default: options.UseSqlite($"Data Source={DatabaseFixture.SqlitePath}"); break;
        }
    }
    protected override void OnModelCreating(ModelBuilder model) => model.ConfigureApproval().ConfigureApprovalForms();
}
public sealed class SqlServerDesignFactory : IDesignTimeDbContextFactory<SqlServerContractContext>
{ public SqlServerContractContext CreateDbContext(string[] args) => new(); }
public sealed class PostgreSqlDesignFactory : IDesignTimeDbContextFactory<PostgreSqlContractContext>
{ public PostgreSqlContractContext CreateDbContext(string[] args) => new(); }
public sealed class SqliteDesignFactory : IDesignTimeDbContextFactory<SqliteContractContext>
{ public SqliteContractContext CreateDbContext(string[] args) => new(); }
