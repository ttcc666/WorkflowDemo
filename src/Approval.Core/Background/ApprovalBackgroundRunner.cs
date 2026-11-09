using System.Collections.Concurrent;
using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

public sealed class ApprovalBackgroundRunner(IApprovalScopeFactory scopes, ApprovalBackgroundState state,
    TimeProvider clock, ILogger<ApprovalBackgroundRunner> logger, IApprovalHostScopeProvider hostScopes)
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
            var result = 0;
            var succeeded = true;
            foreach (var context in await hostScopes.GetBackgroundScopesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var hostScope = context.Enter();
                    await using var scope = await scopes.CreateAsync(ct);
                    result += await operation(scope.ServiceProvider, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    succeeded = false;
                    logger.LogError(ex, "审批后台 {Worker} 在宿主分区 {Partition} 失败", worker, context.PartitionKey);
                }
            }
            state.End(worker, clock.GetUtcNow().UtcDateTime, succeeded); return result;
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
