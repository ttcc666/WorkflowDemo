using System.Linq.Expressions;
using Approval.Core.Abstractions;
using Approval.Core.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Approval.Core.Actions;
using Approval.Core.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Approval.Core.Tests;

public sealed class PartitionSecurityTests
{
    [Theory]
    [InlineData("scalar-raw", false)]
    [InlineData("scalar-raw", true)]
    [InlineData("scalar-interpolated", false)]
    [InlineData("scalar-interpolated", true)]
    [InlineData("entity-raw", false)]
    [InlineData("entity-raw", true)]
    [InlineData("entity-interpolated", false)]
    [InlineData("entity-interpolated", true)]
    [InlineData("write-raw", false)]
    [InlineData("write-raw", true)]
    [InlineData("write-interpolated", false)]
    [InlineData("write-interpolated", true)]
    [InlineData("compiled-raw", false)]
    [InlineData("compiled-raw", true)]
    public async Task RawSql_RejectsEveryEfEntryAndPreservesBothTenants(string entry, bool async)
    {
        await using var fixture = await Fixture.Create();
        await using (var db = fixture.Open("alpha"))
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                switch (entry)
                {
                    case "scalar-raw":
                        var raw = db.Database.SqlQueryRaw<string>("SELECT Secret AS Value FROM SecurityRows");
                        if (async) await raw.ToArrayAsync(); else raw.ToArray(); break;
                    case "scalar-interpolated":
                        var sql = db.Database.SqlQuery<string>($"SELECT Secret AS Value FROM SecurityRows WHERE Id > {0}");
                        if (async) await sql.ToArrayAsync(); else sql.ToArray(); break;
                    case "entity-raw":
                        var entities = db.Rows.FromSqlRaw("SELECT * FROM SecurityRows");
                        if (async) await entities.ToArrayAsync(); else entities.ToArray(); break;
                    case "entity-interpolated":
                        var interpolated = db.Rows.FromSqlInterpolated($"SELECT * FROM SecurityRows WHERE Id > {0}");
                        if (async) await interpolated.ToArrayAsync(); else interpolated.ToArray(); break;
                    case "write-raw":
                        if (async) await db.Database.ExecuteSqlRawAsync("UPDATE SecurityRows SET Secret = 'stolen'");
                        else db.Database.ExecuteSqlRaw("UPDATE SecurityRows SET Secret = 'stolen'"); break;
                    case "write-interpolated":
                        if (async) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM SecurityRows WHERE Id > {0}");
                        else db.Database.ExecuteSqlInterpolated($"DELETE FROM SecurityRows WHERE Id > {0}"); break;
                    case "compiled-raw":
                        if (async) await EF.CompileAsyncQuery((SecurityContext c) => c.Rows.FromSqlRaw("SELECT * FROM SecurityRows").Count())(db);
                        else EF.CompileQuery((SecurityContext c) => c.Rows.FromSqlRaw("SELECT * FROM SecurityRows").Count())(db); break;
                    default: throw new ArgumentOutOfRangeException(nameof(entry));
                }
            });
            // Check storage even when the protection is missing, before asserting rejection.
            await fixture.AssertIntact();
            Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("原生 SQL", error.Message);
        }
    }

    [Theory]
    [InlineData("property", false)]
    [InlineData("property", true)]
    [InlineData("ef-property", false)]
    [InlineData("ef-property", true)]
    [InlineData("projection", false)]
    [InlineData("projection", true)]
    [InlineData("compiled", false)]
    [InlineData("compiled", true)]
    public async Task BulkUpdate_RejectsPartitionSettersAndPreservesBothTenants(string shape, bool async)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        var error = await Record.ExceptionAsync(async () =>
        {
            if (shape == "compiled")
            {
                if (async) await EF.CompileAsyncQuery((SecurityContext c) => c.Rows.ExecuteUpdate(s => s.SetProperty(r => r.ScopeKey, "bravo")))(db);
                else EF.CompileQuery((SecurityContext c) => c.Rows.ExecuteUpdate(s => s.SetProperty(r => r.ScopeKey, "bravo")))(db);
            }
            else if (shape == "projection")
            {
                var query = db.Rows.Select(r => new { Row = r, r.Id });
                if (async) await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Row.ScopeKey, "bravo"));
                else query.ExecuteUpdate(s => s.SetProperty(r => r.Row.ScopeKey, "bravo"));
            }
            else if (shape == "ef-property")
            {
                if (async) await db.Rows.ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<string>(r, "ScopeKey"), "bravo"));
                else db.Rows.ExecuteUpdate(s => s.SetProperty(r => EF.Property<string>(r, "ScopeKey"), "bravo"));
            }
            else
            {
                if (async) await db.Rows.ExecuteUpdateAsync(s => s.SetProperty(r => r.ScopeKey, "bravo"));
                else db.Rows.ExecuteUpdate(s => s.SetProperty(r => r.ScopeKey, "bravo"));
            }
        });
        await fixture.AssertIntact();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("分区", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkBusinessWrites_KeepFiltersAndCannotTouchBravo(bool async)
    {
        await using var fixture = await Fixture.Create();
        await using (var db = fixture.Open("alpha"))
        {
            var bravo = db.Rows.Where(r => r.Id == 2);
            Assert.Equal(0, async ? await bravo.ExecuteDeleteAsync() : bravo.ExecuteDelete());
            Assert.Equal(0, async ? await bravo.ExecuteUpdateAsync(s => s.SetProperty(r => r.Secret, "stolen"))
                : bravo.ExecuteUpdate(s => s.SetProperty(r => r.Secret, "stolen")));
            var own = db.Rows.Where(r => r.Id > 0).Select(r => new { Row = r });
            Assert.Equal(1, async ? await own.ExecuteUpdateAsync(s => s.SetProperty(r => r.Row.Secret, "changed"))
                : own.ExecuteUpdate(s => s.SetProperty(r => r.Row.Secret, "changed")));
        }
        await fixture.AssertIntact("changed");
        await using (var db = fixture.Open("alpha")) Assert.Equal(1, async ? await db.Rows.ExecuteDeleteAsync() : db.Rows.ExecuteDelete());
        await using var alpha = fixture.Open("alpha");
        await using var bravoCheck = fixture.Open("bravo");
        Assert.Empty(await alpha.Rows.ToArrayAsync());
        Assert.Equal("bravo-secret", (await bravoCheck.Rows.SingleAsync()).Secret);
    }

    [Theory]
    [InlineData("update", false)]
    [InlineData("update", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("named-delete", true)]
    public async Task BulkFilterRemoval_RejectsAndPreservesBothTenants(string operation, bool async)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        var query = operation == "named-delete" ? db.Rows.IgnoreQueryFilters(["SoftDelete"]) : db.Rows.IgnoreQueryFilters();
        var error = await Record.ExceptionAsync(async () =>
        {
            if (operation == "update")
            {
                if (async) await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Secret, "stolen"));
                else query.ExecuteUpdate(s => s.SetProperty(r => r.Secret, "stolen"));
            }
            else if (async) await query.ExecuteDeleteAsync(); else query.ExecuteDelete();
        });
        await fixture.AssertIntact();
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData("forged-add", false)]
    [InlineData("forged-add", true)]
    [InlineData("attached-update", false)]
    [InlineData("attached-update", true)]
    [InlineData("attached-delete", false)]
    [InlineData("attached-delete", true)]
    public async Task TrackedWrites_RejectForeignPartitionWithoutPartialWrites(string attack, bool async)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        (await db.Rows.SingleAsync()).Secret = "must-roll-back";
        var foreign = new SecurityRow { Id = attack == "forged-add" ? 3 : 2, ScopeKey = "bravo", Secret = "stolen" };
        if (attack == "forged-add") db.Add(foreign);
        else { db.Attach(foreign); if (attack == "attached-delete") db.Remove(foreign); else db.Entry(foreign).Property(r => r.Secret).IsModified = true; }
        var error = await Record.ExceptionAsync(async () => { if (async) await db.SaveChangesAsync(); else db.SaveChanges(); });
        await fixture.AssertIntact();
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("compiled")]
    [InlineData("save")]
    public async Task ContextPartitionChange_RejectsCachedOperations(string operation)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        var compiled = EF.CompileAsyncQuery((SecurityContext c) => c.Rows.Count());
        Assert.Equal(1, await compiled(db));
        var own = await db.Rows.SingleAsync();
        db.PartitionKey = "bravo";
        var error = await Record.ExceptionAsync(async () =>
        {
            if (operation == "compiled") await compiled(db);
            else if (operation == "save") { own.Secret = "stolen"; await db.SaveChangesAsync(); }
            else await db.Rows.ToArrayAsync();
        });
        await fixture.AssertIntact();
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task MissingPartition_RejectsInsteadOfReturningUnscopedData()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("");
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachedForeignId_WithForgedOwnPartitionFailsDatabasePredicate(bool delete)
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        (await db.Rows.SingleAsync()).Secret = "must-roll-back";
        var foreign = new SecurityRow { Id = 2, ScopeKey = "alpha", Secret = "stolen" };
        db.Attach(foreign);
        if (delete) db.Remove(foreign); else db.Entry(foreign).Property(r => r.Secret).IsModified = true;
        var error = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        await fixture.AssertIntact();
        Assert.IsType<DbUpdateConcurrencyException>(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompiledDelete_RetainsTenantFilterAcrossContexts(bool async)
    {
        await using var fixture = await Fixture.Create();
        await using (var db = fixture.Open("alpha"))
        {
            Assert.Equal(0, async ? await EF.CompileAsyncQuery((SecurityContext c) => c.Rows.Where(r => r.Id == 2).ExecuteDelete())(db)
                : EF.CompileQuery((SecurityContext c) => c.Rows.Where(r => r.Id == 2).ExecuteDelete())(db));
        }
        await fixture.AssertIntact();
    }

    [Fact]
    public async Task PooledContext_RejectsEvenTheFirstLease()
    {
        await using var fixture = await Fixture.Create();
        var builder = fixture.Options();
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.FindExtension<CoreOptionsExtension>()!.WithMaxPoolSize(1));
        await using var db = new SecurityContext(builder.Options, "alpha");
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.Contains("池化", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task SqlAction_PartitionedContextRejectsUnscopedRegisteredQuery()
    {
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        using var services = new ServiceCollection().BuildServiceProvider();
        var opened = false;
        var accessor = new ApprovalDbContextAccessor(db, new ApprovalAuthorizationService(null!), null!, null!);
        var registration = new SqlActionRegistration(new("host", "all", "SELECT Secret FROM SecurityRows", new Dictionary<string, System.Data.DbType>()),
            _ => { opened = true; return new SqliteConnection("Data Source=:memory:"); });
        var handler = new SqlActionHandler(services, accessor, [registration]);
        var config = JsonSerializer.SerializeToElement(new { dataSource = "host", queryId = "all", parameters = new { } });
        var error = await Record.ExceptionAsync(() => handler.ExecuteAsync(null!, config, default));
        await fixture.AssertIntact();
        Assert.False(opened);
        Assert.Contains("分区", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task MissingFilter_FailsClosedBeforeReadingStoredData()
    {
        await using var fixture = await Fixture.Create();
        await using var db = new MissingFilterContext(fixture.Options().Options);
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.Contains("过滤", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task SoftDeleteFilterAlone_CannotSubstituteForPartitionFilter()
    {
        await using var fixture = await Fixture.Create();
        await using var db = new SoftDeleteOnlyContext(fixture.Options().Options);
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.Contains("过滤", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task DisjunctivePartitionFilter_CannotMakeForeignRowsVisible()
    {
        await using var fixture = await Fixture.Create();
        await using var db = new DisjunctiveFilterContext(fixture.Options().Options);
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.Contains("过滤", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Fact]
    public async Task TenantFilterWithSoftDelete_PreservesBothBoundaries()
    {
        await using var fixture = await Fixture.Create();
        foreach (var tenant in new[] { "alpha", "bravo" })
        {
            await using var db = new SoftDeleteContext(fixture.Options().Options, tenant);
            Assert.Equal(tenant + "-secret", (await db.Rows.SingleAsync()).Secret);
            Assert.Equal(0, await db.Rows.Where(r => r.ScopeKey != tenant).CountAsync());
        }
        await fixture.AssertIntact();
    }

    [Fact]
    public async Task MappedSqlQuery_IsRejectedBeforeReadingStoredData()
    {
        await using var fixture = await Fixture.Create();
        await using var db = new SqlMappedContext(fixture.Options().Options);
        var error = await Record.ExceptionAsync(() => db.Rows.ToArrayAsync());
        await fixture.AssertIntact();
        Assert.Contains("原生 SQL", Assert.IsType<InvalidOperationException>(error).Message);
    }

    [Theory]
    [InlineData("bravo")]
    [InlineData(null)]
    public async Task HostScopeChange_RejectsFixedContextEvenForCachedQuery(string? next)
    {
        await using var fixture = await Fixture.Create();
        var scopes = new HostScopes { Current = "alpha" };
        using var services = new ServiceCollection().AddSingleton<IApprovalHostScopeProvider>(scopes).BuildServiceProvider();
        await using var db = new SecurityContext(fixture.Options().UseApplicationServiceProvider(services).Options, "alpha");
        var query = EF.CompileAsyncQuery((SecurityContext c) => c.Rows.Single());
        Assert.Equal("alpha-secret", (await query(db)).Secret);
        scopes.Current = next;
        var error = await Record.ExceptionAsync(() => query(db));
        await fixture.AssertIntact();
        Assert.Equal(ApprovalErrorCode.Forbidden, Assert.IsType<ApprovalException>(error).Code);
    }

    [Fact]
    public async Task DirectAdoNet_IsOutsideEfInterceptionBoundary()
    {
        // Evidence of the trust boundary, NOT a claim of database-level isolation.
        await using var fixture = await Fixture.Create();
        await using var db = fixture.Open("alpha");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SecurityRows";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
        await fixture.AssertIntact();
    }

    public sealed class SecurityRow
    {
        public int Id { get; set; }
        public string ScopeKey { get; set; } = "";
        public string Secret { get; set; } = "";
    }

    public class SecurityContext(DbContextOptions<SecurityContext> options, string partition) : DbContext(options), IApprovalPartitionContext
    {
        public string PartitionKey { get; set; } = partition;
        public DbSet<SecurityRow> Rows => Set<SecurityRow>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureApproval();
            model.Entity<SecurityRow>().ToTable("SecurityRows");
            foreach (var entity in model.Model.GetEntityTypes().ToArray())
            {
                model.Entity(entity.ClrType).Property<string>("ScopeKey").IsRequired().HasMaxLength(64);
                var row = Expression.Parameter(entity.ClrType, "row");
                model.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(Expression.Equal(
                    Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], row, Expression.Constant("ScopeKey")),
                    Expression.Property(Expression.Constant(this), nameof(PartitionKey))), row));
            }
            model.ConfigureApprovalPartition("ScopeKey", typeof(SecurityRow));
        }
    }

    public sealed class MissingFilterContext(DbContextOptions<SecurityContext> options) : SecurityContext(options, "alpha")
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model.Entity<SecurityRow>().HasQueryFilter((Expression<Func<SecurityRow, bool>>?)null);
        }
    }

    public sealed class SoftDeleteOnlyContext(DbContextOptions<SecurityContext> options) : SecurityContext(options, "alpha")
    {
        protected override void OnModelCreating(ModelBuilder model)
        { base.OnModelCreating(model); model.Entity<SecurityRow>().HasQueryFilter(r => r.Id > 0); }
    }

    public sealed class DisjunctiveFilterContext(DbContextOptions<SecurityContext> options) : SecurityContext(options, "alpha")
    {
        protected override void OnModelCreating(ModelBuilder model)
        { base.OnModelCreating(model); model.Entity<SecurityRow>().HasQueryFilter(r => r.ScopeKey == PartitionKey || r.Id > 0); }
    }

    public sealed class SoftDeleteContext(DbContextOptions<SecurityContext> options, string partition) : SecurityContext(options, partition)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { base.OnModelCreating(model); model.Entity<SecurityRow>().HasQueryFilter(r => r.ScopeKey == PartitionKey && r.Id > 0); }
    }

    public sealed class SqlMappedContext(DbContextOptions<SecurityContext> options) : SecurityContext(options, "alpha")
    {
        protected override void OnModelCreating(ModelBuilder model)
        { base.OnModelCreating(model); model.Entity<SecurityRow>().ToSqlQuery("SELECT * FROM SecurityRows"); }
    }

    private sealed class HostScopes : IApprovalHostScopeProvider
    {
        public string? Current;
        public IApprovalHostScope Capture() => new HostScope(Current);
        public Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        private sealed record HostScope(string? PartitionKey) : IApprovalHostScope
        { public IDisposable Enter() => throw new NotSupportedException(); }
    }

    private sealed class Fixture(SqliteConnection connection) : IAsyncDisposable
    {
        public DbContextOptionsBuilder<SecurityContext> Options() => new DbContextOptionsBuilder<SecurityContext>().UseSqlite(connection).UseApproval();
        public SecurityContext Open(string partition, bool guarded = true)
        {
            var options = new DbContextOptionsBuilder<SecurityContext>().UseSqlite(connection);
            if (guarded) options.UseApproval();
            return new(options.Options, partition);
        }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            // Offline schema/data setup; all attacks and verification use guarded contexts.
            await using var db = fixture.Open("alpha", guarded: false);
            await db.Database.EnsureCreatedAsync();
            db.AddRange(new SecurityRow { Id = 1, ScopeKey = "alpha", Secret = "alpha-secret" },
                new SecurityRow { Id = 2, ScopeKey = "bravo", Secret = "bravo-secret" });
            await db.SaveChangesAsync();
            return fixture;
        }
        public async Task AssertIntact(string alphaSecret = "alpha-secret")
        {
            foreach (var tenant in new[] { "alpha", "bravo" })
            {
                await using var db = Open(tenant);
                var row = Assert.Single(await db.Rows.AsNoTracking().ToArrayAsync());
                Assert.Equal(tenant == "alpha" ? 1 : 2, row.Id);
                Assert.Equal(tenant, row.ScopeKey);
                Assert.Equal(tenant == "alpha" ? alphaSecret : "bravo-secret", row.Secret);
                Assert.Equal(0, await db.Rows.CountAsync(r => r.ScopeKey != tenant));
            }
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
