using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Approval.Core.Tests;

public sealed class TenantSweepTests
{
    private sealed class Catalog : IApprovalTenantCatalog
    { public Task<IReadOnlyList<ApprovalTenant>> GetTenantsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalTenant>>([new("a"), new("b", false), new("c")]); }
    private static ServiceProvider Host(FakeTimeProvider clock, int limit)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<IApprovalTenantCatalog, Catalog>();
        services.AddApprovalWorkflow<TestDbContext>(o => { o.MultiTenant = true; o.FixedTenantId = null; o.EnableBackgroundServices = false; o.Background.MaxTenantsPerSweep = limit; o.Background.TenantBudget = TimeSpan.FromSeconds(1); });
        return services.BuildServiceProvider();
    }
    [Fact]
    public async Task Rotation_SkipsDisabledTenantAndFailureCannotStarveLaterTenant()
    {
        var clock = new FakeTimeProvider(); await using var host = Host(clock, 1); var seen = new List<string>();
        var sweeper = host.GetRequiredService<ApprovalTenantSweeper>();
        Task<int> Work(IServiceProvider sp, CancellationToken ct) { var tenant = sp.GetRequiredService<IApprovalTenantContext>().TenantId; seen.Add(tenant); return tenant == "a" ? Task.FromException<int>(new InvalidOperationException("secret-password")) : Task.FromResult(1); }
        Assert.Equal(0, await sweeper.RunAsync("test", Work)); Assert.Equal(1, await sweeper.RunAsync("test", Work));
        Assert.Equal(new[] { "a", "c" }, seen); Assert.False(host.GetRequiredService<ApprovalBackgroundState>().ForTenant("a").Single().Succeeded);
        Assert.Empty(host.GetRequiredService<ApprovalBackgroundState>().ForTenant("b"));
    }
    [Fact]
    public async Task TenantBudget_CancelsHungWorkAndContinuesWithNextTenant()
    {
        var clock = new FakeTimeProvider(); await using var host = Host(clock, 2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); var seen = new List<string>();
        var run = host.GetRequiredService<ApprovalTenantSweeper>().RunAsync("bounded", async (sp, ct) =>
        {
            var tenant = sp.GetRequiredService<IApprovalTenantContext>().TenantId; seen.Add(tenant);
            if (tenant == "a") { entered.SetResult(); return await never.Task.WaitAsync(ct); } return 1;
        });
        await entered.Task; clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await run); Assert.Equal(new[] { "a", "c" }, seen);
        var heartbeat = host.GetRequiredService<ApprovalBackgroundState>().ForTenant("a").Single(); Assert.False(heartbeat.Succeeded); Assert.NotNull(heartbeat.CompletedAt);
    }
}
