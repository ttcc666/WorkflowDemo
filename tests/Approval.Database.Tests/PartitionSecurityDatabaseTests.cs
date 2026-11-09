using Microsoft.EntityFrameworkCore;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Theory]
    [InlineData("raw-read", false)]
    [InlineData("raw-read", true)]
    [InlineData("raw-write", false)]
    [InlineData("raw-write", true)]
    [InlineData("partition-update", false)]
    [InlineData("partition-update", true)]
    [InlineData("filter-delete", false)]
    [InlineData("filter-delete", true)]
    [InlineData("attached-update", false)]
    [InlineData("attached-update", true)]
    [InlineData("attached-delete", false)]
    [InlineData("attached-delete", true)]
    [InlineData("safe-bulk", false)]
    [InlineData("safe-bulk", true)]
    public async Task PartitionSecurity_ProviderRejectsAttacksAndPreservesOtherTenant(string attack, bool async)
    {
        await using var owner = fixture.Create();
        var connection = owner.Database.GetConnectionString()!;
        var offline = PartitionOptions(fixture.Provider, connection);
        var guarded = PartitionOptions(fixture.Provider, connection, guarded: true);
        await using var schema = new PartitionStorageContext(offline, "alpha");
        async Task Execute(bool create)
        {
            foreach (var command in PartitionMigration(schema, create)) await schema.Database.ExecuteSqlRawAsync(command.CommandText);
        }
        await Execute(true);
        try
        {
            var alphaId = Guid.NewGuid(); var bravoId = Guid.NewGuid();
            foreach (var tenant in new[] { "alpha", "bravo" })
            {
                await using var seed = new PartitionStorageContext(offline, tenant);
                seed.AddOwned(new PartitionCategory { Id = tenant == "alpha" ? alphaId : bravoId, Code = "same-code", Description = tenant + "-secret" });
                await seed.SaveChangesAsync();
            }
            Exception? error;
            await using (var db = new PartitionStorageContext(guarded, "alpha"))
            {
                var rows = db.Set<PartitionCategory>();
                error = await Record.ExceptionAsync(async () =>
                {
                    switch (attack)
                    {
                        case "raw-read":
                            var query = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM PartitionCategories");
                            if (async) await query.ToArrayAsync(); else query.ToArray(); break;
                        case "raw-write":
                            if (async) await db.Database.ExecuteSqlRawAsync("DELETE FROM PartitionCategories");
                            else db.Database.ExecuteSqlRaw("DELETE FROM PartitionCategories"); break;
                        case "partition-update":
                            if (async) await rows.ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<string>(r, "TenantId"), "bravo"));
                            else rows.ExecuteUpdate(s => s.SetProperty(r => EF.Property<string>(r, "TenantId"), "bravo")); break;
                        case "filter-delete":
                            if (async) await rows.IgnoreQueryFilters().ExecuteDeleteAsync();
                            else rows.IgnoreQueryFilters().ExecuteDelete(); break;
                        case "attached-update":
                        case "attached-delete":
                            // UPDATE/DELETE must match the stored partition, not just a forged original value.
                            (await rows.SingleAsync()).Description = "must-roll-back";
                            var foreign = new PartitionCategory { Id = bravoId, Code = "forged-code", Description = "stolen" };
                            db.Entry(foreign).Property("TenantId").CurrentValue = "alpha";
                            db.Attach(foreign);
                            if (attack == "attached-delete") db.Remove(foreign);
                            else db.Entry(foreign).Property(r => r.Description).IsModified = true;
                            if (async) await db.SaveChangesAsync(); else db.SaveChanges(); break;
                        case "safe-bulk":
                            var other = rows.Where(r => r.Id == bravoId);
                            Assert.Equal(0, async ? await other.ExecuteDeleteAsync() : other.ExecuteDelete());
                            Assert.Equal(0, async ? await other.ExecuteUpdateAsync(s => s.SetProperty(r => r.Description, "stolen"))
                                : other.ExecuteUpdate(s => s.SetProperty(r => r.Description, "stolen")));
                            var own = rows.Where(r => r.Id == alphaId).Select(r => new { Row = r });
                            Assert.Equal(1, async ? await own.ExecuteUpdateAsync(s => s.SetProperty(r => r.Row.Description, "changed"))
                                : own.ExecuteUpdate(s => s.SetProperty(r => r.Row.Description, "changed"))); break;
                    }
                });
            }
            foreach (var tenant in new[] { "alpha", "bravo" })
            {
                await using var verify = new PartitionStorageContext(guarded, tenant);
                var row = Assert.Single(await verify.Set<PartitionCategory>().AsNoTracking().ToArrayAsync());
                Assert.Equal(tenant == "alpha" ? alphaId : bravoId, row.Id);
                Assert.Equal(attack == "safe-bulk" && tenant == "alpha" ? "changed" : tenant + "-secret", row.Description);
                Assert.Equal("same-code", row.Code);
                Assert.Equal(0, await verify.Set<PartitionCategory>().CountAsync(r => r.Id == (tenant == "alpha" ? bravoId : alphaId)));
            }
            if (attack == "safe-bulk") Assert.Null(error);
            else if (attack.StartsWith("attached-", StringComparison.Ordinal)) Assert.IsType<DbUpdateConcurrencyException>(error);
            else Assert.IsType<InvalidOperationException>(error);
        }
        finally { await Execute(false); }
    }
}
