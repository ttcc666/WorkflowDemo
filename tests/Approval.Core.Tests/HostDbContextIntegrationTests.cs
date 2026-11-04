using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Hosting;
using Approval.Core.Persistence;
using Approval.Core.Background;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Approval.Core.Tests;

public sealed class HostDbContextIntegrationTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
        """;

    public sealed class BusinessRecord { public Guid Id { get; set; } public string Name { get; set; } = ""; public string? SavedByHost { get; set; } }
    public sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.ConfigureApproval(); model.Entity<BusinessRecord>().HasKey(r => r.Id); }
    }
    public abstract class CustomBaseContext(DbContextOptions options) : DbContext(options)
    {
        public int SaveCalls { get; private set; }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            SaveCalls++;
            foreach (var entry in ChangeTracker.Entries<BusinessRecord>().Where(e => e.State == EntityState.Added)) entry.Entity.SavedByHost = "custom-base";
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        }
    }
    public sealed class CustomContext(DbContextOptions<CustomContext> options) : CustomBaseContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.ConfigureApproval("Embedded"); model.Entity<BusinessRecord>().HasKey(r => r.Id); }
    }
    public sealed class IdentityContext(DbContextOptions<IdentityContext> options) : IdentityDbContext<IdentityUser>(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { base.OnModelCreating(model); model.ConfigureApproval(); model.Entity<BusinessRecord>().HasKey(r => r.Id); }
    }
    public sealed class ExplicitAcceptContext(DbContextOptions<ExplicitAcceptContext> options) : CustomBaseContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.ConfigureApproval("Accepted"); model.Entity<BusinessRecord>().HasKey(r => r.Id); }
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            var result = await base.SaveChangesAsync(false, ct);
            if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
            return result;
        }
    }
    private sealed class Permissions : IApprovalPermissionProvider
    {
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class HostEvents : IApprovalEventHandler
    {
        public List<ApprovalEvent> Attempts { get; } = [];
        public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
        {
            Attempts.Add(approvalEvent);
            if (Attempts.Count == 1) throw new IOException("host notification temporarily unavailable");
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("identity")]
    [InlineData("custom")]
    [InlineData("explicit-accept")]
    public Task ExistingHostContext_PreservesBusinessSaveApprovalTransactionConcurrencyAndOutbox(string structure) => structure switch
    {
        "plain" => Exercise<PlainContext>(),
        "identity" => Exercise<IdentityContext>(),
        "explicit-accept" => Exercise<ExplicitAcceptContext>(),
        _ => Exercise<CustomContext>()
    };

    private static async Task Exercise<TContext>() where TContext : DbContext
    {
        var database = $"Data Source=host-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(database); await keeper.OpenAsync();
        var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<TContext>(o => o.UseSqlite(database));
        var identity = new FakeIdentityProvider(); identity.Add("alice", "Admin"); identity.Add("bob");
        services.AddSingleton<IApprovalIdentityProvider>(identity); services.AddSingleton<IApprovalUserStatusProvider, FixtureUserStatus>();
        services.AddSingleton<IApprovalPermissionProvider, Permissions>();
        var events = new HostEvents(); services.AddSingleton<IApprovalEventHandler>(events);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)); services.AddSingleton<TimeProvider>(clock);
        services.AddApprovalWorkflow<TContext>(o => { o.EnableBackgroundServices = false; o.RequireOperationId = true; o.Outbox.BaseRetryDelay = TimeSpan.FromSeconds(1); });
        await using var provider = services.BuildServiceProvider();

        async Task<TResult> As<TResult>(string actor, Func<IServiceProvider, Task<TResult>> run)
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(new(actor), []);
            return await run(scope.ServiceProvider);
        }
        await As("alice", async sp =>
        {
            var db = sp.GetRequiredService<TContext>(); await db.Database.EnsureCreatedAsync();
            Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() is "ApprovalFiles" or "ApprovalDrafts" or "ApprovalArchives");
            if (db is IdentityContext context)
            { context.Users.Add(new IdentityUser { Id = "host-user", UserName = "existing" }); await context.SaveChangesAsync(); Assert.Equal("existing", await context.Users.Select(u => u.UserName).SingleAsync()); }
            var business = new BusinessRecord { Id = Guid.NewGuid(), Name = "ordinary host save" }; db.Add(business); await db.SaveChangesAsync();
            if (db is CustomBaseContext custom) { Assert.Equal(1, custom.SaveCalls); Assert.Equal("custom-base", business.SavedByHost); }
            return true;
        });
        await As("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("publish", "host", "publish", Flow, ApprovalPermissions.DefinitionPublish),
            (attempt, ct) => attempt.GetRequiredService<ApprovalDefinitionService>().PublishAsync("embedded", "Embedded", Flow, ct)));

        var businessId = Guid.NewGuid();
        Task<Guid> Start(string operation, bool fail = false) => As("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("business.start", "invoice", operation, new { businessId, fail }, ApprovalPermissions.InstanceStart),
            async (attempt, ct) =>
            {
                var db = attempt.GetRequiredService<TContext>(); db.Add(new BusinessRecord { Id = fail ? Guid.NewGuid() : businessId, Name = fail ? "rollback" : "invoice" });
                var instance = await attempt.GetRequiredService<ApprovalEngine>().StartAsync(new("embedded", "Invoice", operation, "alice") { FormData = new { amount = 42 } }, ct);
                if (fail) throw new InvalidOperationException("business validation failure");
                return instance.Id;
            }));
        var instanceId = await Start("start-1"); Assert.Equal(instanceId, await Start("start-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start("rollback", true));
        await As("alice", async sp =>
        {
            var db = sp.GetRequiredService<TContext>(); Assert.Equal(2, await db.Set<BusinessRecord>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(2, await db.Set<ApprovalOperation>().CountAsync());
            Assert.DoesNotContain(await db.Set<ApprovalOutboxMessage>().ToListAsync(), m => m.PayloadJson.Contains("rollback"));
            Assert.Equal("42", System.Text.Json.JsonDocument.Parse((await db.Set<ApprovalInstance>().SingleAsync()).FormDataJson).RootElement.GetProperty("amount").ToString());
            return true;
        });

        await using (var first = provider.CreateAsyncScope())
        await using (var second = provider.CreateAsyncScope())
        {
            var db1 = first.ServiceProvider.GetRequiredService<TContext>(); var db2 = second.ServiceProvider.GetRequiredService<TContext>();
            var a = await db1.Set<ApprovalInstance>().SingleAsync(); var b = await db2.Set<ApprovalInstance>().SingleAsync();
            using (new ApprovalUnitOfWork(db1).Writes.Enter(ApprovalWriteOwner.Engine))
            { a.Title = "winner"; a.ConcurrencyStamp++; await db1.SaveChangesAsync(); }
            using (new ApprovalUnitOfWork(db2).Writes.Enter(ApprovalWriteOwner.Engine))
            { b.Title = "stale overwrite"; b.ConcurrencyStamp++; await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync()); }
            var definition = await db1.Set<ApprovalDefinition>().SingleAsync(); definition.Name = "rewrite published version";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db1.SaveChangesAsync());
        }
        var task = await As("bob", async sp => Assert.Single((await sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("bob")).Items));
        await As("bob", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("task.approve", task.TaskId.ToString(), "approve-1", new { comment = "approved" }, ApprovalPermissions.TaskApprove, new() { TaskId = task.TaskId }),
            async (attempt, ct) => { await attempt.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob", "approved", ct); return true; }));
        await As("alice", async sp =>
        {
            var detail = await sp.GetRequiredService<ApprovalQueryService>().GetInstanceForViewerAsync(instanceId, new("alice", false));
            Assert.Equal(InstanceStatus.Approved, detail!.Status); Assert.Equal(ApprovalTaskStatus.Approved, Assert.Single(detail.Tasks).Status);
            Assert.Contains(detail.Logs, l => l.Action == ApprovalAction.Approve && l.OperatorId == "bob");
            Assert.Equal(0, await sp.GetRequiredService<TContext>().Set<ApprovalBusinessSlot>().CountAsync()); return true;
        });
        await provider.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync();
        var failedId = events.Attempts[0].Id; clock.Advance(TimeSpan.FromSeconds(2));
        await provider.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync();
        Assert.Equal(2, events.Attempts.Count(e => e.Id == failedId));
        Assert.Contains(events.Attempts, e => e.EventType == "Complete" && e.Status == InstanceStatus.Approved);
        await As("alice", async sp => { Assert.All(await sp.GetRequiredService<TContext>().Set<ApprovalOutboxMessage>().ToListAsync(), m => Assert.Equal(OutboxStatus.Succeeded, m.Status)); return true; });
    }

    [Fact]
    public async Task MissingPermissionProvider_RejectsEvenAnAdministratorBeforeCreatingData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<PlainContext>(o => o.UseSqlite(connection));
        var identity = new FakeIdentityProvider(); identity.Add("admin", "Admin"); services.AddSingleton<IApprovalIdentityProvider>(identity);
        services.AddApprovalWorkflow<PlainContext>(o => o.EnableBackgroundServices = false);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(new("admin"), []);
        var db = scope.ServiceProvider.GetRequiredService<PlainContext>(); await db.Database.EnsureCreatedAsync();
        var error = await Assert.ThrowsAsync<ApprovalException>(() => scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>().PublishAsync("denied", "Denied", Flow));
        Assert.Equal(ApprovalErrorCode.Forbidden, error.Code); Assert.Equal(0, await db.Set<ApprovalDefinition>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task PooledHostContext_NewLeaseDoesNotInheritFailedTransactionOrWriteAuthority()
    {
        await using var keeper = new SqliteConnection("Data Source=:memory:"); await keeper.OpenAsync();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContextPool<PlainContext>(o => o.UseSqlite(keeper), poolSize: 1);
        services.AddApprovalWorkflow<PlainContext>(o => { o.EnableBackgroundServices = false; o.Outbox.EnableDispatcher = false; });
        await using var provider = services.BuildServiceProvider();
        Guid instance; int lease;
        await using (var failed = provider.CreateAsyncScope())
        {
            failed.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("pool-test", []);
            var db = failed.ServiceProvider.GetRequiredService<PlainContext>(); await db.Database.EnsureCreatedAsync();
            instance = db.ContextId.InstanceId; lease = db.ContextId.Lease;
            await Assert.ThrowsAsync<IOException>(() => failed.ServiceProvider.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
            { db.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = "rollback" }); await Task.Yield(); throw new IOException("abort"); }));
        }
        await using var success = provider.CreateAsyncScope();
        var reused = success.ServiceProvider.GetRequiredService<PlainContext>();
        Assert.Equal(instance, reused.ContextId.InstanceId); Assert.NotEqual(lease, reused.ContextId.Lease);
        reused.Add(new BusinessRecord { Id = Guid.NewGuid(), Name = "new lease" }); await reused.SaveChangesAsync();
        Assert.Equal("new lease", await reused.Set<BusinessRecord>().Select(r => r.Name).SingleAsync());
    }
}
