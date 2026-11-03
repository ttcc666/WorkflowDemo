using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Approval.Core.Hosting;

/// <summary>Validate the final host registrations before accepting work; never deliver a probe event.</summary>
internal sealed class ApprovalHostValidation(IServiceScopeFactory scopes, ApprovalOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken ct) => await ValidateAsync(ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    internal async Task ValidateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeBackground();
        _ = scope.ServiceProvider.GetRequiredService<IApprovalIdentityProvider>();
        if (!options.Outbox.EnableDispatcher) return;
        if (scope.ServiceProvider.GetService<IApprovalEventHandler>() is null)
            throw new InvalidOperationException("已启用 Outbox Dispatcher，宿主必须注册 IApprovalEventHandler；如明确不投递事件，请设置 Outbox.EnableDispatcher = false");
    }
}
