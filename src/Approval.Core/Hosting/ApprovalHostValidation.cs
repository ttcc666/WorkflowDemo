using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Approval.Core.Hosting;

/// <summary>Validate the final host registrations before accepting work; never deliver a probe event.</summary>
internal sealed class ApprovalHostValidation(IServiceScopeFactory scopes, ApprovalOptions options,
    IApprovalHostScopeProvider hostScopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await ValidateAsync(ct);
        foreach (var context in await hostScopes.GetBackgroundScopesAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            using var hostScope = context.Enter();
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeBackground();
            _ = scope.ServiceProvider.GetRequiredService<IApprovalIdentityProvider>();
            if (options.Outbox.EnableDispatcher && scope.ServiceProvider.GetService<IApprovalEventHandler>() is null)
                throw new InvalidOperationException("已启用 Outbox Dispatcher，宿主必须注册 IApprovalEventHandler；如明确不投递事件，请设置 Outbox.EnableDispatcher = false");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // 运行期只预检全局注册；分区内的依赖构造由 Runner 隔离，避免一个分区阻断整轮投递。
    internal Task ValidateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        var registrations = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        if (!registrations.IsService(typeof(IApprovalIdentityProvider)))
            throw new InvalidOperationException("宿主必须注册 IApprovalIdentityProvider");
        if (options.Outbox.EnableDispatcher && !registrations.IsService(typeof(IApprovalEventHandler)))
            throw new InvalidOperationException("已启用 Outbox Dispatcher，宿主必须注册 IApprovalEventHandler；如明确不投递事件，请设置 Outbox.EnableDispatcher = false");
        return Task.CompletedTask;
    }
}
