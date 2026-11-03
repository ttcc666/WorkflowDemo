using System.Text.Json;
using Approval.Core.Actions;
using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Approval.Core.Conditions;
using Approval.Core.Domain;
using Approval.Core.Engine.Extensions;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine;

/// <summary>唤醒后台投递任务：审批操作写入 Outbox 并提交后调用，避免等待轮询间隔。</summary>
public sealed class ApprovalOutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有未处理的唤醒信号
        }
    }

    /// <summary>等待唤醒或超时；返回是否被唤醒。</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

public sealed record OutboxQuery(OutboxStatus? Status = null, Guid? InstanceId = null);

/// <summary>
/// Outbox 投递：抢占到期消息 → 调用渠道 / 动作 → 成功标记完成，失败按指数退避安排下次重试，超过次数标记失败。
/// 抢占时把 NextAttemptAt 推后一个租约时长并递增并发令牌：多实例只有一个能抢到；投递中崩溃则租约到期后重试。
/// 异步动作可通过注入的 DbContext 修改数据：成功时与投递结果在同一事务提交；失败时撤销（含动作自行 SaveChanges 的部分），
/// 只保存失败记录，避免重试时留下做了一半的数据。
/// </summary>
public sealed class ApprovalOutboxService
{
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly ApprovalUnitOfWork _unit;
    private readonly IApprovalTenantScopeFactory? _scopes;
    private readonly string _tenantId;
    private readonly IReadOnlyDictionary<string, IApprovalChannel> _channels;
    private readonly IReadOnlyDictionary<string, IApprovalActionHandler> _actions;
    private readonly ApprovalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApprovalOutboxService> _logger;

    public ApprovalOutboxService(ApprovalDbContextAccessor accessor, IEnumerable<IApprovalChannel> channels,
        IEnumerable<IApprovalActionHandler> actions, ApprovalOptions options, TimeProvider clock, ILogger<ApprovalOutboxService> logger, IApprovalTenantScopeFactory? scopes = null)
        : this(accessor, new ApprovalExtensionCatalog(() => [], () => [], () => actions, () => channels), options, clock, logger, scopes)
    {
    }

