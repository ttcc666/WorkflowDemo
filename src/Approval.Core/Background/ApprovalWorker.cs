using Approval.Core.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

/// <summary>宿主中唯一的审批后台循环。各批次串行执行，作用域由具体服务创建。</summary>
public sealed class ApprovalWorker(
    ApprovalOutboxDispatcher outbox,
    ApprovalTimeoutScanner timeouts,
    ApprovalOutboxSignal signal,
    ApprovalOptions options,
    TimeProvider clock,
    ILogger<ApprovalWorker> logger) : BackgroundService
{
    private DateTimeOffset _nextTimeout = DateTimeOffset.MinValue;

    /// <summary>执行一轮；超时按间隔执行，Outbox 每轮执行一批。</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (clock.GetUtcNow() >= _nextTimeout)
        {
            await RunAsync("超时扫描", async token => { await timeouts.ScanOnceAsync(token); }, ct);
            _nextTimeout = clock.GetUtcNow() + options.TimeoutScanInterval;
        }

        if (options.Outbox.EnableDispatcher) await RunAsync("Outbox 投递", outbox.DispatchOnceAsync, ct);
    }

    private async Task RunAsync(string operation, Func<CancellationToken, Task> run, CancellationToken ct)
    {
        try { await run(ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "审批后台{Operation}失败，将在下轮重试", operation);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                var now = clock.GetUtcNow();
                var delay = options.Outbox.PollInterval;
                if (_nextTimeout - now < delay) delay = _nextTimeout - now;
                if (delay > TimeSpan.Zero) await signal.WaitAsync(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
