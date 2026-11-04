using Approval.Forms;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Approval.ReferenceHost;

public abstract class ReferenceDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    { base.OnConfiguring(options); options.UseApproval(); }

    protected override void OnModelCreating(ModelBuilder model) => model.ConfigureApproval().ConfigureApprovalForms();
}
public sealed class SqliteReferenceContext(DbContextOptions<SqliteReferenceContext> options) : ReferenceDbContext(options);
public sealed class MySqlReferenceContext(DbContextOptions<MySqlReferenceContext> options) : ReferenceDbContext(options);
public sealed class SqlServerReferenceContext(DbContextOptions<SqlServerReferenceContext> options) : ReferenceDbContext(options);
public sealed class PostgreSqlReferenceContext(DbContextOptions<PostgreSqlReferenceContext> options) : ReferenceDbContext(options);

public static class ReferenceDatabase
{
    public static void Configure(DbContextOptionsBuilder options, string provider, string connection)
    {
        switch (provider)
        {
            case "Sqlite": options.UseSqlite(connection); break;
            case "MySql": options.UseMySQL(connection, o => o.EnableRetryOnFailure()); break;
            case "SqlServer": options.UseSqlServer(connection, o => o.EnableRetryOnFailure()); break;
            case "PostgreSql": options.UseNpgsql(connection, o => o.EnableRetryOnFailure()); break;
            default: throw new InvalidOperationException("Unknown Database:Provider.");
        }
    }
    public static void Register(IServiceCollection services, IConfiguration config, Action<ApprovalBuilder> configure)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var connection = config.GetConnectionString("Approval");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Configure ConnectionStrings:Approval.");
        switch (provider)
        {
            case "Sqlite": Add<SqliteReferenceContext>(); break;
            case "MySql": Add<MySqlReferenceContext>(); break;
            case "SqlServer": Add<SqlServerReferenceContext>(); break;
            case "PostgreSql": Add<PostgreSqlReferenceContext>(); break;
            default: throw new InvalidOperationException("Unknown Database:Provider.");
        }
        void Add<T>() where T : ReferenceDbContext
        {
            services.AddDbContext<T>(o => Configure(o, provider, connection));
            services.AddScoped<ReferenceDbContext>(sp => sp.GetRequiredService<T>());
            configure(services.AddApprovalWorkflow<T>(o =>
            {
                o.RequireOperationId = true;
                o.RequireBusinessHandler = true;
                o.EnableBackgroundServices = config.GetValue("Approval:EnableBackgroundServices", true);
                o.DisplayTimeZone = TimeZoneInfo.Utc;
                // Lifecycle and deletion stay disabled. Operators must review policy first.
            }));
        }
    }
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = await services.GetRequiredService<IApprovalScopeFactory>().CreateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Database.MigrateAsync(ct);
    }
}
public abstract class ReferenceDesignFactory<T> : IDesignTimeDbContextFactory<T> where T : ReferenceDbContext
{
    protected abstract string Provider { get; }
    protected abstract string Connection { get; }
    public T CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<T>();
        ReferenceDatabase.Configure(options, Provider, Connection);
        return (T)Activator.CreateInstance(typeof(T), options.Options)!;
    }
}
public sealed class SqliteReferenceFactory : ReferenceDesignFactory<SqliteReferenceContext>
{ protected override string Provider => "Sqlite"; protected override string Connection => "Data Source=approval_design.db"; }
public sealed class MySqlReferenceFactory : ReferenceDesignFactory<MySqlReferenceContext>
{ protected override string Provider => "MySql"; protected override string Connection => "Server=localhost;Database=approval_design;User ID=design;Password=design;CharSet=utf8mb4"; }
public sealed class SqlServerReferenceFactory : ReferenceDesignFactory<SqlServerReferenceContext>
{ protected override string Provider => "SqlServer"; protected override string Connection => "Server=localhost;Database=approval_design;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=false"; }
public sealed class PostgreSqlReferenceFactory : ReferenceDesignFactory<PostgreSqlReferenceContext>
{ protected override string Provider => "PostgreSql"; protected override string Connection => "Host=localhost;Database=approval_design;Username=design;Password=design"; }
