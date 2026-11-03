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
    {
        var result = new List<Assignee>();
        foreach (var assignee in await ResolveRulesAsync(node.Approvers, instance, node, form, ct))
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
    {
        var context = new ApproverResolveContext { Instance = instance,
            Node = System.Text.Json.JsonSerializer.Deserialize<FlowNode>(System.Text.Json.JsonSerializer.Serialize(node))!, FormData = form };
        var result = new List<Assignee>();
        foreach (var rule in rules)
        {
            if (!_resolvers.TryGetValue(rule.Type, out var resolver))
                throw new ApprovalException($"未注册的审批人类型 '{rule.Type}'") { Code = ApprovalErrorCode.InvalidDefinition };
            using var callback = unit.Extension();
            result.AddRange(await resolver.ResolveAsync(rule.Value, context, ct));
        }
        return result;
    }

    /// <summary>
    /// 剔除无法处理任务的审批人：不允许自审时剔除发起人；角色任务要求角色内至少有一名可审批的成员，
    /// 否则任务将无人能处理、流程永久卡住。剔除后为空则交给节点的 EmptyApproverPolicy 处理。
    /// </summary>
    public async Task<List<Assignee>> FilterEligibleAsync(ApprovalInstance instance, List<Assignee> assignees, CancellationToken ct)
    {
        var eligible = new List<Assignee>(assignees.Count);
        foreach (var assignee in assignees)
        {
            var ok = assignee.Kind == AssigneeKind.User
                ? _options.AllowInitiatorApprove || assignee.Value != instance.InitiatorId
                : (await _identity.GetUserIdsInRoleAsync(assignee.Value, ct))
                    .Any(id => _options.AllowInitiatorApprove || id != instance.InitiatorId);
            if (ok && _directory is not null)
            {
                var ids = assignee.Kind == AssigneeKind.User ? new[] { assignee.Value }
                    : (await _identity.GetUserIdsInRoleAsync(assignee.Value, ct)).ToArray();
                ok = (await _directory.GetUsersAsync(ids, ct)).Any(u => u.IsActive
                    && (_options.AllowInitiatorApprove || u.Id != instance.InitiatorId));
            }
            if (ok && UserStatus is not null)
            {
                var ids = assignee.Kind == AssigneeKind.User ? new[] { assignee.Value }
                    : (await _identity.GetUserIdsInRoleAsync(assignee.Value, ct)).ToArray();
                ok = false;
                foreach (var id in ids)
                    if ((_options.AllowInitiatorApprove || id != instance.InitiatorId) && await UserStatus.IsActiveAsync(id, ct)) { ok = true; break; }
            }
            if (ok) eligible.Add(assignee);
        }
        return eligible;
    }

    /// <summary>按当前组织/用户目录计算合格人员；供岗位约束、宿主预检和无人处理告警共用。实例应包含任务历史。</summary>
    public async Task<IReadOnlyList<string>> GetEligibleApproversAsync(ApprovalInstance instance, FlowNode node, CancellationToken ct = default)
    {
        var assignees = await ResolveAssigneesAsync(instance, node, FormData.Parse(instance.FormDataJson), expandRoles: true, ct);
        var ids = assignees.Select(a => a.Value).Distinct().Where(id =>
            (_options.AllowInitiatorApprove || id != instance.InitiatorId)
            && (_options.AllowSameUserMultipleNodes || !instance.Tasks.Any(t =>
                t.NodeId != node.Id && t.HandledBy == id && t.Status == ApprovalTaskStatus.Approved))).ToList();
        if (node.RequireQualifiedAssignee && _directory is null)
            throw new ApprovalException("岗位资格校验需要注册用户目录") { Code = ApprovalErrorCode.InvalidDefinition };
        if (_directory is not null)
        {
            var known = (await _directory.GetUsersAsync(ids, ct)).Where(u => u.IsActive).Select(u => u.Id).ToHashSet();
            ids.RemoveAll(id => !known.Contains(id));
        }
        return ids;
    }
}
