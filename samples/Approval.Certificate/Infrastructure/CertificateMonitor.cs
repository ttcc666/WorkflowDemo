using Certificate.Api.Certificates;

namespace Certificate.Api.Infrastructure;

/// <summary>后台检查运行异常；状态变化时写结构化告警，避免每轮重复刷屏。</summary>
public sealed class CertificateMonitor(IServiceScopeFactory scopes, TimeProvider clock, CertificateOptions options,
    ILogger<CertificateMonitor> logger) : BackgroundService
{
    private string? _last;

    public async Task<CertificateDiagnosticsResult> ScanOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Approval.Core.Hosting.ApprovalExecutionContext>().InitializeSystem("certificate-monitor",
            [Approval.Core.Abstractions.ApprovalPermissions.DiagnosticsRead]);
        var status = await scope.ServiceProvider.GetRequiredService<CertificateDiagnostics>().GetAsync(ct);
        var signature = $"{status.FailedDeliveries}:{status.OverdueTasks}:{string.Join(',', status.UnstaffedTasks.Select(t => t.TaskId).Order())}:{string.Join(',', status.Database.Problems)}";
        if (signature != _last && (status.FailedDeliveries > 0 || status.OverdueTasks > 0 || status.UnstaffedTasks.Count > 0 || !status.Database.Ready))
            logger.LogWarning("注册证运行告警：失败投递 {FailedDeliveries}，超时任务 {OverdueTasks}，无人处理 {UnstaffedTasks}，数据库问题 {DatabaseProblems}",
                status.FailedDeliveries, status.OverdueTasks, status.UnstaffedTasks.Select(t => new { t.TaskId, t.InstanceId, t.NodeName }), status.Database.Problems);
        _last = signature;
        return status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.MonitorInterval, clock);
        try
        {
            do
            {
                try { await ScanOnceAsync(stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "注册证运行检查失败"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
