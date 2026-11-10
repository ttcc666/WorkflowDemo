using System.Linq.Expressions;
using Approval.Core.Domain;
using Approval.Core.Abstractions;
using Approval.Core.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class ApprovalPartitionQueryTests
{
    [Theory]
    [InlineData("projection")]
    [InlineData("include")]
    [InlineData("include-string")]
    [InlineData("then-include")]
    [InlineData("auto-include")]
    [InlineData("compiled")]
    [InlineData("named")]
    [InlineData("host-only")]
    [InlineData("raw-source")]
    public async Task PartitionContext_RejectsFilterRemovalForEveryQueryShape(string shape)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);
        await using var db = new PartitionContext(new DbContextOptionsBuilder<PartitionContext>().UseSqlite(connection).UseApproval().Options, "alpha");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var links = db.Set<SharedLink>().IgnoreQueryFilters().IgnoreAutoIncludes();
            switch (shape)
            {
                case "projection": await links.Select(l => l.Instance.Title).ToListAsync(); break;
                case "include": await links.Include(l => l.Instance).ToListAsync(); break;
                case "include-string": await links.Include("Instance.Tasks").ToListAsync(); break;
                case "then-include": await links.Include(l => l.Instance).ThenInclude(i => i.Tasks).ToListAsync(); break;
                case "auto-include": await db.Set<SharedLink>().IgnoreQueryFilters().ToListAsync(); break;
                case "compiled":
                    var compiled = EF.CompileAsyncQuery((PartitionContext c) => c.Set<SharedLink>().IgnoreQueryFilters().Select(l => l.Instance.Title).First());
                    await compiled(db); break;
                case "named": await db.Set<SharedLink>().IgnoreQueryFilters(["SoftDelete"]).Select(l => l.Instance.Title).ToListAsync(); break;
                case "host-only": await links.Select(l => l.Id).ToListAsync(); break;
                case "raw-source": await db.Database.SqlQueryRaw<Probe>("SELECT 1 AS Value").IgnoreQueryFilters().ToListAsync(); break;
                default: throw new InvalidOperationException("Unknown query shape");
            }
        });
        Assert.Contains("禁止移除查询过滤器", error.Message);
        Assert.Equal(new[] { "alpha-private" }, await db.Set<SharedLink>().Select(l => l.Instance.Title).ToArrayAsync());
    }

    [Fact]
    public async Task PartitionContext_DefaultQueriesRemainIsolated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await SeedAsync(connection);
        foreach (var tenant in new[] { "alpha", "bravo" })
        {
            await using var db = new PartitionContext(new DbContextOptionsBuilder<PartitionContext>().UseSqlite(connection).UseApproval().Options, tenant);
            Assert.Equal(new[] { tenant + "-private" }, await db.Set<SharedLink>().Select(l => l.Instance.Title).ToArrayAsync());
            Assert.Equal(tenant + "-private", Assert.Single(await db.Set<SharedLink>().ToListAsync()).Instance.Title);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Database.SqlQueryRaw<Probe>("SELECT 1 AS Value").SingleAsync());
        }
    }

    [Fact]
    public async Task NonpartitionedContext_CanRemoveHostFilters()
    {
        await using var db = new GlobalContext(new DbContextOptionsBuilder<GlobalContext>().UseSqlite("Data Source=:memory:").UseApproval().Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        db.AddRange(new GlobalRow { Id = 1 }, new GlobalRow { Id = 2, Deleted = true });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { 1 }, await db.Set<GlobalRow>().Select(r => r.Id).ToArrayAsync());
        Assert.Equal(new[] { 1, 2 }, await db.Set<GlobalRow>().IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
        Assert.Equal(new[] { 1, 2 }, await db.Set<GlobalRow>().IgnoreQueryFilters(["SoftDelete"]).OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM GlobalRow").SingleAsync());
        Assert.Equal(1, await db.Set<GlobalRow>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Deleted, true)));
        Assert.Empty(await db.Set<GlobalRow>().ToArrayAsync());
        Assert.Equal(2, await db.Database.ExecuteSqlRawAsync("UPDATE GlobalRow SET Deleted = 0"));
        Assert.Equal(2, await db.Set<GlobalRow>().CountAsync());
    }

    private static async Task SeedAsync(SqliteConnection connection)
    {
        // Offline fixture import; all assertions query through UseApproval.
        await using var db = new PartitionContext(new DbContextOptionsBuilder<PartitionContext>().UseSqlite(connection).Options, "alpha");
        await db.Database.EnsureCreatedAsync();
        foreach (var tenant in new[] { "alpha", "bravo" })
        {
            var definition = new ApprovalDefinition { Id = Guid.NewGuid(), Key = "expense", Name = "Expense", Version = 1, DefinitionJson = "{}" };
            var instance = new ApprovalInstance { Id = Guid.NewGuid(), DefinitionId = definition.Id, DefinitionKey = "expense", DefinitionVersion = 1,
                BusinessType = "Expense", BusinessKey = "EXP-001", InitiatorId = "alice", Title = tenant + "-private" };
            db.Entry(definition).Property("TenantId").CurrentValue = tenant;
            db.Entry(instance).Property("TenantId").CurrentValue = tenant;
            var link = new SharedLink { Id = Guid.NewGuid(), InstanceId = instance.Id };
            db.Entry(link).Property("TenantId").CurrentValue = tenant;
            db.AddRange(definition, instance, link);
        }
        await db.SaveChangesAsync();
    }

    public sealed class SharedLink { public Guid Id { get; set; } public Guid InstanceId { get; set; } public ApprovalInstance Instance { get; set; } = null!; }
    public sealed class Probe { public int Value { get; set; } }
    public sealed class GlobalRow { public int Id { get; set; } public bool Deleted { get; set; } }
    public sealed class GlobalContext(DbContextOptions<GlobalContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<GlobalRow>().HasQueryFilter("SoftDelete", r => !r.Deleted);
    }
    public sealed class PartitionContext(DbContextOptions<PartitionContext> options, string partition) : DbContext(options), IApprovalPartitionContext
    {
        public string PartitionKey { get; } = partition;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureApproval();
            model.Entity<SharedLink>().HasOne(l => l.Instance).WithMany().HasForeignKey(l => l.InstanceId);
            model.Entity<SharedLink>().Navigation(l => l.Instance).AutoInclude();
            foreach (var entity in model.Model.GetEntityTypes().ToArray())
            {
                model.Entity(entity.ClrType).Property<string>("TenantId").IsRequired();
                var row = Expression.Parameter(entity.ClrType, "row");
                var tenant = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], row, Expression.Constant("TenantId"));
                model.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(Expression.Equal(tenant, Expression.Property(Expression.Constant(this), nameof(PartitionKey))), row));
            }
            model.ConfigureApprovalPartition("TenantId", typeof(SharedLink));
        }
    }
}
