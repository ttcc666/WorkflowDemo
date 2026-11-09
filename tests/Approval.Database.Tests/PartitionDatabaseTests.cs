using System.Linq.Expressions;
using Approval.Core.Domain;
using Approval.Core.Abstractions;
using Approval.Core.Persistence;
using Approval.Forms;
using Approval.Forms.Attachments;
using Approval.Forms.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    // Offline storage fixture: exercise real provider constraints in fixture-owned prefixed tables.
    // Application write authorization is covered by MultiTenantHostTests, not bypassed in a host.
    private sealed class PartitionStorageContext(DbContextOptions<PartitionStorageContext> options, string partition) : DbContext(options), IApprovalPartitionContext
    {
        public string PartitionKey { get; } = partition;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureApproval("Partition").ConfigureApprovalForms("Partition");
            model.Entity<PartitionCategory>(b =>
            {
                b.ToTable("PartitionCategories"); b.HasKey(r => r.Id);
                b.Property(r => r.Code).HasMaxLength(64);
                b.Property(r => r.Description).HasMaxLength(100);
                b.HasAlternateKey(r => r.Code).HasName("AK_PartitionCategories_Code");
            });
            model.Entity<PartitionItem>(b =>
            {
                b.ToTable("PartitionItems"); b.HasKey(r => r.Id);
                b.Property(r => r.CategoryCode).HasMaxLength(64);
                b.HasOne<PartitionCategory>().WithMany().HasForeignKey(r => r.CategoryCode).HasPrincipalKey(r => r.Code);
            });
            foreach (var entity in model.Model.GetEntityTypes().ToArray())
            {
                var builder = model.Entity(entity.ClrType);
                builder.Property<string>("TenantId").HasMaxLength(64).IsRequired().IsConcurrencyToken();
                var row = Expression.Parameter(entity.ClrType, "row");
                var tenant = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], row, Expression.Constant("TenantId"));
                builder.HasQueryFilter(Expression.Lambda(Expression.Equal(tenant,
                    Expression.Property(Expression.Constant(this), nameof(PartitionKey))), row));
            }
            // Host-defined named indexes and preconfigured composite FKs must work on every provider.
            var definitions = model.Entity<ApprovalDefinition>();
            var versionIndex = definitions.Metadata.GetIndexes().Single(i => i.IsUnique);
            definitions.Metadata.RemoveIndex(versionIndex);
            definitions.HasIndex(d => new { d.Key, d.Version }, "HostDefinitionVersion")
                .HasDatabaseName("IX_PartitionDefinitions_HostVersion").IsUnique().IsDescending(false, true);
            var instance = model.Model.FindEntityType(typeof(ApprovalInstance))!;
            var principalKey = instance.AddKey([instance.FindProperty("TenantId")!, instance.FindProperty("Id")!]);
            var file = model.Model.FindEntityType(typeof(ApprovalFile))!;
            var fileInstance = file.GetForeignKeys().Single(f => f.PrincipalEntityType == instance);
            fileInstance.SetProperties([file.FindProperty("TenantId")!, file.FindProperty("InstanceId")!], principalKey);
            model.ConfigureApprovalPartition("TenantId", typeof(PartitionCategory), typeof(PartitionItem));
        }

        public void AddOwned(object value)
        {
            var entry = Entry(value);
            entry.Property("TenantId").CurrentValue = PartitionKey;
            entry.State = EntityState.Added;
        }
    }

    private sealed class PartitionCategory { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Description { get; set; } = "initial"; }
    private sealed class PartitionItem { public Guid Id { get; set; } public string CategoryCode { get; set; } = ""; }

    private static DbContextOptions<PartitionStorageContext> PartitionOptions(string provider, string connection, bool guarded = false)
    {
        var builder = new DbContextOptionsBuilder<PartitionStorageContext>();
        switch (provider)
        {
            case "MySql": builder.UseMySQL(connection); break;
            case "SqlServer": builder.UseSqlServer(connection); break;
            case "PostgreSql": builder.UseNpgsql(connection); break;
            case "Sqlite": builder.UseSqlite(connection); break;
            default: throw new InvalidOperationException("Unknown partition provider");
        }
        if (guarded) builder.UseApproval();
        return builder.Options;
    }

    private static IReadOnlyList<MigrationCommand> PartitionMigration(PartitionStorageContext db, bool create)
    {
        var model = db.GetService<IDesignTimeModel>().Model;
        var relational = model.GetRelationalModel();
        Assert.All(relational.Tables, t => Assert.StartsWith("Partition", t.Name));
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(create ? null : relational, create ? relational : null);
        return db.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
    }

    [Theory]
    [InlineData("Sqlite", "Data Source=:memory:")]
    [InlineData("MySql", "Server=localhost;Database=approval_design;User ID=root;Password=design")]
    [InlineData("SqlServer", "Server=localhost;Database=approval_design;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("PostgreSql", "Host=localhost;Database=approval_design;Username=postgres;Password=design")]
    public void PartitionModel_AllProvidersGenerateCompositeConstraintMigrations(string provider, string connection)
    {
        using var db = new PartitionStorageContext(PartitionOptions(provider, connection), "alpha");
        var model = db.GetService<IDesignTimeModel>().Model;
        var operation = model.FindEntityType(typeof(ApprovalOperation))!;
        Assert.Equal(new[] { "TenantId", "LookupHash" }, Assert.Single(operation.GetIndexes()).Properties.Select(p => p.Name));
        Assert.True(Assert.Single(operation.GetIndexes()).IsUnique);
        var version = Assert.Single(model.FindEntityType(typeof(ApprovalDefinition))!.GetIndexes(), i => i.IsUnique);
        Assert.Equal("HostDefinitionVersion", version.Name);
        Assert.Equal("IX_PartitionDefinitions_HostVersion", version.GetDatabaseName());
        Assert.Equal(new[] { "TenantId", "Key", "Version" }, version.Properties.Select(p => p.Name));
        Assert.Equal(new[] { false, false, true }, version.IsDescending);
        var fileKeys = model.FindEntityType(typeof(ApprovalFile))!.GetForeignKeys().ToArray();
        Assert.Equal(2, fileKeys.Length);
        foreach (var fk in fileKeys)
        {
            Assert.Equal("TenantId", fk.Properties[0].Name);
            Assert.Equal(new[] { "TenantId", "Id" }, fk.PrincipalKey.Properties.Select(p => p.Name));
        }
        var category = model.FindEntityType(typeof(PartitionCategory))!;
        Assert.Equal(new[] { "Id" }, category.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var alternate = Assert.Single(category.GetKeys(), k => !k.IsPrimaryKey());
        Assert.Equal(new[] { "TenantId", "Code" }, alternate.Properties.Select(p => p.Name));
        Assert.Equal("AK_PartitionCategories_Code", alternate.GetName());
        var itemKey = Assert.Single(model.FindEntityType(typeof(PartitionItem))!.GetForeignKeys());
        Assert.Equal(new[] { "TenantId", "CategoryCode" }, itemKey.Properties.Select(p => p.Name));
        Assert.Same(alternate, itemKey.PrincipalKey);
        Assert.NotEmpty(PartitionMigration(db, true));
        Assert.NotEmpty(PartitionMigration(db, false));
    }

    [Fact]
    public async Task PartitionModel_MigrationRoundTripEnforcesTenantConstraints()
    {
        await using var owner = fixture.Create();
        var options = PartitionOptions(fixture.Provider, owner.Database.GetConnectionString()!);
        await using var schema = new PartitionStorageContext(options, "alpha");
        var up = PartitionMigration(schema, true);
        var down = PartitionMigration(schema, false);
        async Task Execute(IReadOnlyList<MigrationCommand> commands)
        {
            foreach (var command in commands) await schema.Database.ExecuteSqlRawAsync(command.CommandText);
        }
        var report = Environment.GetEnvironmentVariable("APPROVAL_TEST_REPORT_DIRECTORY");
        if (report is not null)
            await File.WriteAllTextAsync(Path.Combine(report, "partition-migration.sql"), string.Join(Environment.NewLine, up.Select(c => c.CommandText)));
        await Execute(up);
        try
        {
            var definitions = new Dictionary<string, Guid>();
            var instances = new Dictionary<string, Guid>();
            var drafts = new Dictionary<string, Guid>();
            foreach (var tenant in new[] { "alpha", "bravo" })
            {
                await using var db = new PartitionStorageContext(options, tenant);
                var definition = Definition("same-definition");
                var instance = Instance(); instance.DefinitionId = definition.Id;
                var draft = new ApprovalDraft { Id = Guid.NewGuid(), UserId = "alice", DefinitionKey = definition.Key };
                definitions[tenant] = definition.Id; instances[tenant] = instance.Id; drafts[tenant] = draft.Id;
                db.AddOwned(definition); db.AddOwned(instance); db.AddOwned(draft); db.AddOwned(PartitionOperation());
                db.AddOwned(new ApprovalBusinessSlot { Id = Guid.NewGuid(), InstanceId = instance.Id, BusinessType = "Order", BusinessKey = "same-business", LookupHash = "same-business-hash" });
                db.AddOwned(PartitionFile(instance.Id, draft.Id));
                db.AddOwned(new PartitionCategory { Id = Guid.NewGuid(), Code = "shared" });
                db.AddOwned(new PartitionCategory { Id = Guid.NewGuid(), Code = tenant + "-private" });
                db.AddOwned(new PartitionItem { Id = Guid.NewGuid(), CategoryCode = "shared" });
                await db.SaveChangesAsync();
            }
            foreach (var tenant in new[] { "alpha", "bravo" })
            {
                await using var db = new PartitionStorageContext(options, tenant);
                Assert.Equal(definitions[tenant], (await db.Set<ApprovalDefinition>().SingleAsync()).Id);
                Assert.Equal(instances[tenant], (await db.Set<ApprovalInstance>().SingleAsync()).Id);
                Assert.Equal(drafts[tenant], (await db.Set<ApprovalFile>().SingleAsync()).DraftId);
                Assert.Equal("same-operation-hash", (await db.Set<ApprovalOperation>().SingleAsync()).LookupHash);
                Assert.Equal(new[] { tenant + "-private", "shared" }, await db.Set<PartitionCategory>().OrderBy(r => r.Code).Select(r => r.Code).ToArrayAsync());
                Assert.Equal("shared", (await db.Set<PartitionItem>().SingleAsync()).CategoryCode);
            }
            foreach (var duplicate in new[] { "definition", "operation", "business", "alternate-key" })
            {
                await using var db = new PartitionStorageContext(options, "alpha");
                db.AddOwned(duplicate switch
                {
                    "definition" => Definition("same-definition"),
                    "operation" => PartitionOperation(),
                    "alternate-key" => new PartitionCategory { Id = Guid.NewGuid(), Code = "shared" },
                    _ => (object)new ApprovalBusinessSlot { Id = Guid.NewGuid(), InstanceId = instances["alpha"], BusinessType = "Order", BusinessKey = "same-business", LookupHash = "same-business-hash" }
                });
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                AssertPartitionConstraint(error, unique: true);
            }
            foreach (var foreign in new[] { "instance", "draft" })
            {
                await using var db = new PartitionStorageContext(options, "alpha");
                // Match ApprovalUnitOfWork: an explicit transaction owns all flushes.
                await using var transaction = await db.Database.BeginTransactionAsync();
                var own = await db.Set<ApprovalInstance>().SingleAsync(); own.Title = "must-roll-back";
                await db.SaveChangesAsync();
                db.AddOwned(PartitionFile(instances[foreign == "instance" ? "bravo" : "alpha"], drafts[foreign == "draft" ? "bravo" : "alpha"]));
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                AssertPartitionConstraint(error, unique: false);
                await transaction.RollbackAsync();
            }
            await using (var db = new PartitionStorageContext(options, "alpha"))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var category = await db.Set<PartitionCategory>().SingleAsync(r => r.Code == "shared");
                category.Description = "must-roll-back";
                await db.SaveChangesAsync();
                db.AddOwned(new PartitionItem { Id = Guid.NewGuid(), CategoryCode = "bravo-private" });
                AssertPartitionConstraint(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()), unique: false);
                await transaction.RollbackAsync();
            }
            await using (var verify = new PartitionStorageContext(options, "alpha"))
            {
                Assert.Null((await verify.Set<ApprovalInstance>().SingleAsync()).Title);
                Assert.Equal(1, await verify.Set<ApprovalDefinition>().CountAsync());
                Assert.Equal(1, await verify.Set<ApprovalOperation>().CountAsync());
                Assert.Equal(1, await verify.Set<ApprovalBusinessSlot>().CountAsync());
                Assert.Equal(1, await verify.Set<ApprovalFile>().CountAsync());
                Assert.Equal("initial", (await verify.Set<PartitionCategory>().SingleAsync(r => r.Code == "shared")).Description);
                Assert.Equal(1, await verify.Set<PartitionItem>().CountAsync());
            }
            await Execute(down);
            await Execute(up);
            await using var fresh = new PartitionStorageContext(options, "alpha");
            Assert.Equal(0, await fresh.Set<ApprovalDefinition>().CountAsync());
            Assert.Equal(0, await fresh.Set<ApprovalFile>().CountAsync());
            Assert.Equal(0, await fresh.Set<PartitionCategory>().CountAsync());
            Assert.Equal(0, await fresh.Set<PartitionItem>().CountAsync());
        }
        finally { await Execute(down); }
    }

    private static ApprovalOperation PartitionOperation() => new()
    {
        Id = Guid.NewGuid(), LookupHash = "same-operation-hash", ActorId = "alice", Kind = "start", Scope = "order",
        RequestKey = "same-key", RequestHash = "same-request", ResultJson = "{}", CommittedAt = Timestamp
    };

    private static ApprovalFile PartitionFile(Guid instance, Guid draft) => new()
    {
        Id = Guid.NewGuid(), InstanceId = instance, DraftId = draft, UploadedBy = "alice", UploadedAt = Timestamp,
        FileName = "receipt.pdf", ContentType = "application/pdf", StorageKey = Guid.NewGuid().ToString("N"), Sha256 = new string('a', 64), Size = 12
    };

    private void AssertPartitionConstraint(DbUpdateException error, bool unique)
    {
        switch (fixture.Provider)
        {
            case "Sqlite": Assert.Equal(unique ? 2067 : 787, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.InnerException).SqliteExtendedErrorCode); break;
            case "MySql": Assert.Equal(unique ? 1062 : 1452, Assert.IsType<MySql.Data.MySqlClient.MySqlException>(error.InnerException).Number); break;
            case "PostgreSql": Assert.Equal(unique ? "23505" : "23503", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState); break;
            case "SqlServer":
                var number = Assert.IsType<Microsoft.Data.SqlClient.SqlException>(error.InnerException).Number;
                if (unique) Assert.Contains(number, new[] { 2601, 2627 }); else Assert.Equal(547, number);
                break;
            default: throw new InvalidOperationException("Unknown provider");
        }
    }
}
