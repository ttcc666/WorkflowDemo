using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>站内信：列表、未读数、标记已读。只能读写自己的消息。</summary>
public sealed class ApprovalMessageService(ApprovalDbContextAccessor accessor, TimeProvider clock)
{
    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization = accessor.Authorization;
    private readonly DbContext _db = accessor.Context;
    private readonly ApprovalUnitOfWork _unit = accessor.UnitOfWork;
    private readonly TimeProvider _clock = ApprovalHostServices.Clock(accessor.UnitOfWork, clock);

    public async Task<List<ApprovalMessage>> ListAsync(string userId, bool unreadOnly = false, int take = 50, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.MessageRead, new(), userId, ct);
        var query = (await VisibleAsync(ct)).AsNoTracking().Where(m => m.UserId == userId);
        if (unreadOnly) query = query.Where(m => !m.IsRead);
        return await query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
    }

    public async Task<int> UnreadCountAsync(string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.MessageRead, new(), userId, ct);
        return await (await VisibleAsync(ct)).CountAsync(m => m.UserId == userId && !m.IsRead, ct);
    }

    public Task MarkReadAsync(string userId, Guid messageId, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(ApprovalWriteOwner.MessageRead, token => MarkReadAsyncCore(userId, messageId, token), ct);

    private async Task MarkReadAsyncCore(string userId, Guid messageId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.MessageWrite, new() { MessageId = messageId }, userId, ct);
        var message = await _db.Set<ApprovalMessage>().FirstOrDefaultAsync(m => m.Id == messageId && m.UserId == userId, ct)
                      ?? throw new ApprovalException("消息不存在") { Code = ApprovalErrorCode.NotFound };
        if (message.IsRead) return;
        message.IsRead = true;
        message.ReadAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(ApprovalWriteOwner.MessageRead, token => MarkAllReadAsyncCore(userId, token), ct);

    private async Task<int> MarkAllReadAsyncCore(string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.MessageWrite, new(), userId, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var visible = (await VisibleAsync(ct)).Where(m => m.UserId == userId && !m.IsRead);
        var count = 0;
        while (true)
        {
            var batch = await visible.OrderBy(m => m.Id).Take(200).ToListAsync(ct);
            if (batch.Count == 0) return count;
            foreach (var message in batch) { message.IsRead = true; message.ReadAt = now; }
            await _db.SaveChangesAsync(ct); count += batch.Count;
            foreach (var message in batch) _db.Entry(message).State = EntityState.Detached;
            if (batch.Count < 200) return count;
        }
    }

    private async Task<IQueryable<ApprovalMessage>> VisibleAsync(CancellationToken ct)
    {
        var visible = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        return _db.Set<ApprovalMessage>().Where(m => m.InstanceId == null || visible.Select(i => i.Id).Contains(m.InstanceId.Value));
    }
}
