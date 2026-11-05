using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Extensions;
using Approval.Core.Engine.Runtime;
using Approval.Core.Persistence;

namespace Approval.Core.Engine;

public sealed record ApprovalSimulationRequest(string DefinitionJson, System.Text.Json.JsonElement FormData, string? NodeId = null, int? RuleIndex = null);
public sealed record ApprovalSimulationEvaluation(string? Expression, bool Matched, string NextNodeId);
public sealed record ApprovalSimulationMember(string UserId, string? Name, bool? IsEligible, string? ExclusionReason);
public sealed record ApprovalSimulationAssignment(Assignee TaskAssignee, IReadOnlyList<ApprovalSimulationMember> Members);
public sealed record ApprovalSimulationStep(string NodeId, string NodeName, NodeType NodeType, string Status,
    IReadOnlyList<Assignee> ResolvedAssignees, IReadOnlyList<Assignee> ExcludedAssignees,
    IReadOnlyList<ApprovalSimulationEvaluation> Evaluations, string? BlockReason)
{
    public IReadOnlyList<ApprovalSimulationAssignment> Assignments { get; init; } = [];
    public IReadOnlyList<ApproverResolutionTrace> ResolutionTrace { get; init; } = [];
}
public sealed record ApprovalSimulationResult(bool IsValid, bool IsComplete, IReadOnlyList<ApprovalSimulationStep> Steps,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>只读预演，不构造实体、不创建任务、不执行业务与生命周期回调。</summary>
public sealed class ApprovalSimulationService
{
    private readonly ApprovalDbContextAccessor _accessor;
    private readonly ApprovalDefinitionService _definitions;
    private readonly ApprovalAssigneeService _assignees;
    private readonly IConditionEvaluator _conditions;

    internal ApprovalSimulationService(ApprovalDbContextAccessor accessor, ApprovalDefinitionService definitions,
        ApprovalExtensionCatalog extensions, IApprovalIdentityProvider identity, ApprovalOptions options,
        IConditionEvaluator conditions, IApprovalUserDirectory? directory, IApprovalUserStatusProvider? status)
    {
        _accessor = accessor;
        _definitions = definitions;
        var unit = accessor.UnitOfWork;
        _conditions = ApprovalHostServices.Conditions(unit, conditions);
        _assignees = new(ApprovalHostServices.Identity(unit, identity), extensions.Resolvers, options,
            ApprovalHostServices.Directory(unit, directory), unit) { UserStatus = ApprovalHostServices.Status(unit, status) };
    }

    public async Task<ApprovalSimulationResult> SimulateAsync(ApprovalSimulationRequest request, CancellationToken ct = default)
    {
        await _accessor.Authorization.RequireAsync(ApprovalPermissions.DefinitionSimulate, new(), null, ct);
        await _accessor.Authorization.RequireAsync(ApprovalPermissions.DirectoryRead, new(), null, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ct = timeout.Token;
        using var readOnly = _accessor.UnitOfWork.Extension();
        var steps = new List<ApprovalSimulationStep>();
        var budget = new ApprovalSimulationBudget();
        var warnings = new List<string>
        {
            "只预演自动路径；人工审批及其后的表单修改和路径尚未模拟。",
            "不执行业务准备或生命周期回调；输入应包含正式启动准备后的流程变量。草稿没有实例或业务标识，依赖这些标识的 Resolver 需要宿主另行验证。"
        };
        try
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(request.DefinitionJson)) throw ApprovalAutomaticTraversal.Invalid("流程定义不能为空");
            var flow = FlowDefinition.Parse(request.DefinitionJson);
            var errors = await _definitions.ValidateAsync(flow, ct);
            if (errors.Count > 0) return new(false, false, steps, errors, warnings);
            warnings.AddRange(_definitions.Warnings(flow));
            var formJson = request.FormData.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "{}" : request.FormData.GetRawText();
            if (_definitions.DefinitionExtension is { } extension) formJson = extension.NormalizeSimulationData(flow, formJson);
            var form = FormData.Parse(formJson);
            var context = new ApprovalInstanceContext
            {
                DefinitionKey = "", BusinessType = "", BusinessKey = "", InitiatorId = _accessor.ActorId,
                Status = InstanceStatus.Running, FormDataJson = formJson
            };
            if (request.RuleIndex is not null && request.NodeId is null) throw ApprovalAutomaticTraversal.Invalid("规则预览必须指定节点");
            async Task<bool> Enter(FlowNode node, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                budget.BeginNode();
                steps.Add(new(node.Id, node.Name, node.Type, "Visited", [], [], [], null));
                var assigned = new List<Assignee>();
                var excluded = new List<Assignee>();
                var assignments = new List<ApprovalSimulationAssignment>();
                var trace = new List<ApproverResolutionTrace>();
                var status = "Visited";
                string? reason = null;
                if (node.Type is NodeType.Approval or NodeType.Cc)
                {
                    var target = node;
                    if (request.RuleIndex is { } index)
                    {
                        if (index < 0 || index >= node.Approvers.Count) throw ApprovalAutomaticTraversal.Invalid("规则序号无效");
                        // 预览副本，不修改调用方的草稿或运行定义。
                        target = System.Text.Json.JsonSerializer.Deserialize<FlowNode>(System.Text.Json.JsonSerializer.Serialize(node, FormData.JsonOptions), FormData.JsonOptions)!;
                        target.Approvers = [node.Approvers[index]];
                    }
                    var resolved = await _assignees.ResolveAssigneesAsync(context, target, form,
                        expandRoles: node.Type == NodeType.Approval && node.Mode != ApproveMode.Any, token, item =>
                        {
                            budget.Trace();
                            trace.Add(item);
                        }, budget);
                    if (resolved.Count > 200) throw ApprovalAutomaticTraversal.Invalid("预览处理人超过 200 人预算");
                    assigned = node.Type == NodeType.Approval
                        ? await _assignees.FilterEligibleAsync(context.InitiatorId, resolved, token, budget) : resolved;
                    excluded = resolved.Except(assigned).ToList();
                    if (node.Type == NodeType.Approval)
                        foreach (var assignee in resolved) assignments.Add(new(assignee, await _assignees.InspectAsync(context.InitiatorId, assignee, token, budget)));
                    if (node.Type == NodeType.Approval)
                    {
                        if (assigned.Count > 0) { status = "RequiresDecision"; reason = "需要人工决策；后续路径未模拟"; }
                        else if (node.EmptyApproverPolicy == EmptyApproverPolicy.AutoApprove) { status = "AutoApproved"; reason = "无人审批，按节点策略自动通过"; }
                        else { status = "Blocked"; reason = "未找到有效审批人"; }
                    }
                    if (assigned.Any(a => a.Kind == AssigneeKind.Role)) warnings.Add($"节点 {node.Id} 保留角色任务；展示的是任务分配对象，不代表每个角色成员都有资格。考虑角色实际成员时应以宿主当前目录为准。");
                }
                steps[^1] = new(node.Id, node.Name, node.Type, status, assigned, excluded, [], reason) { Assignments = assignments, ResolutionTrace = trace };
                return status is not ("RequiresDecision" or "Blocked");
            }
            if (request.NodeId is { } nodeId)
            {
                var node = flow.GetNode(nodeId);
                if (node.Type is not (NodeType.Approval or NodeType.Cc)) throw ApprovalAutomaticTraversal.Invalid("只能预览审批或抄送节点");
                await Enter(node, ct);
                return new(steps.All(s => s.Status != "Blocked"), false, steps, [], warnings);
            }
            var joins = new Dictionary<string, JoinState>();
            var count = 0;
            var traversal = new ApprovalAutomaticTraversal(flow, FlowStructure.Analyze(flow), _conditions, joins, () => true,
                () => { if (++count > ApprovalAutomaticTraversal.MaxAutoSteps) throw ApprovalAutomaticTraversal.Invalid("流程自动推进步数超限，请检查流程定义"); }, Enter,
                (node, expression, match, next) =>
                {
                    var last = steps[^1];
                    steps[^1] = last with { Evaluations = [.. last.Evaluations, new(expression, match, next)] };
                }) { Form = () => form };
            var end = await traversal.AdvanceAsync(flow.StartNodeId, ct);
            foreach (var join in joins)
                steps.Add(new(join.Key, flow.GetNode(join.Key).Name, NodeType.Join, "Waiting", [], [], [],
                    $"等待并行分支到达汇聚（{join.Value.ArrivedBranches.Count}/{join.Value.Expected}）；人工决策后的路径未模拟"));
            return new(steps.All(s => s.Status != "Blocked"), end is not null, steps, [], warnings.Distinct().ToArray());
        }
        catch (ApprovalException ex) when (ex.Code is ApprovalErrorCode.InvalidDefinition or ApprovalErrorCode.InvalidOperation or ApprovalErrorCode.Validation)
        {
            if (steps.Count > 0) steps[^1] = steps[^1] with { Status = "Failed", BlockReason = ex.Message };
            return new(false, false, steps, [ex.Message], warnings);
        }
        catch (System.Text.Json.JsonException)
        { return new(false, false, steps, ["模拟输入不是有效 JSON"], warnings); }
    }
}
