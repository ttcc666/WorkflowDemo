using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Approval.Core.Persistence;

/// <summary>
/// ChangeTracker 检查点：记录当前跟踪的实体状态与属性值，<see cref="Rollback"/> 撤销其后的修改
/// （新增的实体取消跟踪，修改 / 删除的实体恢复为检查点时的值与状态），检查点之前的修改保留。
/// 用于动作执行失败时丢弃动作做了一半的数据修改。
/// 不恢复内存中导航集合的增删（动作不应修改审批引擎跟踪的实体图）。
/// </summary>
internal sealed class ChangeTrackerCheckpoint
{
    private sealed record Snapshot(EntityState State, PropertyValues Values, string[] ModifiedProperties);

    private readonly DbContext _db;
    private readonly Dictionary<object, Snapshot> _entries;

    private ChangeTrackerCheckpoint(DbContext db, Dictionary<object, Snapshot> entries)
    {
        _db = db;
        _entries = entries;
    }

    public static ChangeTrackerCheckpoint Create(DbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var entries = db.ChangeTracker.Entries().ToDictionary(
            e => e.Entity,
            e => new Snapshot(e.State, e.CurrentValues.Clone(),
                e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToArray()),
            ReferenceEqualityComparer.Instance);
        return new ChangeTrackerCheckpoint(db, entries);
    }

    public void Rollback()
    {
        _db.ChangeTracker.DetectChanges();

        // 检查点之后才开始跟踪的实体（新增或新加载的）
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => !_entries.ContainsKey(e.Entity)).ToList())
            entry.State = EntityState.Detached;

        // 检查点时已跟踪的实体：恢复属性值与状态（包括被取消跟踪的）。
        // 先切到 Unchanged 再写回属性值（避免状态切换重置属性值），最后恢复修改标记 / 原状态
        foreach (var (entity, snapshot) in _entries)
        {
            var entry = _db.Entry(entity);
            if (entry.State != EntityState.Unchanged) entry.State = EntityState.Unchanged;
            entry.CurrentValues.SetValues(snapshot.Values);
            if (snapshot.State is EntityState.Unchanged or EntityState.Modified)
            {
                foreach (var property in entry.Properties)
                    property.IsModified = snapshot.ModifiedProperties.Contains(property.Metadata.Name);
            }
            else
            {
                entry.State = snapshot.State;
            }
        }
    }
}
