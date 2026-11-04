using Approval.Core.Domain;
using Certificate.Api.Certificates;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Certificate.Api.Tests;

public sealed class CertificateMigrationTests
{
    [Fact]
    public async Task Initial_migration_creates_current_schema_and_repeated_initialization_preserves_rows()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.DirectoryPath, "new.db");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        var database = new CertificateDatabase(db);
        await database.InitializeAsync();
        var initial = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(new[] { "20261103051646_InitialApproval" }, initial);
        var definition = new ApprovalDefinition { Id = Guid.NewGuid(), Key = "current", Name = "Current", Version = 1, DefinitionJson = "{}", Category = "分类", IsPublished = true };
        await using (var storage = new CertificateStorageContext(path))
        { storage.Add(definition); await storage.SaveChangesAsync(); }
        await database.InitializeAsync();
        var persisted = await db.Set<ApprovalDefinition>().SingleAsync(); Assert.Equal(definition.Id, persisted.Id); Assert.Equal("分类", persisted.Category);
        Assert.Equal(initial, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.True((await database.CheckAsync()).Ready); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task Initial_migration_can_roll_back_empty_schema_and_recreate_without_model_drift()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.DirectoryPath, "roundtrip.db");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await db.Database.MigrateAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(new[] { "20261103051646_InitialApproval" }, migration);
        await db.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync(); Assert.Equal(migration, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Empty(await db.Certificates.ToListAsync()); Assert.Empty(await db.CertificateRequests.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