    internal ApprovalOutboxService(ApprovalDbContextAccessor accessor, ApprovalExtensionCatalog extensions,
        ApprovalOptions options, TimeProvider clock, ILogger<ApprovalOutboxService> logger, IApprovalTenantScopeFactory? scopes = null)
    {
        _db = accessor.Context;
        _unit = accessor.UnitOfWork; _scopes = scopes; _tenantId = ((ApprovalDbContext)_db).TenantId;
        _authorization = accessor.Authorization;
        _channels = extensions.Channels;
        _actions = extensions.Actions;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>处理一批到期消息，返回本批处理的条数。</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.OutboxDispatch, new(), null, ct);
        if (_unit.IsActive || _db.Database.CurrentTransaction is not null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("后台投递必须使用独立、干净的作用域，不能加入宿主工作单元");
        var now = Now;
        var ids = await _db.Set<ApprovalOutboxMessage>().AsNoTracking()
            .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Select(m => m.Id)
            .Take(Math.Max(1, _options.Outbox.BatchSize))
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            await ProcessOneAsync(id, ct);
        }
        return ids.Count;
    }

    private sealed record Lease(Guid Token, long Stamp);
    private async Task<T> RunWorkerAsync<T>(Func<ApprovalOutboxService, CancellationToken, Task<T>> operation, CancellationToken ct, bool deferredRead = false)
    {
        var scopes = _scopes ?? throw new InvalidOperationException("后台投递需要注册 IApprovalTenantScopeFactory");
        await using var strategyScope = await scopes.CreateAsync(_tenantId, ct);
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context;
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var scope = await scopes.CreateAsync(_tenantId, ct);
            var worker = scope.ServiceProvider.GetRequiredService<ApprovalOutboxService>();
            ((ApprovalDbContext)worker._db).TransactionState.DeferredRead = deferredRead;
            return await worker._unit.ExecuteAsync(token => operation(worker, token), ct);
        });
    }
    private async Task ProcessOneAsync(Guid id, CancellationToken ct)
    {
        Lease? lease;
        try { lease = await RunWorkerAsync((worker, token) => worker.ClaimAsync(id, token), ct); }
        catch (DbUpdateConcurrencyException) { return; }
        if (lease is null) return;
        try { await RunWorkerAsync((worker, token) => worker.DeliverOwnedAsync(id, lease, token), ct, deferredRead: true); }
        catch (DbUpdateConcurrencyException) { _logger.LogWarning("Outbox 消息 {Id} 的租约已被替换，旧投递结果已回滚", id); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Delivery transaction has been rolled back/disposed, including an async action's own SaveChanges.
            try { await RunWorkerAsync((worker, token) => worker.FailOwnedAsync(id, lease, ex, token), ct); }
            catch (DbUpdateConcurrencyException) { /* A newer lease or administrator owns the result. */ }
        }
    }
    private async Task<Lease?> ClaimAsync(Guid id, CancellationToken ct)
    {
        var message = await _db.Set<ApprovalOutboxMessage>().SingleOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || message.Status != OutboxStatus.Pending || message.NextAttemptAt > Now) return null;
        message.Attempts++; message.NextAttemptAt = Now + _options.Outbox.LeaseDuration;
        message.LeaseId = Guid.NewGuid(); message.ConcurrencyStamp++;
        await _db.SaveChangesAsync(ct);
        return new Lease(message.LeaseId.Value, message.ConcurrencyStamp);
    }
    private Task<ApprovalOutboxMessage?> OwnedAsync(Guid id, Lease lease, CancellationToken ct) =>
        _db.Set<ApprovalOutboxMessage>().SingleOrDefaultAsync(m => m.Id == id && m.Status == OutboxStatus.Pending && m.LeaseId == lease.Token && m.ConcurrencyStamp == lease.Stamp, ct);
    private async Task<bool> DeliverOwnedAsync(Guid id, Lease lease, CancellationToken ct)
    {
        var message = await OwnedAsync(id, lease, ct);
        if (message is null || message.NextAttemptAt <= Now) return false;
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct);
        execution.CancelAfter(_options.Outbox.LeaseDuration);
        using (var extension = _unit.Extension(allowSave: true))
            await DeliverAsync(message, execution.Token).WaitAsync(execution.Token);
        // A timeout/expired lease cannot acknowledge writes even if a handler ignored cancellation.
        execution.Token.ThrowIfCancellationRequested();
        if (message.NextAttemptAt <= Now) throw new DbUpdateConcurrencyException("Outbox lease expired");
        message.Status = OutboxStatus.Succeeded; message.CompletedAt = Now; message.LastError = null;
        message.LeaseId = null; message.ConcurrencyStamp++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
    private async Task<bool> FailOwnedAsync(Guid id, Lease lease, Exception error, CancellationToken ct)
    {
        var message = await OwnedAsync(id, lease, ct);
        if (message is null) return false;
        RecordFailure(message, error); message.LeaseId = null; message.ConcurrencyStamp++;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private void RecordFailure(ApprovalOutboxMessage message, Exception ex)
    {
        message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);
        if (message.Attempts >= message.MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            message.CompletedAt = Now;
            _logger.LogError(ex, "Outbox 消息 {Id}（{Kind} {Target}）重试 {Attempts} 次后仍失败", message.Id, message.Kind,
                message.Target, message.Attempts);
        }
        else
        {
            message.NextAttemptAt = Now + RetryDelay(message.Attempts);
            _logger.LogWarning(ex, "Outbox 消息 {Id}（{Kind} {Target}）第 {Attempts} 次投递失败，{Next:u} 重试", message.Id,
                message.Kind, message.Target, message.Attempts, message.NextAttemptAt);
        }
    }

    /// <summary>第 n 次失败后等待 BaseRetryDelay × 2^(n-1)，不超过 MaxRetryDelay。</summary>
    public TimeSpan RetryDelay(int attempts)
    {
        var delay = _options.Outbox.BaseRetryDelay * Math.Pow(2, Math.Min(attempts - 1, 20));
        return delay < _options.Outbox.MaxRetryDelay ? delay : _options.Outbox.MaxRetryDelay;
    }

    private async Task DeliverAsync(ApprovalOutboxMessage message, CancellationToken ct)
    {
        switch (message.Kind)
        {
            case OutboxKind.Notification:
            {
                var channel = _channels.GetValueOrDefault(message.Target)
                              ?? throw new InvalidOperationException($"通知渠道 '{message.Target}' 未注册");
                var payload = JsonSerializer.Deserialize<ChannelMessage>(message.PayloadJson, OutboxJson.Options)!;
                await channel.SendAsync(payload, ct);
                break;
            }

            case OutboxKind.Action:
            {
                // 与同步引擎和设计器一致：同名动作由后注册的实现覆盖。
                var handler = _actions.GetValueOrDefault(message.Target)
                              ?? throw new InvalidOperationException($"动作类型 '{message.Target}' 未注册");
                var payload = JsonSerializer.Deserialize<AsyncActionPayload>(message.PayloadJson, OutboxJson.Options)!;
                var instance = await _db.Set<ApprovalInstance>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == message.InstanceId, ct)
                               ?? throw new InvalidOperationException($"审批实例 {message.InstanceId} 不存在");

                await handler.ExecuteAsync(new ApprovalActionContext
                {
                    Instance = instance,
                    NodeId = payload.NodeId,
                    StepName = payload.StepName,
                    Parameters = new Dictionary<string, string>(payload.Parameters, StringComparer.OrdinalIgnoreCase),
                    FormData = FormData.Parse(instance.FormDataJson),
                    IsAsync = true,
                    IdempotencyKey = message.Id
                }, ct);
                break;
            }
        }
    }

    /// <summary>投递记录，按创建时间倒序分页。</summary>
    public async Task<PagedResult<ApprovalOutboxMessage>> ListAsync(OutboxQuery query, PageRequest? page = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.OutboxRead, new() { InstanceId = query.InstanceId }, null, ct);
        var q = _db.Set<ApprovalOutboxMessage>().AsNoTracking();
        var visible = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        q = q.Where(m => m.InstanceId == null || visible.Select(i => i.Id).Contains(m.InstanceId.Value));
        if (query.Status is { } status) q = q.Where(m => m.Status == status);
        if (query.InstanceId is { } instanceId) q = q.Where(m => m.InstanceId == instanceId);
        return await q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToPagedAsync(page ?? new PageRequest(), ct);
    }

    /// <summary>手动重试：重新进入待投递状态并立即执行，重新计算重试次数。</summary>
    public Task RetryAsync(Guid id, CancellationToken ct = default) => _unit.ExecuteCommandAsync(token => RetryCoreAsync(id, token), ct);

    private async Task RetryCoreAsync(Guid id, CancellationToken ct)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.OutboxRetry, new() { OutboxId = id }, null, ct);
        var message = await _db.Set<ApprovalOutboxMessage>().FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new ApprovalException("消息不存在") { Code = ApprovalErrorCode.NotFound };
        if (message.Status == OutboxStatus.Succeeded) throw new ApprovalException("消息已投递成功");

        message.Status = OutboxStatus.Pending;
        message.LeaseId = null;
        message.Attempts = 0;
        message.NextAttemptAt = Now;
        message.CompletedAt = null;
        message.ConcurrencyStamp++;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ApprovalException("消息正在投递，请稍后重试", ex) { Code = ApprovalErrorCode.Conflict };
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
