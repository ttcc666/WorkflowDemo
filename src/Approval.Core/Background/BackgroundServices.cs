using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Files;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

/// <summary>后台投递 Outbox：被审批操作唤醒或按轮询间隔执行，每次在新的 DI 作用域中处理到期消息。</summary>
public sealed class ApprovalOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ApprovalOutboxSignal signal,
    ApprovalOptions options,
    ILogger<ApprovalOutboxDispatcher> logger) : BackgroundService
{
    /// <summary>处理所有到期消息（测试与手动触发时可直接调用）。</summary>
    public async Task DispatchOnceAsync(CancellationToken ct = default)
    {
        int processed;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            processed = await scope.ServiceProvider.GetRequiredService<ApprovalOutboxService>().ProcessDueAsync(ct);
        } while (processed >= options.Outbox.BatchSize && !ct.IsCancellationRequested);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // 数据库暂时不可用等：记录后等待下一轮，不让后台任务退出
                logger.LogError(ex, "Outbox 投递任务出错");
            }

            try
            {
                await signal.WaitAsync(options.Outbox.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>定时扫描超过时限的待办，逐条交给 <see cref="ApprovalEngine.HandleTimeoutAsync"/> 处理（每条一个作用域，互不影响）。</summary>
public sealed class ApprovalTimeoutScanner(
    IServiceScopeFactory scopeFactory,
    ApprovalOptions options,
    TimeProvider clock,
    ILogger<ApprovalTimeoutScanner> logger) : BackgroundService
{
    private const int BatchSize = 100;

    /// <summary>扫描一次，返回处理的待办数（测试与手动触发时可直接调用）。</summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct = default)
    {
        List<Guid> taskIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context;
            var now = clock.GetUtcNow().UtcDateTime;
            taskIds = await (
                    from t in db.Set<ApprovalTask>().AsNoTracking()
                    join i in db.Set<ApprovalInstance>().AsNoTracking() on t.InstanceId equals i.Id
                    where t.Status == ApprovalTaskStatus.Pending && t.NextRemindAt != null && t.NextRemindAt <= now
                          && i.Status == InstanceStatus.Running
                    orderby t.NextRemindAt
                    select t.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        // 顺带清理过期的未绑定附件（启用表单审批时）
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<ApprovalFileService>() is { } files) await files.CleanupAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "清理未绑定附件失败");
        }

        var handled = 0;
        foreach (var taskId in taskIds)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(taskId, ct)) handled++;
            }
            catch (ApprovalException ex) when (ex.Code == ApprovalErrorCode.Conflict)
            {
                // 与用户审批同时发生，以用户操作为准；下一轮会重新判断
                logger.LogInformation("超时处理与其他操作冲突，稍后重试：任务 {TaskId}", taskId);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "超时处理失败：任务 {TaskId}", taskId);
            }
        }
        return handled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.TimeoutScanInterval, clock);
        do
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "超时扫描出错");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
