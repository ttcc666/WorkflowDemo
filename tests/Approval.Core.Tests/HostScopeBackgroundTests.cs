using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace Approval.Core.Tests;

public sealed class HostScopeBackgroundTests
{
    [Fact]
    public async Task Runner_ContinuesAfterPartitionFailure_AndRestoresCallerContext()
    {
        var contexts = new ScopeProvider("alpha", "bravo") { Current = "request" };
        await using var services = CreateServices(contexts);
        var seen = new List<string?>();
        var handled = await services.GetRequiredService<ApprovalBackgroundRunner>().RunAsync("partition-test", (_, _) =>
        {
            seen.Add(contexts.Current);
            if (contexts.Current == "alpha") throw new InvalidOperationException("Partition unavailable");
            return Task.FromResult(2);
        });
        Assert.Equal(2, handled);
        Assert.Equal(new[] { "alpha", "bravo" }, seen);
        Assert.Equal("request", contexts.Current);
        Assert.False(Assert.Single(services.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
    }

    [Fact]
    public async Task Runner_DoesNotOverlapTheSameWorker()
    {
        var contexts = new ScopeProvider("alpha");
        await using var services = CreateServices(contexts);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = services.GetRequiredService<ApprovalBackgroundRunner>();
        var first = runner.RunAsync("partition-test", async (_, ct) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            Assert.Equal("alpha", contexts.Current);
            return 1;
        });
        await entered.Task;
        var second = await runner.RunAsync("partition-test", (_, _) => throw new InvalidOperationException("Unexpected overlap"));
        Assert.Equal(0, second);
        release.SetResult();
        Assert.Equal(1, await first);
    }

    [Theory]
    [InlineData("scope-entry")]
    [InlineData("identity-resolution")]
    [InlineData("handler-resolution")]
    public async Task Dispatcher_ValidationFailure_ContinuesOtherPartitionsAndRecoversNextRun(string failure)
    {
        var databases = new Dictionary<string, string>
        {
            ["alpha"] = $"Data Source=outbox-alpha-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            ["bravo"] = $"Data Source=outbox-bravo-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
        };
        await using var alpha = new TestHost(o => o.EnableBackgroundServices = false, connectionString: databases["alpha"]);
        await using var bravo = new TestHost(o => o.EnableBackgroundServices = false, connectionString: databases["bravo"]);
        var alphaMessage = Message(alpha.Clock.GetUtcNow().UtcDateTime);
        var bravoMessage = Message(bravo.Clock.GetUtcNow().UtcDateTime);
        await alpha.SeedStorageAsync(async db => { db.Add(alphaMessage); await db.SaveChangesAsync(); });
        await bravo.SeedStorageAsync(async db => { db.Add(bravoMessage); await db.SaveChangesAsync(); });
        var contexts = new ScopeProvider("alpha", "bravo") { Current = "request" };
        var delivered = new List<(string? Partition, Guid Id)>();
        var unavailable = true;
        void Fail(string stage, string? partition)
        {
            if (unavailable && stage == failure && partition == "alpha")
                throw new IOException("Alpha partition validation unavailable");
        }
        contexts.BeforeEnter = partition => Fail("scope-entry", partition);
        await using var services = CreateServices(contexts, registrations =>
        {
            registrations.AddDbContext<TestDbContext>((_, options) => options.UseSqlite(databases[contexts.Current!]));
            registrations.AddSingleton<TimeProvider>(alpha.Clock);
            registrations.AddScoped<IApprovalIdentityProvider>(_ =>
            {
                Fail("identity-resolution", contexts.Current);
                return new FakeIdentityProvider();
            });
            registrations.AddScoped<IApprovalEventHandler>(_ =>
            {
                Fail("handler-resolution", contexts.Current);
                return new PartitionEvents(contexts, delivered);
            });
        });
        var dispatcher = services.GetRequiredService<ApprovalOutboxDispatcher>();

        await dispatcher.DispatchOnceAsync();

        Assert.Equal(new[] { ((string?)"bravo", bravoMessage.Id) }, delivered);
        Assert.Equal("request", contexts.Current);
        var heartbeat = Assert.Single(services.GetRequiredService<ApprovalBackgroundState>().Snapshot());
        Assert.Equal("outbox", heartbeat.Worker);
        Assert.False(heartbeat.Succeeded);
        await alpha.RunAsync(async sp =>
        {
            var pending = await sp.GetRequiredService<TestDbContext>().Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync();
            Assert.Equal(alphaMessage.Id, pending.Id);
            Assert.Equal(OutboxStatus.Pending, pending.Status);
            Assert.Equal(0, pending.Attempts);
            Assert.Null(pending.CompletedAt);
            Assert.Null(pending.LastError);
        });
        await bravo.RunAsync(async sp =>
        {
            var completed = await sp.GetRequiredService<TestDbContext>().Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync();
            Assert.Equal(bravoMessage.Id, completed.Id);
            Assert.Equal(OutboxStatus.Succeeded, completed.Status);
            Assert.Equal(1, completed.Attempts);
            Assert.Equal(bravo.Clock.GetUtcNow().UtcDateTime, completed.CompletedAt);
        });

        unavailable = false;
        await dispatcher.DispatchOnceAsync();

        Assert.Equal(new[] { ((string?)"bravo", bravoMessage.Id), ((string?)"alpha", alphaMessage.Id) }, delivered);
        Assert.Equal("request", contexts.Current);
        Assert.True(Assert.Single(services.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
        await alpha.RunAsync(async sp =>
        {
            var completed = await sp.GetRequiredService<TestDbContext>().Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync();
            Assert.Equal(OutboxStatus.Succeeded, completed.Status);
            Assert.Equal(1, completed.Attempts);
        });
    }

    [Theory]
    [InlineData("identity-resolution")]
    [InlineData("handler-resolution")]
    public async Task Startup_RejectsUnconstructiblePartitionServicesAndRestoresCallerContext(string failure)
    {
        var contexts = new ScopeProvider("alpha", "bravo") { Current = "request" };
        var error = new IOException("Alpha partition validation unavailable");
        await using var services = CreateServices(contexts, registrations =>
        {
            registrations.AddScoped<IApprovalIdentityProvider>(_ => failure == "identity-resolution" && contexts.Current == "alpha"
                ? throw error : new FakeIdentityProvider());
            registrations.AddScoped<IApprovalEventHandler>(_ => failure == "handler-resolution" && contexts.Current == "alpha"
                ? throw error : new PartitionEvents(contexts, []));
        });

        var observed = await Assert.ThrowsAsync<IOException>(() => services.GetServices<IHostedService>()
            .OfType<ApprovalHostValidation>().Single().StartAsync(CancellationToken.None));

        Assert.Same(error, observed);
        Assert.Equal("request", contexts.Current);
        Assert.Empty(services.GetRequiredService<ApprovalBackgroundState>().Snapshot());
    }

    [Fact]
    public async Task Dispatcher_MissingEventHandler_RejectsBeforeEnteringPartitions()
    {
        var contexts = new ScopeProvider("alpha", "bravo") { Current = "request" };
        var entered = new List<string>();
        contexts.BeforeEnter = entered.Add;
        await using var services = CreateServices(contexts, registrations =>
            registrations.AddSingleton<IApprovalIdentityProvider>(new FakeIdentityProvider()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync());

        Assert.Contains("IApprovalEventHandler", error.Message);
        Assert.Empty(entered);
        Assert.Equal("request", contexts.Current);
        Assert.Empty(services.GetRequiredService<ApprovalBackgroundState>().Snapshot());
    }

    private static ApprovalOutboxMessage Message(DateTime now)
    {
        var id = Guid.NewGuid();
        var approvalEvent = new ApprovalEvent(id, "Start", Guid.NewGuid(), Guid.NewGuid(), 1,
            "Certificate", "partition-test", InstanceStatus.Running, null, null, null, "alice", null, now);
        return new() { Id = id, Target = "Host", PayloadJson = JsonSerializer.Serialize(approvalEvent, OutboxJson.Options),
            Status = OutboxStatus.Pending, MaxAttempts = 3, NextAttemptAt = now, CreatedAt = now };
    }

    private sealed class PartitionEvents(ScopeProvider contexts, List<(string? Partition, Guid Id)> delivered) : IApprovalEventHandler
    {
        public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
        {
            delivered.Add((contexts.Current, approvalEvent.Id));
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider CreateServices(ScopeProvider contexts, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApprovalHostScopeProvider>(contexts);
        services.AddApprovalWorkflow<TestDbContext>(options => options.EnableBackgroundServices = false);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class ScopeProvider(params string[] partitions) : IApprovalHostScopeProvider
    {
        private readonly AsyncLocal<string?> _current = new();
        public Action<string>? BeforeEnter { get; set; }
        public string? Current { get => _current.Value; set => _current.Value = value; }
        public IApprovalHostScope Capture() => new Scope(this, Current!);
        public Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IApprovalHostScope>>(partitions.Select(partition => (IApprovalHostScope)new Scope(this, partition)).ToArray());

        private sealed class Scope(ScopeProvider owner, string partition) : IApprovalHostScope
        {
            public string PartitionKey => partition;
            public IDisposable Enter()
            {
                owner.BeforeEnter?.Invoke(partition);
                var previous = owner.Current;
                owner.Current = partition;
                return new Restore(owner, previous);
            }
        }

        private sealed class Restore(ScopeProvider owner, string? previous) : IDisposable
        {
            public void Dispose() => owner.Current = previous;
        }
    }
}
