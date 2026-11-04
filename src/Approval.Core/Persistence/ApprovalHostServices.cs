using Approval.Core.Abstractions;

namespace Approval.Core.Persistence;

/// <summary>Host providers execute without inheriting the calling module's write permissions.</summary>
internal static class ApprovalHostServices
{
    private static async Task<T> Call<T>(ApprovalUnitOfWork unit, Func<Task<T>> call)
    {
        using var boundary = unit.Extension(); var value = await call();
        return value switch
        {
            IReadOnlyList<string> ids => (T)(object)ids.ToArray(),
            IReadOnlyList<ApprovalUser> users => (T)(object)users.Select(u => u with { Roles = u.Roles.ToArray() }).ToArray(),
            _ => value
        };
    }

    internal static IApprovalIdentityProvider Identity(ApprovalUnitOfWork unit, IApprovalIdentityProvider provider) => new IdentityProvider(unit, provider);

    internal static IApprovalUserDirectory? Directory(ApprovalUnitOfWork unit, IApprovalUserDirectory? provider) => provider is null ? null : new UserDirectory(unit, provider);

    internal static IApprovalUserStatusProvider? Status(ApprovalUnitOfWork unit, IApprovalUserStatusProvider? provider) => provider is null ? null : new UserStatus(unit, provider);

    internal static IConditionEvaluator Conditions(ApprovalUnitOfWork unit, IConditionEvaluator provider) => new ConditionsProvider(unit, provider);

    internal static TimeProvider Clock(ApprovalUnitOfWork unit, TimeProvider provider) => new HostClock(unit, provider);

    private sealed class HostClock(ApprovalUnitOfWork unit, TimeProvider provider) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        { using var boundary = unit.Extension(); return provider.GetUtcNow(); }

        public override long GetTimestamp()
        { using var boundary = unit.Extension(); return provider.GetTimestamp(); }

        public override long TimestampFrequency => provider.TimestampFrequency;
        public override TimeZoneInfo LocalTimeZone => provider.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => provider.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class ConditionsProvider(ApprovalUnitOfWork unit, IConditionEvaluator provider) : IConditionEvaluator
    {
        public bool Evaluate(string expression, IReadOnlyDictionary<string, object?> variables)
        { using var boundary = unit.Extension(); return provider.Evaluate(expression, variables); }
    }

    private sealed class IdentityProvider(ApprovalUnitOfWork unit, IApprovalIdentityProvider provider) : IApprovalIdentityProvider
    {
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Call(unit, () => provider.IsInRoleAsync(userId, role, ct));

        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Call(unit, () => provider.GetUserIdsInRoleAsync(role, ct));

        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Call(unit, () => provider.GetRolesAsync(userId, ct));
    }

    private sealed class UserDirectory(ApprovalUnitOfWork unit, IApprovalUserDirectory provider) : IApprovalUserDirectory
    {
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Call(unit, () => provider.SearchUsersAsync(keyword, take, ct));

        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Call(unit, () => provider.GetUsersAsync(ids, ct));

        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Call(unit, () => provider.ListRolesAsync(ct));
    }

    private sealed class UserStatus(ApprovalUnitOfWork unit, IApprovalUserStatusProvider provider) : IApprovalUserStatusProvider

    { public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Call(unit, () => provider.IsActiveAsync(userId, ct)); }
}
