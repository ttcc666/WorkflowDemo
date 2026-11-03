using Approval.Core.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalMaintenanceWorker(ApprovalBackgroundRunner sweeper, ApprovalOptions options)
{
    public ApprovalMaintenanceWorker(ApprovalBackgroundRunner sweeper, ApprovalOptions options, TimeProvider clock,
        ILogger<ApprovalMaintenanceWorker> logger) : this(sweeper, options) { }
    public Task<int> RunOnceAsync(CancellationToken ct = default) => !options.Lifecycle.Enabled ? Task.FromResult(0) : sweeper.RunAsync("maintenance", async (sp, token) =>
    {
        var service = sp.GetRequiredService<ApprovalLifecycleService>();
        var count = await service.ProcessPendingAsync(token);
        await service.DrainFileDeletesAsync(token); return count;
    }, ct);
}
