using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Persistence;

namespace Approval.Core.Engine.Runtime;

/// <summary>人员规则解析、角色展开与资格过滤；不修改流程状态或提交数据库。</summary>
internal sealed class ApprovalAssigneeService(
    IApprovalIdentityProvider identity,
    IReadOnlyDictionary<string, IApproverResolver> resolvers,
    ApprovalOptions options,
    IApprovalUserDirectory? directory,
    ApprovalUnitOfWork unit)
{
    private readonly IApprovalIdentityProvider _identity = identity;
    private readonly IReadOnlyDictionary<string, IApproverResolver> _resolvers = resolvers;
    private readonly ApprovalOptions _options = options;
    private readonly IApprovalUserDirectory? _directory = directory;
    internal IApprovalUserStatusProvider? UserStatus { get; set; }

    public bool CanResolve(string type) => _resolvers.ContainsKey(type);

    public async Task<List<Assignee>> ResolveAssigneesAsync(
        ApprovalInstance instance, FlowNode node, IReadOnlyDictionary<string, object?> form, bool expandRoles, CancellationToken ct)
        => await ResolveAssigneesAsync(ApprovalContextSnapshots.Instance(instance), node, form, expandRoles, ct);

    internal async Task<List<Assignee>> ResolveAssigneesAsync(
        ApprovalInstanceContext instance, FlowNode node, IReadOnlyDictionary<string, object?> form, bool expandRoles, CancellationToken ct,
        Action<ApproverResolutionTrace>? trace = null)
    {
        if (node.Type == NodeType.Approval && node.RequireQualifiedAssignee && _directory is null)
            throw new ApprovalException("岗位资格校验需要注册用户目录") { Code = ApprovalErrorCode.InvalidDefinition };
        var result = new List<Assignee>();
        foreach (var assignee in await ResolveRulesAsync(node.Approvers, instance, node, form, ct, trace))
        {
            if (expandRoles && assignee.Kind == AssigneeKind.Role)
            {
                var userIds = await _identity.GetUserIdsInRoleAsync(assignee.Value, ct);
                result.AddRange(userIds.Select(id => new Assignee(AssigneeKind.User, id)));
            }
            else
            {
                result.Add(assignee);
            }
        }
        return result.Distinct().ToList();
    }

    public async Task<List<Assignee>> ResolveRulesAsync(IEnumerable<ApproverRule> rules, ApprovalInstance instance, FlowNode node,
        IReadOnlyDictionary<string, object?> form, CancellationToken ct)
        => await ResolveRulesAsync(rules, ApprovalContextSnapshots.Instance(instance), node, form, ct);

    internal async Task<List<Assignee>> ResolveRulesAsync(IEnumerable<ApproverRule> rules, ApprovalInstanceContext instance, FlowNode node,
        IReadOnlyDictionary<string, object?> form, CancellationToken ct, Action<ApproverResolutionTrace>? trace = null)
    {
        var result = new List<Assignee>();
        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();
            if (!_resolvers.TryGetValue(rule.Type, out var resolver))
                throw new ApprovalException($"未注册的审批人类型 '{rule.Type}'") { Code = ApprovalErrorCode.InvalidDefinition };
            var context = new ApproverResolveContext
            {
                Trace = trace,
                Instance = instance,
                Node = ApprovalContextSnapshots.Node(node),
                FormData = FormData.Snapshot(form)
            };
            using var callback = unit.Extension();
            trace?.Invoke(new(rule.Type, $"规则参数：{rule.Value}"));
            var resolved = await resolver.ResolveAsync(rule.Value, context, ct);
            ct.ThrowIfCancellationRequested();
            trace?.Invoke(new(rule.Type, "规则输出：" + string.Join("、", resolved.Select(a => $"{a.Kind}:{a.Value}"))));
            result.AddRange(resolved);
        }
        return result;
    }

    /// <summary>
    /// 剔除无法处理任务的审批人：不允许自审时剔除发起人；角色任务要求角色内至少有一名可审批的成员，
    /// 否则任务将无人能处理、流程永久卡住。剔除后为空则交给节点的 EmptyApproverPolicy 处理。
    /// </summary>
    public async Task<List<Assignee>> FilterEligibleAsync(ApprovalInstance instance, List<Assignee> assignees, CancellationToken ct)
        => await FilterEligibleAsync(instance.InitiatorId, assignees, ct);

    internal async Task<List<Assignee>> FilterEligibleAsync(string initiatorId, List<Assignee> assignees, CancellationToken ct)
    {
        var eligible = new List<Assignee>(assignees.Count);
        foreach (var assignee in assignees)
        {
            var ids = assignee.Kind == AssigneeKind.User ? new[] { assignee.Value }
                : (await _identity.GetUserIdsInRoleAsync(assignee.Value, ct)).ToArray();
            IEnumerable<string> candidates = ids.Where(id => _options.AllowInitiatorApprove || id != initiatorId);
            if (_directory is not null)
            {
                var active = (await _directory.GetUsersAsync(ids, ct)).Where(u => u.IsActive).Select(u => u.Id).ToHashSet();
                candidates = candidates.Where(active.Contains);
            }
            foreach (var id in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (UserStatus is not null && !await UserStatus.IsActiveAsync(id, ct)) continue;
                eligible.Add(assignee);
                break;
            }
        }
        return eligible;
    }

    internal async Task<IReadOnlyList<ApprovalSimulationMember>> InspectAsync(string initiatorId, Assignee assignee, CancellationToken ct)
    {
        var ids = assignee.Kind == AssigneeKind.User ? new[] { assignee.Value }
            : (await _identity.GetUserIdsInRoleAsync(assignee.Value, ct)).Distinct().ToArray();
        if (ids.Length > 200) throw new ApprovalException("预览角色成员超过 200 人预算") { Code = ApprovalErrorCode.InvalidDefinition };
        var members = new List<ApprovalSimulationMember>();
        var users = _directory is null ? [] : await _directory.GetUsersAsync(ids, ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var user = users.FirstOrDefault(u => u.Id == id);
            var reason = !_options.AllowInitiatorApprove && id == initiatorId ? "不允许自审"
                : _directory is not null && user is null ? "用户目录中不存在"
                : user?.IsActive == false ? "用户已停用"
                : UserStatus is not null && !await UserStatus.IsActiveAsync(id, ct) ? "宿主用户状态无效" : null;
            var known = _directory is not null || UserStatus is not null;
            members.Add(new(id, user?.Name, reason is not null ? false : known ? true : null,
                reason ?? (known ? null : "未注册用户目录或状态 Provider，无法验证")));
        }
        return members;
    }

    /// <summary>按当前组织/用户目录计算合格人员；供岗位约束、宿主预检和无人处理告警共用。实例应包含任务历史。</summary>
    public async Task<IReadOnlyList<string>> GetEligibleApproversAsync(ApprovalInstance instance, FlowNode node, CancellationToken ct = default)
    {
        var assignees = await ResolveAssigneesAsync(instance, node, FormData.Parse(instance.FormDataJson), expandRoles: true, ct);
        var ids = assignees.Select(a => a.Value).Distinct().Where(id =>
            (_options.AllowInitiatorApprove || id != instance.InitiatorId)
            && (_options.AllowSameUserMultipleNodes || !instance.Tasks.Any(t =>
                t.NodeId != node.Id && t.HandledBy == id && t.Status == ApprovalTaskStatus.Approved))).ToList();
        return (await FilterEligibleAsync(instance.InitiatorId, ids.Select(id => new Assignee(AssigneeKind.User, id)).ToList(), ct))
            .Select(a => a.Value).ToArray();
    }
}
