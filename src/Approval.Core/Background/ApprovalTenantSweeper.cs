using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalTenantSweeper(IServiceScopeFactory scopes, IApprovalTenantScopeFactory tenants,
    ApprovalOptions options, TimeProvider clock, ApprovalBackgroundState state, ILogger<ApprovalTenantSweeper> logger)
{
    public async Task<int> RunAsync(string worker, Func<IServiceProvider, CancellationToken, Task<int>> operation, CancellationToken ct = default)
    {
        await using var catalogScope = scopes.CreateAsyncScope();
        var catalog = await catalogScope.ServiceProvider.GetRequiredService<IApprovalTenantCatalog>().GetTenantsAsync(ct);
        var processed = 0;
        foreach (var tenant in state.Take(worker, catalog, options.Background.MaxTenantsPerSweep))
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = new CancellationTokenSource(options.Background.TenantBudget, clock);
            using var token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var started = clock.GetTimestamp(); state.Start(tenant.TenantId, worker, clock.GetUtcNow().UtcDateTime);
            using var activity = ApprovalInstrumentation.Activities.StartActivity("approval.background");
            activity?.SetTag("approval.tenant.id", tenant.TenantId); activity?.SetTag("approval.worker", worker);
            try
            {
                await using var scope = await tenants.CreateAsync(tenant.TenantId, token.Token);
                processed += await operation(scope.ServiceProvider, token.Token).WaitAsync(token.Token);
                state.End(tenant.TenantId, worker, clock.GetUtcNow().UtcDateTime, true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                state.End(tenant.TenantId, worker, clock.GetUtcNow().UtcDateTime, false);
                logger.LogError("后台 {Worker} 在租户 {TenantId} 失败；类型 {ExceptionType}；TraceId {TraceId}", worker, tenant.TenantId, ex.GetType().Name, System.Diagnostics.Activity.Current?.TraceId);
            }
            finally { ApprovalInstrumentation.BackgroundDuration.Record(clock.GetElapsedTime(started).TotalMilliseconds); }
        }
        return processed;
    }
}
