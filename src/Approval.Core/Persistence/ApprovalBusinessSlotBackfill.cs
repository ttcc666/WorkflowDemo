using System.Data;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Persistence;

public sealed record ApprovalBusinessSlotReport(bool Ready, int Missing, int Changed, IReadOnlyList<string> Problems);

/// <summary>升级时停写后逐企业预览/回填，不删除已有占用、不改变实例。调用者必须拥有独立维护权限。</summary>
public static class ApprovalBusinessSlotBackfill
{
    public static async Task<ApprovalBusinessSlotReport> RunAsync(ApprovalDbContext db, bool apply = false, CancellationToken ct = default)
    {
        if (db.ChangeTracker.HasChanges() || db.Database.CurrentTransaction is not null || db.Database.CreateExecutionStrategy().RetriesOnFailure)
            throw new InvalidOperationException("业务占用回填需要独立、无自动重试的维护上下文，并须先停止写入");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        const int limit = 100000;
        var instances = await db.Set<ApprovalInstance>().AsNoTracking().Where(i => i.Status == InstanceStatus.Running).Take(limit + 1).ToListAsync(ct);
        var slots = await db.Set<ApprovalBusinessSlot>().AsNoTracking().Take(limit + 1).ToListAsync(ct);
        if (instances.Count > limit || slots.Count > limit)
            return new(false, 0, 0, ["单企业占用回填上限为 100000 行，请使用分批维护流程"]);
        var slotsByHash = slots.ToDictionary(s => s.LookupHash, StringComparer.Ordinal);
        var instanceIds = instances.Select(i => i.Id).ToHashSet();
        var problems = new List<string>(); var missing = new List<ApprovalBusinessSlot>();
        foreach (var group in instances.GroupBy(i => ApprovalCommandHash.Of(i.BusinessType, i.BusinessKey)))
        {
            if (group.Count() != 1) { problems.Add($"重复进行中业务：{string.Join(',', group.Select(i => i.Id))}"); continue; }
            var instance = group.Single(); slotsByHash.TryGetValue(group.Key, out var slot);
            if (slot is null) missing.Add(new() { Id = Guid.NewGuid(), LookupHash = group.Key, BusinessType = instance.BusinessType, BusinessKey = instance.BusinessKey, InstanceId = instance.Id });
            else if (slot.InstanceId != instance.Id || slot.BusinessType != instance.BusinessType || slot.BusinessKey != instance.BusinessKey) problems.Add($"占用与实例不一致：{slot.Id}");
        }
        foreach (var slot in slots.Where(s => !instanceIds.Contains(s.InstanceId))) problems.Add($"占用指向非运行中实例：{slot.Id}");
        if (problems.Count > 0) return new(false, missing.Count, 0, problems);
        if (apply) { db.AddRange(missing); await db.SaveChangesAsync(ct); }
        await transaction.CommitAsync(ct);
        return new(true, missing.Count, apply ? missing.Count : 0, []);
    }
}
