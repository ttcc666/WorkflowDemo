using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalBackgroundRunner(IApprovalScopeFactory scopes, ApprovalBackgroundState state,
    TimeProvider clock, ILogger<ApprovalBackgroundRunner> logger)
{
    public async Task<int> RunAsync(string worker, Func<IServiceProvider, CancellationToken, Task<int>> operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); state.Start(worker, clock.GetUtcNow().UtcDateTime);
        var started = clock.GetTimestamp();
        try
        {
            await using var scope = await scopes.CreateAsync(ct);
            var result = await operation(scope.ServiceProvider, ct);
            state.End(worker, clock.GetUtcNow().UtcDateTime, true); return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            state.End(worker, clock.GetUtcNow().UtcDateTime, false);
            logger.LogError(ex, "审批后台 {Worker} 失败", worker); return 0;
        }
        finally { ApprovalInstrumentation.BackgroundDuration.Record(clock.GetElapsedTime(started).TotalMilliseconds); }
    }
}
