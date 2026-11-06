using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Approval.Core.Definitions;
using Approval.Core.Engine.Extensions;
using Approval.Core.Engine.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Approval.Core.Engine;

/// <summary>唤醒后台投递任务：审批操作写入 Outbox 并提交后调用，避免等待轮询间隔。</summary>
internal sealed class ApprovalOutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _dispatching;

    // 同一宿主只有一个投递批次；手动触发也必须经过此入口。
    internal bool IsDispatching => Volatile.Read(ref _dispatching) != 0;
    internal bool TryBeginDispatch() => Interlocked.CompareExchange(ref _dispatching, 1, 0) == 0;
    internal void EndDispatch() => Volatile.Write(ref _dispatching, 0);

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
/// 单实例 Outbox：串行投递到期消息，成功后确认，失败按指数退避重试。没有领取事务或租约。
/// 进程退出或取消时消息仍为 Pending，重启后可立即重试；外部接收方按消息 ID 幂等处理。
/// 可靠事件处理器可通过注入的 DbContext 修改数据：成功时与投递结果在同一事务提交；失败时撤销（含处理器自行 SaveChanges 的部分），
/// 只保存失败记录，避免重试时留下做了一半的数据。
/// </summary>
public sealed class ApprovalOutboxService
{
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly ApprovalUnitOfWork _unit;
    private readonly IApprovalScopeFactory _scopes;
    private readonly ApprovalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApprovalOutboxService> _logger;
    private readonly ApprovalOutboxSignal _signal;
    private readonly ApprovalExtensionCatalog _extensions;
    private readonly ApprovalDbContextAccessor _accessor;
    private readonly Func<IApprovalAccessPolicy> _accessPolicy;
    internal IApprovalEventHandler? EventHandler { get; }

