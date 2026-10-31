using System.Text.Json;
using Approval.Core.Actions;
using Approval.Core.Conditions;
using Approval.Core.Domain;
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

public sealed record OutboxQuery(OutboxStatus? Status = null, Guid? InstanceId = null, int Take = 100);

/// <summary>
/// Outbox 投递：抢占到期消息 → 调用渠道 / 动作 → 成功标记完成，失败按指数退避安排下次重试，超过次数标记失败。
/// 抢占时把 NextAttemptAt 推后一个租约时长并递增并发令牌：多实例只有一个能抢到；投递中崩溃则租约到期后重试。
/// 异步动作可通过注入的 DbContext 修改数据：成功时与投递结果在同一事务提交；失败时撤销（含动作自行 SaveChanges 的部分），
/// 只保存失败记录，避免重试时留下做了一半的数据。
/// </summary>
public sealed class ApprovalOutboxService(
    ApprovalDbContextAccessor accessor,
    IEnumerable<IApprovalChannel> channels,
    IEnumerable<IApprovalActionHandler> actions,
    ApprovalOptions options,
    TimeProvider clock,
    ILogger<ApprovalOutboxService> logger)
{
    private readonly DbContext _db = accessor.Context;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>处理一批到期消息，返回本批处理的条数。</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = Now;
        var ids = await _db.Set<ApprovalOutboxMessage>().AsNoTracking()
            .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Select(m => m.Id)
            .Take(Math.Max(1, options.Outbox.BatchSize))
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            await ProcessOneAsync(id, ct);
            _db.ChangeTracker.Clear();
        }
        return ids.Count;
    }

    private async Task ProcessOneAsync(Guid id, CancellationToken ct)
    {
        var message = await _db.Set<ApprovalOutboxMessage>().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || message.Status != OutboxStatus.Pending || message.NextAttemptAt > Now) return;

        // 抢占
        message.Attempts++;
        message.NextAttemptAt = Now + options.Outbox.LeaseDuration;
        message.ConcurrencyStamp++;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return; // 已被其他实例抢占
        }

        var checkpoint = ChangeTrackerCheckpoint.Create(_db);
        await using var transaction = await BeginTransactionAsync(ct);
        try
        {
            await DeliverAsync(message, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await FailAsync(ex);
            return;
        }

        message.Status = OutboxStatus.Succeeded;
        message.CompletedAt = Now;
        message.LastError = null;
        try
        {
            await SaveResultAsync(message, transaction);
        }
        catch (DbUpdateException ex)
        {
            // 动作的数据修改无法保存（如违反约束）：按投递失败处理，否则消息会一直停留在租约中
            await FailAsync(ex);
        }

        // 撤销动作在失败前的数据修改（已写入数据库的由事务回滚，仅在内存中的由检查点撤销），只保存失败记录
        async Task FailAsync(Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            checkpoint.Rollback();
            RecordFailure(message, ex);
            await SaveResultAsync(message, null);
        }
    }

    private void RecordFailure(ApprovalOutboxMessage message, Exception ex)
    {
        message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);
        if (message.Attempts >= message.MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            message.CompletedAt = Now;
            logger.LogError(ex, "Outbox 消息 {Id}（{Kind} {Target}）重试 {Attempts} 次后仍失败", message.Id, message.Kind,
                message.Target, message.Attempts);
        }
        else
        {
            message.NextAttemptAt = Now + RetryDelay(message.Attempts);
            logger.LogWarning(ex, "Outbox 消息 {Id}（{Kind} {Target}）第 {Attempts} 次投递失败，{Next:u} 重试", message.Id,
                message.Kind, message.Target, message.Attempts, message.NextAttemptAt);
        }
    }

    /// <summary>保存投递结果（成功时连同动作的数据修改一起提交事务）；并发冲突以外的保存错误抛给调用方。</summary>
    private async Task SaveResultAsync(ApprovalOutboxMessage message, IDbContextTransaction? transaction)
    {
        message.ConcurrencyStamp++;
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 投递期间被手动重试重置：以重置后的状态为准，消息会再次投递（动作的数据修改随事务回滚，重试时重新执行）
            logger.LogWarning("Outbox 消息 {Id} 投递期间被修改，投递结果未保存", message.Id);
        }
    }

    /// <summary>
    /// 为投递开启事务。已有外部事务、或执行策略会自动重试（如 SQL Server 的 EnableRetryOnFailure，不允许手动事务）时不开启，
    /// 此时仍会撤销内存中的修改，但动作自行 SaveChanges 的部分无法回滚。
    /// </summary>
    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken ct)
    {
        var database = _db.Database;
        if (!database.IsRelational() || database.CurrentTransaction is not null || database.CreateExecutionStrategy().RetriesOnFailure)
            return null;
        return await database.BeginTransactionAsync(ct);
    }

    /// <summary>第 n 次失败后等待 BaseRetryDelay × 2^(n-1)，不超过 MaxRetryDelay。</summary>
    public TimeSpan RetryDelay(int attempts)
    {
        var delay = options.Outbox.BaseRetryDelay * Math.Pow(2, Math.Min(attempts - 1, 20));
        return delay < options.Outbox.MaxRetryDelay ? delay : options.Outbox.MaxRetryDelay;
    }

    private async Task DeliverAsync(ApprovalOutboxMessage message, CancellationToken ct)
    {
        switch (message.Kind)
        {
            case OutboxKind.Notification:
            {
                var channel = channels.FirstOrDefault(c => string.Equals(c.Name, message.Target, StringComparison.OrdinalIgnoreCase))
                              ?? throw new InvalidOperationException($"通知渠道 '{message.Target}' 未注册");
                var payload = JsonSerializer.Deserialize<ChannelMessage>(message.PayloadJson, OutboxJson.Options)!;
                await channel.SendAsync(payload, ct);
                break;
            }

            case OutboxKind.Action:
            {
                var handler = actions.FirstOrDefault(a => string.Equals(a.Descriptor.Type, message.Target, StringComparison.OrdinalIgnoreCase))
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

    public Task<List<ApprovalOutboxMessage>> ListAsync(OutboxQuery query, CancellationToken ct = default)
    {
        var q = _db.Set<ApprovalOutboxMessage>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(m => m.Status == status);
        if (query.InstanceId is { } instanceId) q = q.Where(m => m.InstanceId == instanceId);
        return q.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(query.Take, 1, 500)).ToListAsync(ct);
    }

    /// <summary>手动重试：重新进入待投递状态并立即执行，重新计算重试次数。</summary>
    public async Task RetryAsync(Guid id, CancellationToken ct = default)
    {
        var message = await _db.Set<ApprovalOutboxMessage>().FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new ApprovalException("消息不存在") { Code = ApprovalErrorCode.NotFound };
        if (message.Status == OutboxStatus.Succeeded) throw new ApprovalException("消息已投递成功");

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
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
