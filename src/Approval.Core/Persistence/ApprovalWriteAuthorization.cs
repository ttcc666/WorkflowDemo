using System.Collections.Concurrent;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Approval.Core.Persistence;

internal enum ApprovalWriteOwner { None, Engine, Definitions, MessageRead, CarbonCopyRead, Outbox, Operations, Forms }

/// <summary>Internal change provenance, bound to one context. Public transactions never grant this authority.</summary>
internal sealed class ApprovalWriteAuthorization
{
    private readonly DbContext db;
    internal ApprovalWriteAuthorization(DbContext context)
    {
        db = context;
        db.ChangeTracker.Tracked += (_, data) =>
        {
            if (!data.FromQuery || !Protected(data.Entry.Metadata.ClrType)) return;
            var snapshot = Snapshot.Of(data.Entry); _trusted[data.Entry.Entity] = snapshot;
            foreach (var frame in _frames) frame.Before.TryAdd(data.Entry.Entity, snapshot);
        };
    }
    private static readonly Type[] CoreTypes = [typeof(ApprovalDefinition), typeof(ApprovalInstance), typeof(ApprovalTask),
        typeof(ApprovalLog), typeof(ApprovalCarbonCopy), typeof(ApprovalCarbonCopyRead), typeof(ApprovalMessage),
        typeof(ApprovalOutboxMessage), typeof(ApprovalOperation), typeof(ApprovalBusinessSlot),
        typeof(ApprovalDefinitionManagement), typeof(ApprovalDefinitionAudit)];
    private static readonly ConcurrentDictionary<Type, byte> FormTypes = new();
    internal static void RegisterForms(Type draft, Type file) { FormTypes.TryAdd(draft, 0); FormTypes.TryAdd(file, 0); }
    internal static bool Protected(Type type) => CoreTypes.Any(t => t.IsAssignableFrom(type)) || FormTypes.Keys.Any(t => t.IsAssignableFrom(type));

