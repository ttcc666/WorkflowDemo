using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;

namespace Approval.Core.Engine.Runtime;

/// <summary>自动节点、条件与并行汇聚导航；返回结束节点，由引擎统一完成业务与持久化。</summary>
internal sealed class ApprovalRuntimeNavigator(
    ApprovalAssigneeService assignees, IConditionEvaluator evaluator, ApprovalTaskCoordinator tasks, ApprovalEventWriter events)
{
    private const string SystemOperator = ApprovalEngine.SystemOperator;
    private const int MaxAutoSteps = 200;
    private readonly ApprovalAssigneeService _assignees = assignees;
    private readonly IConditionEvaluator _evaluator = evaluator;
    private readonly ApprovalTaskCoordinator _tasks = tasks;
    private readonly ApprovalEventWriter _events = events;


    /// <summary>
    /// 从 nodeId 开始自动推进，直到停在需要人工处理的节点、到达结束节点，或在合并节点等待其他分支。
    /// 并行节点对每个分支递归推进。
    /// </summary>
    internal async Task<FlowNode?> AdvanceAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, string nodeId, CancellationToken ct,
        string? sourceNodeId = null, ParallelScope? directBranch = null)
    {
        IReadOnlyList<ParallelScope> branchScope = operation.StructureOf(flow).ScopeOf(sourceNodeId ?? nodeId);
        if (directBranch is { } branch) branchScope = [.. branchScope, branch];
        while (instance.Status == InstanceStatus.Running)
        {
            if (++operation.Steps > MaxAutoSteps)
                throw new ApprovalException("流程自动推进步数超限，请检查流程定义") { Code = ApprovalErrorCode.InvalidDefinition };

            var node = flow.GetNode(nodeId);
            _events.AddLog(operation, instance, node, ApprovalAction.NodeEntered, SystemOperator, "进入节点");
            // 每次推进从实例快照读取最新表单值
            var form = FormData.Parse(instance.FormDataJson);

            switch (node.Type)
            {
                case NodeType.End:
                    return node;

                case NodeType.Start:
                    nodeId = node.Next!;
                    continue;

                case NodeType.Condition:
                    nodeId = SelectBranch(node, form);
                    continue;

                case NodeType.Parallel:
                    operation.StateOf(instance).Joins[operation.StructureOf(flow).JoinOf(node.Id)] = new JoinState { Expected = node.Branches.Count };
                    for (var branchIndex = 0; branchIndex < node.Branches.Count; branchIndex++)
                    {
                        if (await AdvanceAsync(operation, instance, flow, node.Branches[branchIndex].Next, ct, node.Id, new(node.Id, branchIndex)) is { } completed) return completed;
                    }
                    return null;

                case NodeType.Join:
                    {
                        var joins = operation.StateOf(instance).Joins;
                        if (!joins.TryGetValue(node.Id, out var join))
                            throw new ApprovalException($"合并节点 '{node.Name}' 没有进行中的并行分支") { Code = ApprovalErrorCode.InvalidDefinition };
                        if (branchScope.Count == 0 || operation.StructureOf(flow).JoinOf(branchScope[^1].ForkId) != node.Id)
                            throw new ApprovalException("并行汇聚来源无效") { Code = ApprovalErrorCode.InvalidDefinition };
                        if (!join.ArrivedBranches.Add(branchScope[^1].Branch)) return null;
                        if (++join.Arrived < join.Expected) return null;
                        if (join.Arrived > join.Expected) throw new ApprovalException("并行分支到达数无效") { Code = ApprovalErrorCode.InvalidDefinition };
                        branchScope = branchScope.Take(branchScope.Count - 1).ToArray();
                        joins.Remove(node.Id);
                        nodeId = node.Next!;
                        continue;
                    }


                case NodeType.Cc:
                    {
                        var recipients = await _assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: false, ct);
                        if (recipients.Count > 0)
                        {
                            foreach (var recipient in recipients) _events.AddCarbonCopy(instance, node, recipient);
                            _events.AddLog(operation, instance, node, ApprovalAction.Cc, SystemOperator,
                                "抄送：" + string.Join("、", recipients.Select(ApprovalEventWriter.Describe)));
                            await _events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo(recipients), ct,
                                fallback: NotificationDefaults.CcRule);
                        }
                        nodeId = node.Next!;
                        continue;
                    }

                case NodeType.Approval:
                    {
                        // 会签、依次审批要求每个人都处理，因此角色需要展开为具体用户
                        var resolved = await _assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: node.Mode != ApproveMode.Any, ct);
                        var assignees = await _assignees.FilterEligibleAsync(instance, resolved, ct);

                        if (assignees.Count == 0)
                        {
                            if (node.EmptyApproverPolicy != EmptyApproverPolicy.AutoApprove)
                                throw new ApprovalException($"节点 '{node.Name}' 未找到审批人") { Code = ApprovalErrorCode.InvalidDefinition };

                            _events.AddLog(operation, instance, node, ApprovalAction.AutoApprove, SystemOperator, "未找到审批人，自动通过");
                            nodeId = node.Next!;
                            continue;
                        }

                        await _tasks.CreateTasksAsync(operation, instance, flow, node, assignees, ct);
                        return null;
                    }

                default:
                    throw new ApprovalException($"不支持的节点类型 {node.Type}") { Code = ApprovalErrorCode.InvalidDefinition };
            }
        }
        return null;
    }

    private string SelectBranch(FlowNode node, IReadOnlyDictionary<string, object?> form)
    {
        // 有条件的分支按优先级依次匹配，默认分支始终最后
        foreach (var branch in node.Branches
                     .OrderBy(b => string.IsNullOrWhiteSpace(b.Expression))
                     .ThenBy(b => b.Priority))
        {
            if (string.IsNullOrWhiteSpace(branch.Expression) || _evaluator.Evaluate(branch.Expression, form))
                return branch.Next;
        }
        throw new ApprovalException($"条件节点 '{node.Name}' 没有匹配的分支") { Code = ApprovalErrorCode.InvalidDefinition };
    }
}
