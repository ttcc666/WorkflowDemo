using System.Reflection;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    // Read-only negative diagnostics: use the existing provider snapshots and alter only test metadata.
    // No migration is generated/applied and no table/column is created for the shadow property.
    private sealed class DiagnosticContext(DbContextOptions<DiagnosticContext> options, string provider, bool drift, bool pending) : DbContext(options)
    {
        public string Provider { get; } = provider;
        public bool Pending { get; } = pending;
        public bool Drift { get; } = drift;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureApproval().ConfigureApprovalForms();
            if (Drift) model.Entity<ApprovalInstance>().Property<string>("P4UnappliedColumn");
        }
    }
    private sealed class DiagnosticModelKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => context is DiagnosticContext diagnostic
            ? (context.GetType(), diagnostic.Provider, diagnostic.Drift, diagnostic.Pending, designTime) : (object)(context.GetType(), designTime);
    }
    private sealed class DiagnosticMigrations(ICurrentDbContext current) : IMigrationsAssembly
    {
        private DiagnosticContext Context => (DiagnosticContext)current.Context;
        public IReadOnlyDictionary<string, TypeInfo> Migrations => Context.Pending
            ? new Dictionary<string, TypeInfo> { ["99999999999999_P4PendingMetadata"] = typeof(Migrations.Sqlite.InitialApproval).GetTypeInfo() }
            : new Dictionary<string, TypeInfo>();
        public ModelSnapshot? ModelSnapshot => Context.Provider switch
        {
            "MySql" => new Migrations.MySql.MySqlContractContextModelSnapshot(),
            "SqlServer" => new Migrations.SqlServer.SqlServerContractContextModelSnapshot(),
            "PostgreSql" => new Migrations.PostgreSql.PostgreSqlContractContextModelSnapshot(),
            _ => new Migrations.Sqlite.SqliteContractContextModelSnapshot()
        };
        public Assembly Assembly => typeof(DatabaseContractTests).Assembly;
        public string? FindMigrationId(string nameOrId) => Migrations.ContainsKey(nameOrId) ? nameOrId : null;
        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) => throw new InvalidOperationException("diagnostics must never apply migrations");
    }

    [Theory]
    [InlineData("drift")]
    [InlineData("pending-migration")]
    public async Task Diagnostics_ModelDriftOrPendingMigrationFailsClosedWithoutWrites(string mode)
    {
        await using var host = await AtomicHostAsync(new AtomicFaults());
        await using var schema = fixture.Create();
        var connection = schema.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<DiagnosticContext>().ReplaceService<IMigrationsAssembly, DiagnosticMigrations>()
            .ReplaceService<IModelCacheKeyFactory, DiagnosticModelKey>();
        switch (fixture.Provider)
        {
            case "MySql": options.UseMySQL(connection); break;
            case "SqlServer": options.UseSqlServer(connection); break;
            case "PostgreSql": options.UseNpgsql(connection); break;
            default: options.UseSqlite(connection); break;
        }
        await using var diagnosticDb = new DiagnosticContext(options.Options, fixture.Provider, mode == "drift", mode == "pending-migration");
        await AtomicRun(host, "admin", async sp =>
        {
            var accessor = new ApprovalDbContextAccessor(diagnosticDb, sp.GetRequiredService<ApprovalAuthorizationService>(),
                sp.GetRequiredService<IApprovalCommandExecutor>(), sp.GetRequiredService<IApprovalActorContext>());
            var diagnostics = new ApprovalDiagnosticsService(accessor, sp.GetRequiredService<ApprovalEngine>(),
                sp.GetRequiredService<ApprovalOptions>(), TimeProvider.System, sp.GetRequiredService<ApprovalBackgroundState>(), []);
            var result = await diagnostics.CheckAsync();
            Assert.False(result.Ready); Assert.False(result.DataAvailable); Assert.True(result.SchemaManaged);
            Assert.Equal(mode == "drift", result.ModelDrift); Assert.Equal(mode == "pending-migration" ? 1 : 0, result.PendingMigrations);
            Assert.Empty(result.Findings); Assert.Equal(0, result.RunningInstances);
            var db = sp.GetRequiredService<AtomicContractContext>();
            Assert.Equal(1, await db.Set<ApprovalDefinition>().CountAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync()); Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOperation>().ToListAsync()); Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            Assert.Empty(await db.Set<AtomicHostRecord>().ToListAsync()); return true;
        });
    }
}
