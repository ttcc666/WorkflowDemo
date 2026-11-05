using Approval.Core.Abstractions;
using Approval.Core.Domain;
using System.Text.RegularExpressions;

namespace Approval.Core.Resolvers;

/// <summary>组织架构类审批人的参数：级数 N（1–10），可选 <c>@字段</c> 表示以表单人员字段中的人为基准（默认以发起人为基准）。</summary>
public readonly partial record struct ApproverLevel(int Level, string? Field)
{
    public const int Max = 10;

    [GeneratedRegex(@"^\s*(\d{1,2})\s*(?:@\s*([A-Za-z_][A-Za-z0-9_]*)\s*)?$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? value, out ApproverLevel level)
    {
        level = default;
        if (value is null || Pattern().Match(value) is not { Success: true } match) return false;
        var n = int.Parse(match.Groups[1].Value);
        if (n is < 1 or > Max) return false;
        level = new ApproverLevel(n, match.Groups[2].Success ? match.Groups[2].Value : null);
        return true;
    }

    public static ApproverLevel Parse(string? value, string type) =>
        TryParse(value, out var level)
            ? level
            : throw new ApprovalException($"审批人类型 {type} 的参数 '{value}' 无效，应为 1–{Max} 的级数，可带 @表单人员字段")
            {
                Code = ApprovalErrorCode.InvalidDefinition
            };
}

/// <summary>组织架构类审批人的基类：解析级数与基准人员（发起人，或 <c>@字段</c> 指定的表单人员字段；多人字段取第一个）。</summary>
public abstract class OrganizationApproverResolver(IApprovalOrganizationProvider organization) : IApproverResolver
{
    protected IApprovalOrganizationProvider Organization { get; } = organization;

    public abstract string Type { get; }

    public abstract ApproverTypeDescriptor Descriptor { get; }

    public async Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
    {
        var level = ApproverLevel.Parse(value, Type);
        var baseUser = level.Field is null
            ? context.Instance.InitiatorId
            : context.FormData.GetValueOrDefault(level.Field) switch
            {
                string id => id,
                object?[] ids => ids.OfType<string>().FirstOrDefault(),
                _ => null
            };
        // 基准人员字段为空：解析不到审批人，交给节点的“找不到审批人时”策略
        if (string.IsNullOrWhiteSpace(baseUser)) return [];

        var users = await ResolveUsersAsync(baseUser, level.Level, context, ct);
        return users.Distinct().Select(id => new Assignee(AssigneeKind.User, id)).ToList();
    }

    protected abstract Task<IReadOnlyList<string>> ResolveUsersAsync(string baseUser, int level, ApproverResolveContext context,
        CancellationToken ct);

    protected const string LevelHelp = "填写级数 N；可写 N@字段，以表单人员字段中的人为基准（如 1@payee 表示收款人），默认以发起人为基准。";
}

/// <summary>部门负责人：N=1 为基准人员所在部门的负责人，N=2 为上级部门的负责人，依此类推；没有该级部门时为空。</summary>
public sealed class DeptLeaderApproverResolver(IApprovalOrganizationProvider organization) : OrganizationApproverResolver(organization)
{
    public override string Type => "DeptLeader";

    public override ApproverTypeDescriptor Descriptor { get; } = new("DeptLeader", "部门负责人", ApproverValueKind.Level)
    {
        Help = "1 为所在部门负责人，2 为上级部门负责人，依此类推；没有该级部门时按“找不到审批人”处理。" + LevelHelp
    };

    protected override async Task<IReadOnlyList<string>> ResolveUsersAsync(string baseUser, int level, ApproverResolveContext context,
        CancellationToken ct)
    {
        var department = await Organization.GetDepartmentAsync(baseUser, ct);
        context.Trace?.Invoke(new(Type, $"基准人员 {baseUser}，所在部门：{department?.Name ?? "无"}"));
        var visited = new HashSet<string>();
        for (var i = 1; department is not null && i < level; i++)
        {
            if (!visited.Add(department.Id)) return [];   // 部门关系有环
            department = await Organization.GetParentDepartmentAsync(department.Id, ct);
            context.Trace?.Invoke(new(Type, $"第 {i + 1} 级部门：{department?.Name ?? "无"}"));
        }
        return department is null ? [] : await Organization.GetDepartmentLeadersAsync(department.Id, ct);
    }
}

/// <summary>第 N 级上级：沿直属上级向上 N 次；没有该级上级时为空。</summary>
public sealed class ManagerApproverResolver(IApprovalOrganizationProvider organization) : OrganizationApproverResolver(organization)
{
    public override string Type => "Manager";

    public override ApproverTypeDescriptor Descriptor { get; } = new("Manager", "上级", ApproverValueKind.Level)
    {
        Help = "1 为直属上级，2 为上级的上级，依此类推；没有该级上级时按“找不到审批人”处理。" + LevelHelp
    };

    protected override async Task<IReadOnlyList<string>> ResolveUsersAsync(string baseUser, int level, ApproverResolveContext context,
        CancellationToken ct)
    {
        var visited = new HashSet<string> { baseUser };
        var current = baseUser;
        for (var i = 0; i < level; i++)
        {
            var manager = await Organization.GetManagerAsync(current, ct);
            context.Trace?.Invoke(new(Type, $"第 {i + 1} 级：{current} → {manager ?? "无上级"}"));
            if (string.IsNullOrWhiteSpace(manager) || !visited.Add(manager)) return [];
            current = manager;
        }
        return [current];
    }
}

/// <summary>
/// 逐级审批：从直属上级开始到第 N 级上级的有序列表（节点须为依次审批）。遇到没有上级时截止；同一人只审一次；
/// 链上遇到发起人本人时跳过（不能审批自己的申请）。
/// </summary>
public sealed class ManagerChainApproverResolver(IApprovalOrganizationProvider organization) : OrganizationApproverResolver(organization)
{
    public override string Type => "ManagerChain";

    public override ApproverTypeDescriptor Descriptor { get; } = new("ManagerChain", "逐级上级", ApproverValueKind.Level)
    {
        Help = "从直属上级开始逐级审批到第 N 级上级，遇到没有上级时截止，同一人只审一次。审批方式须为依次审批。" + LevelHelp,
        Ordered = true
    };

    protected override async Task<IReadOnlyList<string>> ResolveUsersAsync(string baseUser, int level, ApproverResolveContext context,
        CancellationToken ct)
    {
        var chain = new List<string>();
        var visited = new HashSet<string> { baseUser };
        var current = baseUser;
        for (var i = 0; i < level; i++)
        {
            var manager = await Organization.GetManagerAsync(current, ct);
            context.Trace?.Invoke(new(Type, $"第 {i + 1} 级：{current} → {manager ?? "无上级"}"));
            if (string.IsNullOrWhiteSpace(manager) || !visited.Add(manager)) break;
            current = manager;
            if (manager != context.Instance.InitiatorId) chain.Add(manager);
        }
        return chain;
    }
}
