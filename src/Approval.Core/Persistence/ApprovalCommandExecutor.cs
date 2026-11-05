using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Domain;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Approval.Core.Persistence;

internal sealed class ApprovalCommandExecutor<TContext>(IServiceScopeFactory scopes,
    IApprovalActorContext actor, ApprovalOptions options, TimeProvider clock, TContext callerContext) : IApprovalCommandExecutor where TContext : DbContext
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<T> ExecuteAsync<T>(ApprovalCommand command, Func<IServiceProvider, CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ApprovalUnitOfWork.RejectExtensionReentry(callerContext);
        if (string.IsNullOrWhiteSpace(command.Kind) || command.Kind.Length > 100 || command.Scope.Length > 2000)
            throw new ArgumentException("命令类型/范围无效", nameof(command));
        var key = command.OperationId;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200 || key.Any(char.IsControl))
            throw new ApprovalException("提交标识必须为 1–200 个非控制字符") { Code = ApprovalErrorCode.OperationIdRequired };
        var identity = new ApprovalExecutionIdentity(actor.ActorId, actor.IsSystem);
        using var observation = Approval.Core.Diagnostics.ApprovalInstrumentation.Command(command.Kind, key);
        var attempts = 0;
        if (identity.ActorId!.Length > 256) throw new InvalidOperationException("操作人标识超过 256 字符");
        var grants = actor is IApprovalSystemActorContext system ? system.AllowedPermissions.ToArray() : [];
        var hash = ApprovalCommandHash.Payload(command.Payload);
        var lookup = ApprovalCommandHash.Of(identity.ActorId, command.Kind, command.Scope, key);
        var seed = ApprovalCommandHash.Of(lookup);
        var definitions = new Dictionary<string, Guid>(StringComparer.Ordinal);

        AsyncServiceScope CreateScope()
        {
            var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(identity, grants);
            return scope;
        }
        async Task<ApprovalOperation?> FindAsync(CancellationToken token)
        {
            await using var scope = CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TContext>().Set<ApprovalOperation>().AsNoTracking().SingleOrDefaultAsync(o => o.LookupHash == lookup, token);
        }
        async Task<T> ReplayAsync(ApprovalOperation previous, CancellationToken token)
        {
            if (previous.ActorId != identity.ActorId || previous.Kind != command.Kind || previous.Scope != command.Scope || previous.RequestKey != key
                || previous.RequestHash != hash || previous.HashVersion != 1 || previous.ResultVersion != 2)
                throw new ApprovalException("同一提交标识不能用于不同内容或不支持的结果版本") { Code = ApprovalErrorCode.Conflict };
            await using var scope = CreateScope(); var sp = scope.ServiceProvider;
            var auth = sp.GetRequiredService<ApprovalAuthorizationService>();
            var stored = JsonSerializer.Deserialize<StoredResult<T>>(previous.ResultJson, Json)!;
            if (previous.InstanceId is { } id && !identity.IsSystem)
            {
                await auth.RequireAsync(ApprovalPermissions.InstanceRead, new() { InstanceId = id }, identity.ActorId, token);
                var db = sp.GetRequiredService<TContext>();
                var instance = await db.Set<ApprovalInstance>().AsNoTracking().SingleAsync(i => i.Id == id, token);
                if (!(await sp.GetRequiredService<IApprovalAccessPolicy>().GetAccessAsync(new(identity.ActorId, false), ApprovalContextSnapshots.Instance(instance), token)).CanView)
                    throw new ApprovalException("无权读取原操作结果") { Code = ApprovalErrorCode.NotFound };
            }
            else
            {
                // An unbound operation can still reference a live instance (e.g. Outbox Retry).
                // Reapply its current QueryScope; recorded ACLs alone cannot authorize that resource.
                var resource = stored.Resource.InstanceId is not null
                    ? await auth.Inner.ResolveResourceAsync(stored.Resource, token) : stored.Resource;
                await auth.Inner.RequireRecordedAsync(command.Permission, resource, token);
            }
            return stored.Value;
        }
        await using var strategyScope = CreateScope();
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<TContext>();
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                if (++attempts > 1) Approval.Core.Diagnostics.ApprovalInstrumentation.Retries.Add(1);
                ct.ThrowIfCancellationRequested();
                if (await FindAsync(ct) is { } previous) return await ReplayAsync(previous, ct);
                await using var scope = CreateScope(); var sp = scope.ServiceProvider;

                await sp.GetRequiredService<ApprovalAuthorizationService>().RequireAsync(command.Permission, command.Resource, identity.ActorId, ct);
                var authorizedResource = await sp.GetRequiredService<ApprovalAuthorizationService>().Inner.ResolveResourceAsync(command.Resource ?? new(), ct);
                var db = sp.GetRequiredService<TContext>(); var unit = (ApprovalUnitOfWork)sp.GetRequiredService<IApprovalUnitOfWork>();
                ApprovalPersistence.State(db).OperationSeed = seed; ApprovalPersistence.State(db).Definitions = definitions;
                if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("命令作用域必须从干净的 DbContext 开始");
                var committing = false;
                try
                {
                    return await unit.ExecuteAsync(async token =>
                    {
                        var record = new ApprovalOperation
                        {
                            Id = Guid.NewGuid(),
                            LookupHash = lookup,
                            ActorId = identity.ActorId,
                            Kind = command.Kind,
                            Scope = command.Scope,
                            RequestKey = key,
                            RequestHash = hash
                        };
                        using (unit.Writes.Enter(ApprovalWriteOwner.Operations))
                        {
                            db.Add(record);
                            // Reserve before invoking host code; concurrent requests cannot both execute a committed command.
                            await db.SaveChangesAsync(token);
                        }
                        ApprovalPersistence.State(db).KeyedCommand = true;
                        var result = await operation(sp, token);
                        unit.CheckUsable();
                        string resultJson;
                        using (unit.Extension()) resultJson = JsonSerializer.Serialize(new StoredResult<T>(result, authorizedResource), Json);
                        var committedAt = clock.GetUtcNow().UtcDateTime;
                        using (unit.Writes.Enter(ApprovalWriteOwner.Operations))
                        {
                            record.ResultJson = resultJson;
                            record.InstanceId = ApprovalPersistence.State(db).InstanceId; record.DefinitionId = ApprovalPersistence.State(db).DefinitionId;
                            record.CommittedAt = committedAt;
                        }
                        // Any failure after here is conservatively verified using a separate context.
                        committing = true;
                        return result;
                    }, ct);
                }
                catch (Exception ex) when (committing && ex is not ApprovalException)
                {
                    using var verification = new CancellationTokenSource(options.CommitVerificationTimeout);
                    try { if (await FindAsync(verification.Token) is { } committed) return await ReplayAsync(committed, verification.Token); }
                    catch (ApprovalException) { throw; }
                    catch (Exception verificationFailure)
                    { throw new ApprovalException("提交结果待确认，请保留原提交标识重试", verificationFailure) { Code = ApprovalErrorCode.CommitOutcomeUnknown }; }
                    throw;
                }
            });
        }
        catch (Exception ex) when (ex is DbUpdateException update &&
            (ApprovalDatabaseErrors.OperationConflict(update, strategyDb) || ApprovalDatabaseErrors.BusinessSlotConflict(update, strategyDb))
            || ex is ApprovalException { Code: ApprovalErrorCode.Conflict })
        {
            Approval.Core.Diagnostics.ApprovalInstrumentation.Conflicts.Add(1);
            // Transaction/scope have been disposed; PostgreSQL is no longer in an aborted transaction.
            if (await FindAsync(ct) is { } winner) return await ReplayAsync(winner, ct);
            if (ex is ApprovalException { Code: ApprovalErrorCode.Conflict }) throw;
            throw new ApprovalException("业务已被其他提交占用或资源已更新，请刷新后重试", ex) { Code = ApprovalErrorCode.Conflict };
        }
    }

    private sealed record StoredResult<T>(T Value, ApprovalResource Resource);
}
