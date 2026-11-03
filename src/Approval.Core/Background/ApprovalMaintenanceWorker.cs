using Approval.Core.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalMaintenanceWorker(ApprovalTenantSweeper sweeper, ApprovalOptions options, TimeProvider clock, ILogger<ApprovalMaintenanceWorker> logger) : BackgroundService
{
    public Task<int> RunOnceAsync(CancellationToken ct = default) => !options.Lifecycle.Enabled ? Task.FromResult(0) : sweeper.RunAsync("maintenance", async (sp, token) =>
    {
        var service = sp.GetRequiredService<ApprovalLifecycleService>();
        var count = await service.ProcessPendingAsync(token);
        await service.DrainFileDeletesAsync(token); return count;
    }, ct);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Lifecycle.PollInterval, clock);
        try { do { try { await RunOnceAsync(ct); } catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError("维护轮询失败；类型 {ExceptionType}", ex.GetType().Name); } } while (await timer.WaitForNextTickAsync(ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
