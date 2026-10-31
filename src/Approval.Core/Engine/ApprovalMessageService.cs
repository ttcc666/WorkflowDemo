using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>站内信：列表、未读数、标记已读。只能读写自己的消息。</summary>
public sealed class ApprovalMessageService(ApprovalDbContextAccessor accessor, TimeProvider clock)
{
    private readonly DbContext _db = accessor.Context;

    public Task<List<ApprovalMessage>> ListAsync(string userId, bool unreadOnly = false, int take = 50, CancellationToken ct = default)
    {
        var query = _db.Set<ApprovalMessage>().AsNoTracking().Where(m => m.UserId == userId);
        if (unreadOnly) query = query.Where(m => !m.IsRead);
        return query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
    }

    public Task<int> UnreadCountAsync(string userId, CancellationToken ct = default) =>
        _db.Set<ApprovalMessage>().CountAsync(m => m.UserId == userId && !m.IsRead, ct);

    public async Task MarkReadAsync(string userId, Guid messageId, CancellationToken ct = default)
    {
        var message = await _db.Set<ApprovalMessage>().FirstOrDefaultAsync(m => m.Id == messageId && m.UserId == userId, ct)
                      ?? throw new ApprovalException("消息不存在") { Code = ApprovalErrorCode.NotFound };
        if (message.IsRead) return;
        message.IsRead = true;
        message.ReadAt = clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return _db.Set<ApprovalMessage>()
            .Where(m => m.UserId == userId && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true).SetProperty(m => m.ReadAt, now), ct);
    }
}
