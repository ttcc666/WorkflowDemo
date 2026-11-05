using System.Collections.Concurrent;
using Approval.Core.Abstractions;

namespace Approval.Testing;

/// <summary>Per-host user directory, role membership and active status for integration tests.</summary>
public sealed class ApprovalTestUsers : IApprovalIdentityProvider, IApprovalUserStatusProvider, IApprovalUserDirectory
{
    private readonly ConcurrentDictionary<string, ApprovalUser> _users = new(StringComparer.Ordinal);

    public void SetUser(string id, string name, IEnumerable<string>? roles = null, bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _users[id] = new(id, name, Array.AsReadOnly(roles?.Distinct(StringComparer.Ordinal).ToArray() ?? [])) { IsActive = isActive };
    }

    public void SetActive(string id, bool active) => _users.AddOrUpdate(id,
        key => throw new KeyNotFoundException($"Test user '{key}' does not exist."), (_, user) => user with { IsActive = active });

    public Task<bool> IsActiveAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_users.TryGetValue(userId, out var user) && user.IsActive);
    }

    public async Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) =>
        (await GetRolesAsync(userId, ct)).Contains(role, StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_users.TryGetValue(userId, out var user) && user.IsActive ? user.Roles : (IReadOnlyList<string>)[]);
    }

    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(_users.Values.Where(u => u.IsActive && u.Roles.Contains(role, StringComparer.Ordinal))
            .Select(u => u.Id).Order(StringComparer.Ordinal).ToArray());
    }

    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ApprovalUser>>(_users.Values.Where(u => string.IsNullOrEmpty(keyword)
            || u.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.Id, StringComparer.Ordinal).Take(take).ToArray());
    }

    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ApprovalUser>>(_users.Values.Where(u => ids.Contains(u.Id, StringComparer.Ordinal))
            .OrderBy(u => u.Id, StringComparer.Ordinal).ToArray());
    }

    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(_users.Values.Where(u => u.IsActive).SelectMany(u => u.Roles)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
