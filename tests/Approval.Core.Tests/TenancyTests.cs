using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Domain;
using Approval.Core.Files;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Approval.Core.Tests;

public sealed class TenantTestDbContext(DbContextOptions<TenantTestDbContext> options, IApprovalTenantContext tenant) : ApprovalDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder builder) => builder.ConfigureApproval(this);
}

public sealed class TenancyTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _provider = null!;
    private readonly Catalog _catalog = new();
    private readonly List<string> _deliveries = [];
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddDbContext<TenantTestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IApprovalTenantCatalog>(_catalog);
        services.AddScoped<IApprovalIdentityProvider, FakeIdentityProvider>();
        services.AddScoped<IApprovalChannel>(sp => new TenantChannel(sp.GetRequiredService<IApprovalTenantContext>(), _deliveries));
        services.AddScoped<IApprovalTenantSettingsProvider, Settings>();
        services.AddApprovalWorkflow<TenantTestDbContext>(o => { o.MultiTenant = true; o.EnableBackgroundServices = false; o.Outbox.BatchSize = 1; }).AddFormApprovals();
        _provider = services.BuildServiceProvider();
        await using var scope = await Scope("a");
        await Db(scope).Database.EnsureCreatedAsync();
    }
    public async Task DisposeAsync() { await _provider.DisposeAsync(); await _connection.DisposeAsync(); }
    private async Task<ApprovalTenantScope> Scope(string id)
    {
        var scope = await _provider.GetRequiredService<IApprovalTenantScopeFactory>().CreateAsync(id);
        scope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Authorization = Approval.Tests.Infrastructure.FlowFixtureAuthorization.Instance;
        return scope;
    }
    private static TenantTestDbContext Db(ApprovalTenantScope scope) => scope.ServiceProvider.GetRequiredService<TenantTestDbContext>();
    private static ApprovalDefinition Definition() => new() { Id = Guid.NewGuid(), Key = "shared", Version = 1, Name = "Shared", DefinitionJson = "{}", CreatedAt = DateTime.UnixEpoch };
    private static ApprovalInstance Instance(Guid definition) => new()
    {
        Id = Guid.NewGuid(), DefinitionId = definition, DefinitionKey = "shared", DefinitionVersion = 1,
        BusinessType = "Doc", BusinessKey = "same", InitiatorId = "same-user", Status = InstanceStatus.Running, CreatedAt = DateTime.UnixEpoch
    };
    private async Task<ApprovalInstance> Seed(string tenant)
    {
        await using var scope = await Scope(tenant);
        var db = Db(scope); var definition = Definition(); var instance = Instance(definition.Id);
        var draft = new ApprovalDraft { Id = Guid.NewGuid(), UserId = "same-user", DefinitionKey = "shared" };
        instance.Tasks.Add(new() { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review", AssigneeValue = "same-user", Status = ApprovalTaskStatus.Pending });
        instance.Logs.Add(new() { Id = Guid.NewGuid(), InstanceId = instance.Id, Sequence = 1, OperatorId = "same-user" });
        db.AddRange(definition, instance, draft,
            new ApprovalCarbonCopy { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "cc", AssigneeValue = "same-user" },
            new ApprovalFile { Id = Guid.NewGuid(), InstanceId = instance.Id, FileName = "test.txt", ContentType = "text/plain", Size = 1, Sha256 = "00", StorageKey = "key", UploadedBy = "same-user" },
            new ApprovalMessage { Id = Guid.NewGuid(), InstanceId = instance.Id, UserId = "same-user", Title = "private", Content = tenant },
            new ApprovalOutboxMessage { Id = Guid.NewGuid(), InstanceId = instance.Id, Target = "Test", PayloadJson = "{}", NextAttemptAt = DateTime.UnixEpoch });
        await db.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task NineEntities_ModelCacheAndSameKeysRemainTenantIsolated()
    {
        var a = await Seed("a"); var b = await Seed("b");
        await using var left = await Scope("a"); await using var right = await Scope("b");
        var x = Db(left); var y = Db(right);
        Assert.Same(x.Model, y.Model);
        foreach (var db in new[] { x, y })
        {
            Assert.Equal(1, await db.Set<ApprovalDefinition>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalCarbonCopy>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalDraft>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalFile>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalMessage>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalOutboxMessage>().CountAsync());
        }
        Assert.Equal(a.Id, (await x.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync()).Id);
        Assert.Equal(b.Id, (await y.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync()).Id);
        Assert.Null(await x.Set<ApprovalInstance>().FindAsync(b.Id));
        Assert.Null(await left.ServiceProvider.GetRequiredService<ApprovalQueryService>().GetInstanceAsync(b.Id));
        Assert.Null(await left.ServiceProvider.GetRequiredService<ApprovalFileService>().OpenAsync(
            await y.Set<ApprovalFile>().Select(f => f.Id).SingleAsync(), new ApprovalViewer("same-user", true)));
    }

    [Fact]
    public async Task BulkWrites_OnlyChangeAndDeleteCurrentTenantRows()
    {
        await Seed("a"); await Seed("b");
        await using (var a = await Scope("a"))
        {
            Assert.Equal(1, await a.ServiceProvider.GetRequiredService<ApprovalMessageService>().MarkAllReadAsync("same-user"));
            Assert.Equal(1, await Db(a).Set<ApprovalCarbonCopy>().ExecuteDeleteAsync());
        }
        await using var b = await Scope("b");
        Assert.False((await Db(b).Set<ApprovalMessage>().SingleAsync()).IsRead);
        Assert.Equal(1, await Db(b).Set<ApprovalCarbonCopy>().CountAsync());
    }

    [Fact]
    public async Task ForeignAttachAndOwnershipChange_AreRejected()
    {
        var b = await Seed("b");
        await using var a = await Scope("a");
        Assert.Throws<ApprovalException>(() => Db(a).Attach(b));
        Assert.Null(await Db(a).Set<ApprovalInstance>().FindAsync(b.Id));
        await using var clean = await Scope("b");
        var entity = await Db(clean).Set<ApprovalMessage>().SingleAsync();
        entity.TenantId = "a";
        await Assert.ThrowsAsync<ApprovalException>(() => Db(clean).SaveChangesAsync());
        await using var verify = await Scope("b");
        Assert.Equal(b.Id, (await Db(verify).Set<ApprovalInstance>().SingleAsync()).Id);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForgedCurrentTenantEntity_CannotUpdateOrDeleteForeignId(bool delete)
    {
        var b = await Seed("b");
        await using var a = await Scope("a");
        var forged = Instance(b.DefinitionId); forged.Id = b.Id; forged.TenantId = "a";
        if (delete) Db(a).Remove(forged); else Db(a).Update(forged);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Db(a).SaveChangesAsync());
        await using var verify = await Scope("b");
        Assert.Equal(InstanceStatus.Running, (await Db(verify).Set<ApprovalInstance>().SingleAsync()).Status);
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsForeignTenantDefinition()
    {
        var b = await Seed("b");
        await using var a = await Scope("a");
        Db(a).Add(Instance(b.DefinitionId));
        await Assert.ThrowsAsync<DbUpdateException>(() => Db(a).SaveChangesAsync());
    }

    [Fact]
    public async Task FilterBypass_IsRejectedAndMissingContextNeverFallsBack()
    {
        await using var scope = await Scope("a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db(scope).Set<ApprovalDefinition>().IgnoreQueryFilters().ToListAsync());
        await using var untrusted = _provider.CreateAsyncScope();
        Assert.Throws<ApprovalException>(() => untrusted.ServiceProvider.GetRequiredService<TenantTestDbContext>());
        var options = new ApprovalOptions { MultiTenant = true, FixedTenantId = "a" };
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddApprovalWorkflow<TenantTestDbContext>(o => { o.MultiTenant = true; o.FixedTenantId = options.FixedTenantId; }));
    }

    [Fact]
    public async Task BulkTenantMutationAndRawSql_AreRejectedBeforeWriting()
    {
        await Seed("a");
        await using var scope = await Scope("a");
        await Assert.ThrowsAsync<ApprovalException>(() => Db(scope).Set<ApprovalMessage>().ExecuteUpdateAsync(s => s.SetProperty(m => m.TenantId, "b")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db(scope).Database.ExecuteSqlRawAsync("DELETE FROM ApprovalMessages"));
        Assert.Equal(1, await Db(scope).Set<ApprovalMessage>().CountAsync());
        await using var other = await Scope("b"); Assert.Empty(await Db(other).Set<ApprovalMessage>().ToListAsync());
    }

    [Fact]
    public async Task HostNamedQueryFilter_ComposesWithTenantFilter()
    {
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await Scope(tenant);
            var visible = Definition(); visible.Key = "visible"; visible.IsPublished = true;
            var hidden = Definition(); hidden.Key = "hidden";
            Db(scope).AddRange(visible, hidden); await Db(scope).SaveChangesAsync();
        }
        var options = new DbContextOptionsBuilder<HostFilterContext>().UseSqlite(_connection).Options;
        await using var filtered = new HostFilterContext(options, new FixedApprovalTenantContext("a"));
        var result = Assert.Single(await filtered.Set<ApprovalDefinition>().ToListAsync());
        Assert.Equal("a", result.TenantId); Assert.Equal("visible", result.Key);
    }

    private sealed class HostFilterContext(DbContextOptions<HostFilterContext> options, IApprovalTenantContext tenant) : ApprovalDbContext(options, tenant)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ConfigureApproval(this);
            builder.Entity<ApprovalDefinition>().HasQueryFilter("Published", d => d.IsPublished);
        }
    }

    [Fact]
    public async Task BackgroundScopes_AreImmutableAndDisabledTenantIsRejected()
    {
        await using var a = await Scope("a"); await using var b = await Scope("b");
        Assert.Equal("a", a.ServiceProvider.GetRequiredService<IApprovalTenantContext>().TenantId);
        Assert.Equal("b", b.ServiceProvider.GetRequiredService<IApprovalTenantContext>().TenantId);
        Assert.True(a.ServiceProvider.GetRequiredService<IApprovalActorContext>().IsSystem);
        Assert.Throws<InvalidOperationException>(() => a.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeBackground("b"));
        await Assert.ThrowsAsync<ApprovalException>(() => Scope("disabled"));
        await Assert.ThrowsAsync<ApprovalException>(() => Scope("unknown"));
    }

    [Fact]
    public async Task BackgroundDelivery_FailureAndBacklogDoNotBlockOtherTenants()
    {
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await Scope(tenant);
            for (var n = 0; n < 2; n++) Db(scope).Add(new ApprovalOutboxMessage
            {
                Id = Guid.NewGuid(), Kind = OutboxKind.Notification, Target = "Test", PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                    new ChannelMessage(Guid.NewGuid(), default, null, null, "Title", "Body", ["same-user"], null)), NextAttemptAt = DateTime.UnixEpoch, MaxAttempts = 2
            });
            await Db(scope).SaveChangesAsync();
        }
        await _provider.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync();
        Assert.Equal(["a", "b"], _deliveries);
        await using var b = await Scope("b");
        Assert.Equal(1, await Db(b).Set<ApprovalOutboxMessage>().CountAsync(m => m.Status == OutboxStatus.Succeeded));
        Assert.Equal(1, await Db(b).Set<ApprovalOutboxMessage>().CountAsync(m => m.Status == OutboxStatus.Pending));
    }

    [Fact]
    public async Task TimeoutScanner_ProcessesEachEnabledTenantAndLeavesDisabledTenantPending()
    {
        const string flow = """
            {"startNodeId":"start","nodes":{"start":{"name":"Start","type":"Start","next":"review"},
            "review":{"name":"Review","type":"Approval","mode":"Any","timeout":{"minutes":1,"action":"AutoApprove"},
            "approvers":[{"type":"User","value":"same-user"}],"next":"end"},"end":{"name":"End","type":"End"}}}
            """;
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await Scope(tenant);
            await scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>().PublishAsync("timeout", "Timeout", flow);
            await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().StartAsync(new("timeout", "Doc", "same", "initiator"));
        }
        var options = new DbContextOptionsBuilder<TenantTestDbContext>().UseSqlite(_connection).Options;
        await using (var disabled = new TenantTestDbContext(options, new FixedApprovalTenantContext("disabled")))
        {
            var definition = Definition(); definition.DefinitionJson = flow;
            var instance = Instance(definition.Id); instance.CurrentNodeId = "review";
            instance.Tasks.Add(new() { Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "review", NodeName = "Review", AssigneeValue = "same-user", Status = ApprovalTaskStatus.Pending, NextRemindAt = _clock.GetUtcNow().UtcDateTime });
            disabled.AddRange(definition, instance); await disabled.SaveChangesAsync();
        }
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, await _provider.GetRequiredService<ApprovalTimeoutScanner>().ScanOnceAsync());
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await Scope(tenant);
            Assert.Equal(InstanceStatus.Approved, (await Db(scope).Set<ApprovalInstance>().SingleAsync()).Status);
        }
        await using var verify = new TenantTestDbContext(options, new FixedApprovalTenantContext("disabled"));
        Assert.Equal(InstanceStatus.Running, (await verify.Set<ApprovalInstance>().SingleAsync()).Status);
        Assert.Equal(ApprovalTaskStatus.Pending, (await verify.Set<ApprovalTask>().SingleAsync()).Status);
    }

    [Fact]
    public async Task TenantTemplateSettings_RenderOnlyCurrentTenantLinkAndVariables()
    {
        const string flow = """
            {"startNodeId":"start","nodes":{"start":{"name":"Start","type":"Start","next":"review"},
            "review":{"name":"Review","type":"Approval","mode":"Any","approvers":[{"type":"User","value":"same-user"}],"next":"end",
            "notifications":[{"events":["Enter"],"recipients":[{"type":"User","value":"same-user"}],"channels":["InApp"],"title":"{tenant}","content":"{link}"}]},
            "end":{"name":"End","type":"End"}}}
            """;
        foreach (var tenant in new[] { "a", "b" })
        {
            await using var scope = await Scope(tenant);
            await scope.ServiceProvider.GetRequiredService<ApprovalDefinitionService>().PublishAsync("settings", "Settings", flow);
            var instance = await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().StartAsync(new("settings", "Doc", "same", "initiator"));
            var message = await Db(scope).Set<ApprovalMessage>().SingleAsync();
            Assert.Equal(tenant, message.Title);
            Assert.Equal($"https://{tenant}.example.com/{instance.Id}", message.Content);
        }
        Assert.Empty(_provider.GetRequiredService<ApprovalOptions>().Http.AllowedHosts);
    }

    [Fact]
    public async Task LocalFiles_UseTenantStorageKeysAndRejectOtherTenantKeys()
    {
        var options = new ApprovalOptions { Files = { RootPath = Path.Combine(Path.GetTempPath(), "approval-tenants-" + Guid.NewGuid().ToString("N")) } };
        var a = new LocalApprovalFileStore(options, TimeProvider.System, new FixedApprovalTenantContext("a"));
        var b = new LocalApprovalFileStore(options, TimeProvider.System, new FixedApprovalTenantContext("b"));
        var key = await a.SaveAsync(new MemoryStream([42]), default);
        try
        {
            Assert.StartsWith("tenants/ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb/", key);
            await Assert.ThrowsAsync<ApprovalException>(() => b.OpenReadAsync(key, default));
            await Assert.ThrowsAsync<ApprovalException>(() => b.DeleteAsync(key, default));
            await using var stream = await a.OpenReadAsync(key, default);
            Assert.Equal(42, stream.ReadByte());
        }
        finally { await a.DeleteAsync(key, default); Directory.Delete(options.Files.RootPath, true); }
    }

    private sealed class Catalog : IApprovalTenantCatalog
    { public Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalTenant>>([new("a"), new("b"), new("disabled", false)]); }
    private sealed class Settings(IApprovalTenantContext tenant) : IApprovalTenantSettingsProvider
    {
        public ApprovalTenantSettings GetSettings() => new(TimeZoneInfo.Utc, $"https://{tenant.TenantId}.example.com/{{0}}", new Dictionary<string, string> { ["tenant"] = tenant.TenantId });
    }
    private sealed class TenantChannel(IApprovalTenantContext tenant, List<string> deliveries) : IApprovalChannel
    {
        public string Name => "Test";
        public string DisplayName => "Test";
        public Task SendAsync(ChannelMessage message, CancellationToken ct)
        { deliveries.Add(tenant.TenantId); return tenant.TenantId == "a" ? Task.FromException(new InvalidOperationException("Tenant a failure")) : Task.CompletedTask; }
    }
}