    private sealed record Snapshot(EntityState State, PropertyValues Values, PropertyValues Originals, string[] Modified)
    {
        internal static Snapshot Of(EntityEntry entry) => new(entry.State, entry.CurrentValues.Clone(), entry.OriginalValues.Clone(),
            entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToArray());
        internal bool Matches(EntityEntry entry) => State == entry.State && Same(Values, entry.CurrentValues) && Same(Originals, entry.OriginalValues)
            && Modified.SequenceEqual(entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name));
        internal bool Accepted(EntityEntry entry) => entry.State == EntityState.Unchanged && Same(Values, entry.CurrentValues)
            && Same(Values, entry.OriginalValues) && !entry.Properties.Any(p => p.IsModified);
        private static bool Same(PropertyValues left, PropertyValues right) => left.Properties.All(p => p.GetValueComparer()!.Equals(left[p], right[p]));
    }
    private sealed record Frame(ApprovalWriteOwner Owner, Dictionary<object, Snapshot> Before, object? Prepared, string[] PreparedProperties);
    private readonly Stack<Frame> _frames = new();
    private readonly Dictionary<object, Snapshot> _trusted = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Snapshot> _savedUnaccepted = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, Snapshot> _authorized = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, Snapshot>? _saving;

    private IEnumerable<EntityEntry> Entries() => db.ChangeTracker.Entries().Where(e => Protected(e.Metadata.ClrType));
    private Dictionary<object, Snapshot> Capture() => Entries().ToDictionary(e => e.Entity, Snapshot.Of, ReferenceEqualityComparer.Instance);
    private static bool Changed(EntityEntry e) => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;
    internal IDisposable Enter(ApprovalWriteOwner owner, object? prepared = null, params string[] preparedProperties)
    {
        Seal(owner != ApprovalWriteOwner.None);
        var frame = new Frame(owner, Capture(), prepared, preparedProperties); _frames.Push(frame);
        return new ExitScope(this, frame);
    }
    private void Exit(Frame frame)
    {
        if (_frames.Count == 0 || !ReferenceEquals(_frames.Peek(), frame)) throw new InvalidOperationException("审批写入作用域必须按嵌套顺序释放");
        try { if (!ApprovalPersistence.State(db).Failed) Seal(); }
        finally { _frames.Pop(); }
    }
    private sealed class ExitScope(ApprovalWriteAuthorization writes, Frame frame) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; writes.Exit(frame); }
    }
    internal void Validate(bool requireTrusted = false)
    {
        db.ChangeTracker.DetectChanges();
        foreach (var entry in Entries())
        {
            if (!Changed(entry))
            {
                if (_savedUnaccepted.TryGetValue(entry.Entity, out var saved) && saved.Accepted(entry))
                {
                    _trusted[entry.Entity] = Snapshot.Of(entry); _authorized.Remove(entry.Entity); _savedUnaccepted.Remove(entry.Entity);
                }
                if (entry.State == EntityState.Unchanged && (_trusted.TryGetValue(entry.Entity, out var original) ? !original.Matches(entry) : requireTrusted))
                    throw new InvalidOperationException("审批服务不能使用宿主伪造或重设原始值的跟踪实体，请重新查询真实记录");
                continue;
            }
            if (_authorized.TryGetValue(entry.Entity, out var known) && known.Matches(entry)) continue;
            if (_frames.TryPeek(out var frame) && Allowed(frame, entry)) continue;
            throw new InvalidOperationException($"审批实体 {entry.Metadata.ClrType.Name} 的写入必须由拥有它的审批服务执行");
        }
    }
    private static bool Only(EntityEntry entry, Snapshot? before, params string[] properties) => before is not null
        && before.Values.Properties.Where(p => !properties.Contains(p.Name, StringComparer.Ordinal))
            .All(p => p.GetValueComparer()!.Equals(before.Values[p], entry.CurrentValues[p]));
    private static bool Allowed(Frame frame, EntityEntry e)
    {
        frame.Before.TryGetValue(e.Entity, out var before);
        if (ReferenceEquals(frame.Prepared, e.Entity) && e.State is EntityState.Added or EntityState.Modified
            && Only(e, before, frame.PreparedProperties)) return true;
        return frame.Owner switch
        {
            ApprovalWriteOwner.Engine => e.Entity switch
            {
                ApprovalInstance or ApprovalTask or ApprovalBusinessSlot => true,
                ApprovalLog or ApprovalCarbonCopy or ApprovalMessage or ApprovalOutboxMessage => e.State == EntityState.Added,
                _ => false
            },
            ApprovalWriteOwner.Definitions => e.Entity is ApprovalDefinitionManagement
                || e.Entity is ApprovalDefinition or ApprovalDefinitionAudit && e.State == EntityState.Added,
            ApprovalWriteOwner.MessageRead => e.Entity is ApprovalMessage && e.State == EntityState.Modified && Only(e, before, "IsRead", "ReadAt"),
            ApprovalWriteOwner.CarbonCopyRead => e.Entity is ApprovalCarbonCopyRead && e.State == EntityState.Added,
            ApprovalWriteOwner.Outbox => e.Entity is ApprovalOutboxMessage && e.State == EntityState.Modified
                && Only(e, before, "Status", "Attempts", "NextAttemptAt", "LastError", "CompletedAt", "ConcurrencyStamp"),
            ApprovalWriteOwner.Operations => e.Entity is ApprovalOperation && (e.State == EntityState.Added
                || e.State == EntityState.Modified && Only(e, before, "ResultJson", "InstanceId", "DefinitionId", "CommittedAt")),
            ApprovalWriteOwner.Forms => FormTypes.Keys.Any(t => t.IsInstanceOfType(e.Entity)),
            _ => false
        };
    }
    internal void Seal(bool requireTrusted = false)
    {
        Validate(requireTrusted);
        _authorized = Entries().Where(Changed).ToDictionary(e => e.Entity, Snapshot.Of, ReferenceEqualityComparer.Instance);
    }
    internal void Saving()
    {
        Seal(); _saving = Capture();
    }
    internal void RecordRevision(EntityEntry entry) => _authorized[entry.Entity] = Snapshot.Of(entry);
    internal void VerifySaving()
    {
        if (_saving is null) return;
        db.ChangeTracker.DetectChanges();
        foreach (var entry in Entries())
            if (_saving.TryGetValue(entry.Entity, out var expected) ? !expected.Matches(entry) : Changed(entry))
                throw new InvalidOperationException("审批实体在保存校验之后被修改，拒绝执行数据库命令");
    }
    internal void Saved()
    {
        foreach (var entry in Entries().Where(Changed))
            if (_saving?.TryGetValue(entry.Entity, out var written) == true && written.State is EntityState.Added or EntityState.Modified)
                _savedUnaccepted[entry.Entity] = Snapshot.Of(entry);
        foreach (var entry in Entries().Where(e => e.State == EntityState.Unchanged))
            if (_trusted.ContainsKey(entry.Entity) || _saving?.TryGetValue(entry.Entity, out var saved) == true && saved.State is EntityState.Added or EntityState.Modified)
                _trusted[entry.Entity] = Snapshot.Of(entry);
        _saving = null;
        _authorized = Entries().Where(Changed).ToDictionary(e => e.Entity, Snapshot.Of, ReferenceEqualityComparer.Instance);
        // Refresh baselines after EF accepts changes; pending outer owner permissions remain narrow.
        foreach (var frame in _frames)
        {
            frame.Before.Clear(); foreach (var pair in Capture()) frame.Before.Add(pair.Key, pair.Value);
        }
    }
    internal void Failed() { _saving = null; _authorized.Clear(); _savedUnaccepted.Clear(); }
    internal void ResetLease()
    {
        if (_frames.Count != 0) throw new InvalidOperationException("不能在审批写入尚未结束时重用池化 DbContext");
        Failed(); _trusted.Clear();
    }
}
