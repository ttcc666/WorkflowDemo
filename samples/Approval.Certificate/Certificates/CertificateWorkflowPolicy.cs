using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Authorization;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

/// <summary>证书业务对已发布流程的准入检查；用户与组织变化后仍由引擎在实际操作时重新校验。</summary>
public sealed class CertificateWorkflowPolicy(AppDbContext db, ApprovalAuthorizationService authorization, ApprovalEngine engine, IConditionEvaluator evaluator)
{
    public async Task<Guid> EnsureReadyAsync(CertOperation operation, CertificateData data, string userId, CancellationToken ct)
    {
        var key = Key(operation);
        await authorization.RequireAsync(ApprovalPermissions.InstanceStart, new() { DefinitionKey = key, BusinessType = CertificateService.BusinessType }, userId, ct);
        // Host-internal staffing preflight: never expose the full definition through a user response.
        var management = await db.Set<ApprovalDefinitionManagement>().AsNoTracking().SingleOrDefaultAsync(m => m.Key == key, ct);
        if (management is { IsEnabled: false }) throw new ApprovalException("注册证流程已停用，不能发起新审批");
        var activeId = management?.ActiveDefinitionId;
        var definition = await db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.Key == key && d.IsPublished && (activeId == null || d.Id == activeId))
            .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct) ?? throw Invalid("注册证流程未发布");
        var flow = FlowDefinition.Parse(definition.DefinitionJson);
        if (operation == CertOperation.Create)
        {
            var required = new[] { ("dept", "DeptLeader", "1", "quality"), ("quality", "Role", "QualityManager", "leader"),
                ("leader", "Role", "Leader", "end") };
            if (flow.GetNode(flow.StartNodeId).Next != "dept" || flow.GetNode("end").Type != NodeType.End
                || required.Any(r => !flow.Nodes.TryGetValue(r.Item1, out var n) || n.Type != NodeType.Approval || n.Next != r.Item4
                    || n.Mode != ApproveMode.Any || n.Approvers.Count != 1 || n.Approvers[0] != new ApproverRule(r.Item2, r.Item3)))
                throw Invalid("注册证登记必须按部门初审、质量/法规复审、负责人终审执行，请发布试点模板");
        }
        var instance = new ApprovalInstance
        {
            DefinitionKey = key,
            BusinessType = CertificateService.BusinessType,
            BusinessKey = "preflight",
            InitiatorId = userId,
            FormDataJson = FormData.Serialize(data),
            Status = InstanceStatus.Running
        };
        var variables = FormData.Parse(instance.FormDataJson);
        var pending = new Stack<string>([flow.StartNodeId]);
        var visited = new HashSet<string>();
        var slots = new List<IReadOnlyList<string>>();
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id)) continue;
            var node = flow.GetNode(id);
            if (node.Type == NodeType.Approval)
            {
                if (!node.RequireQualifiedAssignee || node.AllowTransfer || node.AllowAddSign || node.AllowReturn
                    || node.EmptyApproverPolicy == EmptyApproverPolicy.AutoApprove || node.Timeout?.Action == TimeoutAction.AutoApprove)
                    throw Invalid($"节点「{node.Name}」尚未启用注册证岗位保护，请发布试点版本");
                var users = await engine.GetStartEligibleApproversAsync(new(key, instance.BusinessType, instance.BusinessKey, userId)
                    { DefinitionId = definition.Id, FormData = data }, node.Id, ct);
                if (users.Count == 0) throw Invalid($"节点「{node.Name}」没有合格审批人，请完善部门与岗位配置");
                if (node.Mode == ApproveMode.Any) slots.Add(users);
                else slots.AddRange(users.Select(u => (IReadOnlyList<string>)new[] { u }));
            }
            if (node.Type == NodeType.Condition)
            {
                var branch = node.Branches.OrderBy(b => string.IsNullOrWhiteSpace(b.Expression)).ThenBy(b => b.Priority)
                    .FirstOrDefault(b => string.IsNullOrWhiteSpace(b.Expression) || evaluator.Evaluate(b.Expression, variables));
                if (branch is null) throw Invalid($"节点「{node.Name}」没有匹配分支");
                pending.Push(branch.Next);
            }
            else if (node.Type == NodeType.Parallel) foreach (var branch in node.Branches) pending.Push(branch.Next);
            else if (node.Next is { } next) pending.Push(next);
        }
        var assigned = new Dictionary<string, int>();
        bool Match(int slot, HashSet<string> seen)
        {
            foreach (var user in slots[slot])
            {
                if (!seen.Add(user)) continue;
                if (!assigned.TryGetValue(user, out var previous) || Match(previous, seen)) { assigned[user] = slot; return true; }
            }
            return false;
        }
        for (var slot = 0; slot < slots.Count; slot++)
            if (!Match(slot, [])) throw Invalid("审批岗位无法分配给不同人员，请补齐替代审批人");
        return definition.Id;
    }

    public static string Key(CertOperation operation) => $"certificate.{operation.ToString().ToLowerInvariant()}";
    private static ApprovalException Invalid(string message) => new(message) { Code = ApprovalErrorCode.InvalidDefinition };
}
