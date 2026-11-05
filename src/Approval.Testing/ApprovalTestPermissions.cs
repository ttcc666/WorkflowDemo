using System.Collections.Concurrent;
using Approval.Core.Abstractions;

namespace Approval.Testing;

/// <summary>Explicit per-actor grants. All other requests are denied.</summary>
public sealed class ApprovalTestPermissions : IApprovalPermissionProvider
{
    private readonly ConcurrentDictionary<(string Actor, string Permission), Func<ApprovalResource, bool>> _grants = new();
    private volatile Exception? _failure;

    public Exception? Failure { get => _failure; set => _failure = value; }

    /// <summary>Sets the grant for an actor and permission, optionally restricted to matching resources.</summary>
    public void Grant(string actorId, string permission, Func<ApprovalResource, bool>? matches = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (!ApprovalPermissions.All.Contains(permission)) throw new ArgumentException("Unknown approval permission.", nameof(permission));
        _grants[(actorId, permission)] = matches ?? (_ => true);
    }

    public void Revoke(string actorId, string permission) => _grants.TryRemove((actorId, permission), out _);

    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Failure is { } failure) return Task.FromException<bool>(failure);
        return Task.FromResult(_grants.TryGetValue((request.Subject.ActorId, request.Permission), out var matches) && matches(request.Resource));
    }
}
