using Approval.Core.Abstractions;
using Approval.Core.Definitions;

namespace Approval.Core.Engine.Runtime;

/// <summary>正式执行与只读预演共用自动导航。人工决策和持久化由调用方拥有。</summary>
internal sealed class ApprovalAutomaticTraversal(
    FlowDefinition flow, FlowStructure structure, IConditionEvaluator evaluator,
    Dictionary<string, JoinState> joins, Func<bool> running, Action countStep,
    Func<FlowNode, CancellationToken, Task<bool>> enter,
    Action<FlowNode, string?, bool, string>? evaluated = null)
{
    internal const int MaxAutoSteps = 200;

    internal async Task<FlowNode?> AdvanceAsync(string nodeId, CancellationToken ct,
        string? sourceNodeId = null, ParallelScope? directBranch = null)
    {
        IReadOnlyList<ParallelScope> scope = structure.ScopeOf(sourceNodeId ?? nodeId);
        if (directBranch is { } branch) scope = [.. scope, branch];
        while (running())
        {
            ct.ThrowIfCancellationRequested();
            countStep();
            var node = flow.GetNode(nodeId);
            if (!await enter(node, ct)) return null;
            switch (node.Type)
            {
                case NodeType.End: return node;
                case NodeType.Start:
                case NodeType.Cc:
                case NodeType.Approval:
                    nodeId = node.Next!;
                    break;
                case NodeType.Condition:
                    nodeId = SelectBranch(node);
                    break;
                case NodeType.Parallel:
                    joins[structure.JoinOf(node.Id)] = new JoinState { Expected = node.Branches.Count };
                    for (var index = 0; index < node.Branches.Count; index++)
                        if (await AdvanceAsync(node.Branches[index].Next, ct, node.Id, new(node.Id, index)) is { } end) return end;
                    return null;
                case NodeType.Join:
                    if (!joins.TryGetValue(node.Id, out var join)) throw Invalid($"合并节点 '{node.Name}' 没有进行中的并行分支");
                    if (scope.Count == 0 || structure.JoinOf(scope[^1].ForkId) != node.Id) throw Invalid("并行汇聚来源无效");
                    if (!join.ArrivedBranches.Add(scope[^1].Branch)) return null;
                    if (join.ArrivedBranches.Count < join.Expected) return null;
                    if (join.ArrivedBranches.Count > join.Expected) throw Invalid("并行分支到达数无效");
                    scope = scope.Take(scope.Count - 1).ToArray();
                    joins.Remove(node.Id);
                    nodeId = node.Next!;
                    break;
                default: throw Invalid($"不支持的节点类型 {node.Type}");
            }
        }
        return null;
    }

    // 输入值由调用方提供，以便正式执行始终使用当前实例快照。
    internal Func<IReadOnlyDictionary<string, object?>> Form { get; init; } = () => new Dictionary<string, object?>();

    private string SelectBranch(FlowNode node)
    {
        var form = Form();
        foreach (var branch in node.Branches.OrderBy(b => string.IsNullOrWhiteSpace(b.Expression)).ThenBy(b => b.Priority))
        {
            var match = string.IsNullOrWhiteSpace(branch.Expression) || evaluator.Evaluate(branch.Expression, form);
            evaluated?.Invoke(node, branch.Expression, match, branch.Next);
            if (match) return branch.Next;
        }
        throw Invalid($"条件节点 '{node.Name}' 没有匹配的分支");
    }

    internal static ApprovalException Invalid(string message) => new(message) { Code = ApprovalErrorCode.InvalidDefinition };
}
