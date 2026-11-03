using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Background;

/// <summary>后台投递 Outbox：被审批操作唤醒或按轮询间隔执行，每次在新的 DI 作用域中处理到期消息。</summary>
public sealed class ApprovalOutboxDispatcher(ApprovalBackgroundRunner sweeper)
{
    internal Func<CancellationToken, Task>? ValidateHost { get; init; }
    internal ApprovalOptions? Options { get; init; }
    // 保留宿主原有显式构造方式；定时循环已统一由 ApprovalWorker 拥有。
    public ApprovalOutboxDispatcher(ApprovalOutboxSignal signal, ApprovalOptions options,
        ILogger<ApprovalOutboxDispatcher> logger, ApprovalBackgroundRunner sweeper) : this(sweeper) { }
    /// <summary>处理所有到期消息（测试与手动触发时可直接调用）。</summary>
    public async Task DispatchOnceAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Options?.Outbox.EnableDispatcher == false) return;
        if (ValidateHost is not null) await ValidateHost(ct);
        await sweeper.RunAsync("outbox", (sp, token) => sp.GetRequiredService<ApprovalOutboxService>().ProcessDueAsync(token), ct);
    }

}

/// <summary>定时扫描超过时限的待办，逐条交给 <see cref="ApprovalEngine.HandleTimeoutAsync"/> 处理（每条一个作用域，互不影响）。</summary>
public sealed class ApprovalTimeoutScanner(
    IApprovalScopeFactory tenants,
    TimeProvider clock,
    ILogger<ApprovalTimeoutScanner> logger,
    ApprovalBackgroundRunner sweeper)
{
    public ApprovalTimeoutScanner(IApprovalScopeFactory tenants, ApprovalOptions options, TimeProvider clock,
        ILogger<ApprovalTimeoutScanner> logger, ApprovalBackgroundRunner sweeper) : this(tenants, clock, logger, sweeper) { }
    private const int BatchSize = 100;

    /// <summary>扫描一次，返回处理的待办数（测试与手动触发时可直接调用）。</summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct = default)
    {
        return await sweeper.RunAsync("timeouts", (sp, token) => ScanHostAsync(token), ct);
    }

    private async Task<int> ScanHostAsync(CancellationToken ct)
    {
        List<Guid> taskIds;
        await using (var scope = await tenants.CreateAsync(ct))
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

        var handled = 0;
        foreach (var taskId in taskIds)
        {
            try
            {
                await using var scope = await tenants.CreateAsync(ct);
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

}
