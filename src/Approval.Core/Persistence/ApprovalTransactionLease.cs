using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Approval.Core.Persistence;

internal sealed class ApprovalTransactionLease(IDbContextTransaction transaction, ApprovalDbContext db, DbTransaction? native = null, bool closeConnection = false) : IAsyncDisposable
{
    public IDbContextTransaction Transaction { get; } = transaction;
    private bool _disposed;
    public static async Task<ApprovalTransactionLease> BeginAsync(ApprovalDbContext db, bool deferredRead, CancellationToken ct)
    {
        if (!deferredRead || db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            return new(db.TransactionState.Serializable
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : await db.Database.BeginTransactionAsync(ct), db);
        // SQLite normally takes a write reservation at BEGIN. A delivery waiting on remote I/O must
        // allow a new lease owner to commit. Use the driver's deferred transaction with WAL;
        // stale snapshot upgrades then fail and the entire local delivery is rolled back.
        var connection = db.Database.GetDbConnection(); var close = connection.State != ConnectionState.Open;
        if (close) await db.Database.OpenConnectionAsync(ct);
        DbTransaction? native = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var begin = connection.GetType().GetMethod("BeginTransaction", [typeof(IsolationLevel), typeof(bool)])
                ?? throw new NotSupportedException("SQLite delivery requires a driver supporting deferred transactions");
            try { native = (DbTransaction)begin.Invoke(connection, [IsolationLevel.Serializable, true])!; }
            catch (TargetInvocationException ex) when (ex.InnerException is { } inner) { ExceptionDispatchInfo.Capture(inner).Throw(); throw; }
            return new((await db.Database.UseTransactionAsync(native, ct))!, db, native, close);
        }
        catch { if (native is not null) await native.DisposeAsync(); if (close) await db.Database.CloseConnectionAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        try { await Transaction.DisposeAsync(); }
        finally { if (native is not null) await native.DisposeAsync(); if (closeConnection) await db.Database.CloseConnectionAsync(); }
    }
}
