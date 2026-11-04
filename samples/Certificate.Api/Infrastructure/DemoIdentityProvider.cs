using Approval.Core;
using Approval.Core.Abstractions;
using Approval.AspNetCore;

namespace Certificate.Api.Infrastructure;

/// <summary>
/// 仅供演示：用户、角色与组织架构来自配置 DemoUsers、Departments。
/// 生产环境请替换为对接组织架构 / 认证系统的实现。
/// </summary>
public sealed class DemoIdentityProvider(IConfiguration configuration)
    : IApprovalIdentityProvider, IApprovalUserDirectory, IApprovalOrganizationProvider
{
    // 配置系统会丢弃空数组，因此用对象列表表示用户，没有角色的用户也能保留
    private sealed class DemoUser
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string[] Roles { get; set; } = [];
        public string? Department { get; set; }
        public string? Manager { get; set; }
    }

    private sealed class DemoDepartment
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }
        public string[] Leaders { get; set; } = [];
    }

    private List<DemoUser> All => configuration.GetSection("DemoUsers").Get<List<DemoUser>>() ?? [];

    private List<DemoDepartment> Departments => configuration.GetSection("Departments").Get<List<DemoDepartment>>() ?? [];

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

    // ───── IApprovalOrganizationProvider：部门负责人、上级（审批人类型 DeptLeader / Manager / ManagerChain） ─────

    public Task<ApprovalDepartment?> GetDepartmentAsync(string userId, CancellationToken ct) =>
        Task.FromResult(FindDepartment(All.FirstOrDefault(u => u.Id == userId)?.Department));

    public Task<IReadOnlyList<string>> GetDepartmentLeadersAsync(string departmentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Departments.FirstOrDefault(d => d.Id == departmentId)?.Leaders ?? []);

    public Task<ApprovalDepartment?> GetParentDepartmentAsync(string departmentId, CancellationToken ct) =>
        Task.FromResult(FindDepartment(Departments.FirstOrDefault(d => d.Id == departmentId)?.ParentId));

    public Task<string?> GetManagerAsync(string userId, CancellationToken ct) =>
        Task.FromResult(All.FirstOrDefault(u => u.Id == userId)?.Manager);

    private ApprovalDepartment? FindDepartment(string? id) =>
        Departments.FirstOrDefault(d => d.Id == id) is { } d ? new ApprovalDepartment(d.Id, d.Name, d.ParentId) : null;
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
