using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Notifications;
using Approval.AspNetCore;

namespace Certificate.Api.Infrastructure;

/// <summary>
/// 仅供演示：用户与角色来自配置 DemoUsers。
/// 生产环境请替换为对接组织架构 / 认证系统的实现。
/// </summary>
public sealed class DemoIdentityProvider(IConfiguration configuration)
    : IApprovalIdentityProvider, IApprovalContactProvider, IApprovalUserDirectory
{
    // 配置系统会丢弃空数组，因此用对象列表表示用户，没有角色的用户也能保留
    private sealed class DemoUser
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string[] Roles { get; set; } = [];
    }

    private List<DemoUser> All => configuration.GetSection("DemoUsers").Get<List<DemoUser>>() ?? [];

    private Dictionary<string, string[]> Users => All.ToDictionary(u => u.Id, u => u.Roles);

    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) =>
        Task.FromResult(Users.TryGetValue(userId, out var roles) && roles.Contains(role));

    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Users.Where(u => u.Value.Contains(role)).Select(u => u.Key).ToList());

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Users.TryGetValue(userId, out var roles) ? roles : []);

    public bool Exists(string userId) => Users.ContainsKey(userId);

    /// <summary>演示用户列表（前端右上角切换当前用户）。</summary>
    public IEnumerable<object> ListUsers() => All.Select(u => new { id = u.Id, name = u.Name ?? u.Id, roles = u.Roles });

    // ───── IApprovalUserDirectory：用户选择器、角色下拉 ─────

    public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApprovalUser>>(All
            .Where(u => string.IsNullOrEmpty(keyword)
                        || u.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (u.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(take)
            .Select(ToUser)
            .ToList());

    public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApprovalUser>>(All.Where(u => ids.Contains(u.Id)).Select(ToUser).ToList());

    public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(All.SelectMany(u => u.Roles).Distinct().Order().ToList());

    private static ApprovalUser ToUser(DemoUser u) => new(u.Id, u.Name ?? u.Id, u.Roles);

    /// <summary>演示：邮箱为 {用户}@example.com，企业微信 / 钉钉 UserId 与用户 ID 相同。</summary>
    public Task<string?> GetContactAsync(string userId, string channel, CancellationToken ct) =>
        Task.FromResult<string?>(channel == "Email" ? $"{userId}@example.com" : userId);
}

/// <summary>
/// 仅供演示：当前用户取自请求头 X-User-Id（须为配置中存在的用户），前端右上角可切换。
/// 生产环境删除此类即可使用默认的 <see cref="ClaimsApprovalCurrentUser"/>（读取认证后的 Claims）。
/// </summary>
public sealed class DemoCurrentUser(DemoIdentityProvider identity) : IApprovalCurrentUser
{
    public const string Header = "X-User-Id";

    public string? GetUserId(HttpContext http)
    {
        var userId = http.Request.Headers[Header].ToString();
        return !string.IsNullOrWhiteSpace(userId) && identity.Exists(userId) ? userId : null;
    }
}
