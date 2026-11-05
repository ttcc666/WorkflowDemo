using System.Collections.Concurrent;
using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalBackgroundRunner(IApprovalScopeFactory scopes, ApprovalBackgroundState state,
    TimeProvider clock, ILogger<ApprovalBackgroundRunner> logger)
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    public async Task<int> RunAsync(string worker, Func<IServiceProvider, CancellationToken, Task<int>> operation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var started = clock.GetTimestamp();
        if (!_running.TryAdd(worker, 0)) return 0;
        try
        {
            state.Start(worker, clock.GetUtcNow().UtcDateTime);
            await using var scope = await scopes.CreateAsync(ct);
            var result = await operation(scope.ServiceProvider, ct);
            state.End(worker, clock.GetUtcNow().UtcDateTime, true); return result;
        }
        catch (Exception ex)
        {
            state.End(worker, clock.GetUtcNow().UtcDateTime, false);
            if (ct.IsCancellationRequested) throw;
            logger.LogError(ex, "审批后台 {Worker} 失败", worker); return 0;
        }
        finally
        {
            _running.TryRemove(worker, out _);
            ApprovalInstrumentation.BackgroundDuration.Record(clock.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