    internal ApprovalOutboxService(ApprovalDbContextAccessor accessor,
        ApprovalOptions options, TimeProvider clock, ILogger<ApprovalOutboxService> logger, IApprovalScopeFactory scopes,
        ApprovalOutboxSignal signal, IApprovalEventHandler? eventHandler, ApprovalExtensionCatalog extensions,
        Func<IApprovalAccessPolicy> accessPolicy)
    {
        _db = accessor.Context;
        _unit = accessor.UnitOfWork; _scopes = scopes;
        _authorization = accessor.Authorization;
        _options = options;
        _clock = ApprovalHostServices.Clock(_unit, clock);
        _logger = logger;
        _signal = signal;
        _extensions = extensions;
        _accessor = accessor;
        _accessPolicy = accessPolicy;
        EventHandler = eventHandler;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>处理一批到期消息，返回本批处理的条数。</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_options.Outbox.EnableDispatcher) return 0;
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.OutboxDispatch, new(), null, ct);
        if (_unit.IsActive || _db.Database.CurrentTransaction is not null || _db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("后台投递必须使用独立、干净的作用域，不能加入宿主工作单元");
        if (!_signal.TryBeginDispatch()) return 0;
        try
        {
            var now = Now;
            var messages = await _db.Set<ApprovalOutboxMessage>().AsNoTracking()
                .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
                .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.Id)
                .Select(m => new Delivery(m.Id, m.ConcurrencyStamp))
                .Take(Math.Max(1, _options.Outbox.BatchSize))
                .ToListAsync(ct);

            foreach (var message in messages)
                await ProcessOneAsync(message, ct);
            return messages.Count;
        }
        finally { _signal.EndDispatch(); }
    }

    private sealed record Delivery(Guid Id, long Stamp);
    private sealed class ActionAttempt(ApprovalOutboxMessage message)
    {
        internal Guid? InstanceId { get; } = message.InstanceId;
        internal string? NodeId { get; } = message.NodeId;
        internal ApprovalActionContext? Context { get; set; }
        internal string? HandlerType { get; set; }
        internal string FailureCode { get; set; } = "InvalidActionSnapshot";
        internal Exception? HandlerException { get; set; }
    }
    private async Task<T> RunWorkerAsync<T>(Func<ApprovalOutboxService, CancellationToken, Task<T>> operation, CancellationToken ct, bool deferredRead = false)
    {
        var scopes = _scopes;
        await using var strategyScope = await scopes.CreateAsync(ct);
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context;
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var scope = await scopes.CreateAsync(ct);
            var worker = scope.ServiceProvider.GetRequiredService<ApprovalOutboxService>();
            ApprovalPersistence.State(worker._db).DeferredRead = deferredRead;
            return await worker._unit.ExecuteOwnedAsync(ApprovalWriteOwner.Outbox, token => operation(worker, token), ct);
        });
    }
    private async Task ProcessOneAsync(Delivery delivery, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ActionAttempt? attempt = null;
        try
        {
            await RunWorkerAsync((worker, token) =>
            {
                attempt = null;
                return worker.DeliverPendingAsync(delivery, value => attempt = value, token);
            }, ct, deferredRead: true);
        }
        catch (DbUpdateConcurrencyException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Outbox 消息 {Id} 已被更新，投递结果未覆盖新状态", delivery.Id);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var error = ex;
            if (attempt is not null)
            {
                var safe = ex as WorkflowActionDeliveryException ?? new(attempt.FailureCode);
                ApprovalActionDiagnostics.Failure(_logger, attempt.InstanceId, attempt.NodeId, attempt.Context?.ActionId,
                    attempt.Context?.ExecutionId, delivery.Id, attempt.HandlerType, ActionExecutionMode.Async, safe.Code, attempt.HandlerException ?? ex);
                error = safe;
            }
            // 投递事务已回滚，包含宿主处理器自行 SaveChanges 的写入；失败记录使用新事务。
            try { await RunWorkerAsync((worker, token) => worker.FailPendingAsync(delivery, error, token), ct); }
            catch (DbUpdateConcurrencyException) { _logger.LogInformation("Outbox 消息 {Id} 已被更新，未覆盖其失败状态", delivery.Id); }
        }
    }
    private Task<ApprovalOutboxMessage?> PendingAsync(Delivery delivery, CancellationToken ct) =>
        _db.Set<ApprovalOutboxMessage>().SingleOrDefaultAsync(m => m.Id == delivery.Id && m.Status == OutboxStatus.Pending && m.ConcurrencyStamp == delivery.Stamp, ct);
    private async Task<bool> DeliverPendingAsync(Delivery delivery, Action<ActionAttempt> observe, CancellationToken ct)
    {
        var message = await PendingAsync(delivery, ct);
        if (message is null || message.NextAttemptAt > Now) return false;
        ActionAttempt? attempt = null;
        using (var extension = _unit.Extension(allowSave: true))
        {
            if (message.Target == WorkflowActionDelivery.Target)
            {
                attempt = new(message);
                observe(attempt);
                try { await DeliverActionAsync(message, attempt, ct); }
                catch (Exception ex) when (ex is not WorkflowActionDeliveryException && !ct.IsCancellationRequested)
                {
                    // Handler failures use Outbox backoff. Keep EF execution strategy retries for the storage phase.
                    attempt.HandlerException = ex;
                    throw new WorkflowActionDeliveryException(attempt.FailureCode);
                }
            }
            else
            {
                var approvalEvent = ReadEvent(message);
                if (approvalEvent.Notification?.Event == NotifyEvent.Action)
                {
                    attempt = new(message) { HandlerType = "Notification", FailureCode = "ActionExecutionFailed" };
                    observe(attempt);
                }
                await (EventHandler ?? throw new InvalidOperationException("宿主未注册 IApprovalEventHandler"))
                    .HandleAsync(approvalEvent, ct);
            }
        }
        // 等待处理器真正退出才释放进程内执行权，防止忽略取消的处理器与下一批重叠。
        ct.ThrowIfCancellationRequested();
        message.Attempts++;
        message.Status = OutboxStatus.Succeeded; message.CompletedAt = Now; message.LastError = null;
        message.ConcurrencyStamp++;
        if (attempt is not null) attempt.FailureCode = "ActionDeliveryFailed";
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outbox 消息 {Id} 第 {Attempts} 次投递成功", message.Id, message.Attempts);
        return true;
    }
    private async Task<bool> FailPendingAsync(Delivery delivery, Exception error, CancellationToken ct)
    {
        var message = await PendingAsync(delivery, ct);
        if (message is null) return false;
        message.Attempts++;
        if (message.Target == WorkflowActionDelivery.Target && error is not WorkflowActionDeliveryException)
            error = new WorkflowActionDeliveryException("ActionDeliveryFailed");
        RecordFailure(message, error); message.ConcurrencyStamp++;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private void RecordFailure(ApprovalOutboxMessage message, Exception ex)
    {
        message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);
        // The action diagnostic is emitted once with safe structural exception information.
        var detail = ex is WorkflowActionDeliveryException ? null : ex;
        if (message.Attempts >= message.MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            message.CompletedAt = Now;
            _logger.LogError(detail, "Outbox 消息 {Id}（{Target}）重试 {Attempts} 次后仍失败", message.Id,
                message.Target, message.Attempts);
        }
        else
        {
            message.NextAttemptAt = Now + RetryDelay(message.Attempts);
            _logger.LogWarning(detail, "Outbox 消息 {Id}（{Target}）第 {Attempts} 次投递失败，{Next:u} 重试", message.Id,
                message.Target, message.Attempts, message.NextAttemptAt);
        }
    }

    /// <summary>第 n 次失败后等待 BaseRetryDelay × 2^(n-1)，不超过 MaxRetryDelay。</summary>
    public TimeSpan RetryDelay(int attempts)
    {
        var delay = _options.Outbox.BaseRetryDelay * Math.Pow(2, Math.Min(attempts - 1, 20));
        return delay < _options.Outbox.MaxRetryDelay ? delay : _options.Outbox.MaxRetryDelay;
    }

    private static ApprovalEvent ReadEvent(ApprovalOutboxMessage message)
    {
        var approvalEvent = JsonSerializer.Deserialize<ApprovalEvent>(message.PayloadJson, OutboxJson.Options)
            ?? throw new InvalidOperationException("审批事件正文无效");
        if (approvalEvent.Id != message.Id || approvalEvent.InstanceId == Guid.Empty || approvalEvent.DefinitionId == Guid.Empty
            || approvalEvent.DefinitionVersion <= 0 || string.IsNullOrWhiteSpace(approvalEvent.EventType)
            || string.IsNullOrWhiteSpace(approvalEvent.BusinessType) || string.IsNullOrWhiteSpace(approvalEvent.BusinessKey)
            || string.IsNullOrWhiteSpace(approvalEvent.OperatorId) || approvalEvent.OccurredAt == default
            || !Enum.IsDefined(approvalEvent.Status)
            || message.InstanceId is { } instanceId && approvalEvent.InstanceId != instanceId
            || approvalEvent.EventType == "NotificationRequested" && (string.IsNullOrWhiteSpace(approvalEvent.DeliveryTarget)
                || approvalEvent.Notification is not { } notification || notification.Id != message.Id
                || notification.InstanceId != approvalEvent.InstanceId))
            throw new InvalidOperationException("审批事件正文缺少必要字段或与 Outbox 记录不一致");
        return approvalEvent;
    }

    private async Task DeliverActionAsync(ApprovalOutboxMessage message, ActionAttempt attempt, CancellationToken ct)
    {
        using var extension = _unit.Extension();
        var delivery = JsonSerializer.Deserialize<WorkflowActionDelivery>(message.PayloadJson, OutboxJson.Options);
        if (delivery is not { Version: 1, Context: { Mode: ActionExecutionMode.Async } context } ||
            context.InstanceId != message.InstanceId || context.NodeId != message.NodeId || context.ExecutionId != message.Id ||
            !ApprovalActionValidation.IsId(context.ActionId) || !ApprovalActionValidation.IsId(delivery.Type))
            throw new WorkflowActionDeliveryException("InvalidActionSnapshot");
        attempt.Context = context;
        attempt.HandlerType = delivery.Type;
        if (!_extensions.Actions.TryGetValue(delivery.Type, out var handler) ||
            !handler.Descriptor.SupportedModes.Contains(ActionExecutionMode.Async) ||
            handler.ValidateConfig(context.ConfigVersion, context.Mode, delivery.Config).Count != 0)
            throw new WorkflowActionDeliveryException("InvalidActionSnapshot");
        if (handler is Actions.HttpActionHandler http && http.DestinationStamp(delivery.Config) != delivery.DestinationStamp)
            throw new WorkflowActionDeliveryException("HttpDestinationChanged");
        attempt.FailureCode = "ActionExecutionFailed";
        var result = await handler.ExecuteAsync(context, delivery.Config, ct);
        if (!result.Succeeded) throw new WorkflowActionDeliveryException("ActionRejected");
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
        var result = await q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new ApprovalOutboxMessage
            {
                Id = m.Id, Target = m.Target, InstanceId = m.InstanceId, NodeId = m.NodeId,
                Description = m.Description, PayloadJson = "{}", Status = m.Status, Attempts = m.Attempts,
                MaxAttempts = m.MaxAttempts, NextAttemptAt = m.NextAttemptAt, LastError = m.LastError,
                CreatedAt = m.CreatedAt, CompletedAt = m.CompletedAt, ConcurrencyStamp = m.ConcurrencyStamp
            }).ToPagedAsync(page ?? new PageRequest(), ct);
        foreach (var message in result.Items)
        {
            message.Description = SafeDescription(message);
            message.LastError = SafeLastError(message);
        }
        return result;
    }

    /// <summary>读取实例关联投递正文。要求投递读取能力、实例敏感数据权限和完整实例可见性。</summary>
    public async Task<string> GetPayloadAsync(Guid id, CancellationToken ct = default)
    {
        var resource = new ApprovalResource { OutboxId = id };
        await _authorization.RequireAsync(ApprovalPermissions.OutboxRead, resource, ct: ct);
        var owner = await _db.Set<ApprovalOutboxMessage>().Where(m => m.Id == id)
            .Select(m => new { m.InstanceId, m.Target, m.NodeId }).SingleOrDefaultAsync(ct);
        if (owner?.InstanceId is not { } instanceId || owner.Target is not ("Host" or WorkflowActionDelivery.Target))
            throw PayloadUnavailable();

        await _authorization.RequireAsync(ApprovalPermissions.InstanceReadSensitive, resource, ct: ct);
        var visible = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        var instance = await visible.SingleOrDefaultAsync(i => i.Id == instanceId, ct) ?? throw PayloadUnavailable();
        var viewer = await _authorization.ViewerAsync(new(_accessor.ActorId, false), instanceId, ct);
        // The snapshot and rendered notifications may contain fields hidden from participants.
        using (_unit.Extension())
            if ((await _accessPolicy().GetAccessAsync(viewer, ApprovalContextSnapshots.Instance(instance), ct)).Level != ApprovalAccessLevel.Full)
                throw PayloadUnavailable();

        var payload = await _db.Set<ApprovalOutboxMessage>().Where(m => m.Id == id && m.InstanceId == instanceId && m.Target == owner.Target)
            .Select(m => m.PayloadJson).SingleOrDefaultAsync(ct) ?? throw PayloadUnavailable();
        try
        {
            if (owner.Target == WorkflowActionDelivery.Target)
            {
                var action = JsonSerializer.Deserialize<WorkflowActionDelivery>(payload, OutboxJson.Options);
                if (action is not { Version: 1, Context: { Mode: ActionExecutionMode.Async } context } || context.InstanceId != instanceId ||
                    context.ExecutionId != id || context.NodeId != owner.NodeId || context.DefinitionVersion != instance.DefinitionVersion ||
                    context.DefinitionKey != instance.DefinitionKey)
                    throw PayloadUnavailable();
            }
            else
            {
                var approvalEvent = JsonSerializer.Deserialize<ApprovalEvent>(payload, OutboxJson.Options);
                if (approvalEvent?.Id != id || approvalEvent.InstanceId != instanceId || approvalEvent.DefinitionId != instance.DefinitionId ||
                    approvalEvent.DefinitionVersion != instance.DefinitionVersion ||
                    approvalEvent.Notification is { } notification && (notification.InstanceId != instanceId || notification.Id != id))
                    throw PayloadUnavailable();
            }
        }
        catch (JsonException) { throw PayloadUnavailable(); }
        return payload;
    }

    private static ApprovalException PayloadUnavailable() => new("投递正文不存在或无权查看") { Code = ApprovalErrorCode.NotFound };

    private static string? SafeDescription(ApprovalOutboxMessage message)
    {
        if (message.Description is not { } description) return null;
        if (message.Target == WorkflowActionDelivery.Target)
        {
            var parts = description.Split('/');
            return parts.Length == 2 && parts.All(ApprovalActionValidation.IsId) ? description : WorkflowActionDelivery.Target;
        }
        if (message.Target != "Host") return "Delivery";
        return description is "NotificationRequested" or "Completed" or "TaskCreated" ||
            Enum.TryParse<ApprovalAction>(description, out var action) && Enum.IsDefined(action) && action.ToString() == description
            ? description : "ApprovalEvent";
    }

    private static string? SafeLastError(ApprovalOutboxMessage message)
    {
        if (message.LastError is not { } error) return null;
        const string prefix = nameof(WorkflowActionDeliveryException) + ": ";
        if (message.Target == WorkflowActionDelivery.Target && error.StartsWith(prefix, StringComparison.Ordinal))
        {
            var code = error[prefix.Length..];
            if (code is "ActionRejected" or "InvalidActionSnapshot" or "ActionDeliveryFailed" or "ActionExecutionFailed" or
                "HttpDestinationChanged" or "MissingHttpCredential" or "HttpRequestTooLarge" or "HttpResponseTooLarge" or "HttpAddressTooLong")
                return code;
            if (code is { Length: 16 } && code.StartsWith("HttpStatus (", StringComparison.Ordinal) && code[^1] == ')' &&
                int.TryParse(code.AsSpan(12, 3), out var status) && status is >= 100 and <= 599)
                return $"HttpStatus ({status})";
        }
        return "DeliveryFailed";
    }

    /// <summary>手动重试：重新进入待投递状态，重置次数，事务提交后唤醒后台执行器。</summary>
    public Task RetryAsync(Guid id, CancellationToken ct = default) => _unit.ExecuteCommandAsync(ApprovalWriteOwner.Outbox, token => RetryCoreAsync(id, token), ct);

    private async Task RetryCoreAsync(Guid id, CancellationToken ct)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.OutboxRetry, new() { OutboxId = id }, null, ct);
        var message = await _db.Set<ApprovalOutboxMessage>().FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new ApprovalException("消息不存在") { Code = ApprovalErrorCode.NotFound };
        if (message.Status == OutboxStatus.Succeeded) throw new ApprovalException("消息已投递成功");
        if (_signal.IsDispatching)
            throw new ApprovalException("后台正在投递，请稍后重试") { Code = ApprovalErrorCode.Conflict };

        message.Status = OutboxStatus.Pending;
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
        _unit.AfterCommit(() => { _signal.Notify(); return Task.CompletedTask; });
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
