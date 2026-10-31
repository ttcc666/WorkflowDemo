using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Notifications;

namespace Certificate.Api.Infrastructure;

/// <summary>
/// 仅供演示：用户与角色来自配置 DemoUsers，当前用户取自请求头 X-User-Id。
/// 生产环境请替换为基于 JWT / Cookie 认证的实现，并从 ClaimsPrincipal 获取用户与角色。
/// </summary>
public sealed class DemoIdentityProvider(IConfiguration configuration) : IApprovalIdentityProvider, IApprovalContactProvider
{
    // 配置系统会丢弃空数组，因此用对象列表表示用户，没有角色的用户也能保留
    private sealed class DemoUser
    {
        public string Id { get; set; } = "";
        public string[] Roles { get; set; } = [];
    }

    private Dictionary<string, string[]> Users =>
        (configuration.GetSection("DemoUsers").Get<List<DemoUser>>() ?? [])
        .ToDictionary(u => u.Id, u => u.Roles);

    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) =>
        Task.FromResult(Users.TryGetValue(userId, out var roles) && roles.Contains(role));

    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Users.Where(u => u.Value.Contains(role)).Select(u => u.Key).ToList());

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Users.TryGetValue(userId, out var roles) ? roles : []);

    public bool Exists(string userId) => Users.ContainsKey(userId);

    /// <summary>演示用户列表（前端用于切换当前用户、选择转交对象）。</summary>
    public IEnumerable<object> ListUsers() => Users.Select(u => new { id = u.Key, roles = u.Value });

    public IEnumerable<string> ListRoles() => Users.Values.SelectMany(r => r).Distinct().Order();

    /// <summary>演示：邮箱为 {用户}@example.com，企业微信 / 钉钉 UserId 与用户 ID 相同。</summary>
    public Task<string?> GetContactAsync(string userId, string channel, CancellationToken ct) =>
        Task.FromResult<string?>(channel == "Email" ? $"{userId}@example.com" : userId);
}

public static class CurrentUser
{
    public const string Header = "X-User-Id";

    public static string Get(HttpContext http)
    {
        var userId = http.Request.Headers[Header].ToString();
        var identity = http.RequestServices.GetRequiredService<DemoIdentityProvider>();
        if (string.IsNullOrWhiteSpace(userId) || !identity.Exists(userId))
            throw new ApprovalException($"请通过请求头 {Header} 指定配置中存在的演示用户") { Code = ApprovalErrorCode.Forbidden };
        return userId;
    }
}
