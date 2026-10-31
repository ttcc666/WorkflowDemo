namespace Approval.Core.Definitions;

/// <summary>节点所处的并行分支：并行节点 ID + 分支序号。</summary>
public readonly record struct ParallelScope(string ForkId, int Branch);

/// <summary>
/// 流程的结构分析：每个节点所在的并行分支栈（由外到内）、并行节点与合并节点的对应关系。
/// 要求并行是“结构化”的：每个并行节点的所有分支最终汇聚到同一个合并节点，分支之间不交叉、不跳出并行块，也不能在合并前结束。
/// 需要先保证流程无环、引用有效（见 <see cref="FlowDefinitionValidator"/>）。
/// </summary>
public sealed class FlowStructure
{
    private readonly FlowDefinition _flow;
    private readonly Dictionary<string, IReadOnlyList<ParallelScope>> _scopes = [];
    private readonly Dictionary<string, string> _joinOfFork = [];
    private readonly List<string> _errors = [];

    private FlowStructure(FlowDefinition flow)
    {
        _flow = flow;
        Visit(flow.StartNodeId, []);
        foreach (var fork in flow.Nodes.Values.Where(n => n.Type == NodeType.Parallel && _scopes.ContainsKey(n.Id)))
        {
            if (!_joinOfFork.ContainsKey(fork.Id)) _errors.Add($"并行节点 '{fork.Id}' 的分支没有汇聚到合并节点");
        }
    }

    public static FlowStructure Analyze(FlowDefinition flow) => new(flow);

    public IReadOnlyList<string> Errors => _errors;

    /// <summary>节点所在的并行分支栈；合并节点记录的是合并之后（外层）的栈。</summary>
    public IReadOnlyList<ParallelScope> ScopeOf(string nodeId) => _scopes.TryGetValue(nodeId, out var s) ? s : [];

    /// <summary>并行节点对应的合并节点。</summary>
    public string JoinOf(string forkId) =>
        _joinOfFork.TryGetValue(forkId, out var join) ? join : throw new ApprovalException($"并行节点 '{forkId}' 没有对应的合并节点")
        {
            Code = ApprovalErrorCode.InvalidDefinition
        };

    /// <summary>from 是否能沿连线到达 to（from 位于 to 的上游）。</summary>
    public bool CanReach(string from, string to)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<string>([from]);
        while (stack.TryPop(out var id))
        {
            if (!visited.Add(id) || !_flow.Nodes.TryGetValue(id, out var node)) continue;
            foreach (var next in Successors(node))
            {
                if (next == to) return true;
                stack.Push(next);
            }
        }
        return false;
    }

    /// <summary>a 的分支栈是否为 b 的前缀（a 与 b 同层，或 a 在 b 所在并行块的外层）。</summary>
    public static bool IsPrefix(IReadOnlyList<ParallelScope> a, IReadOnlyList<ParallelScope> b) =>
        a.Count <= b.Count && a.SequenceEqual(b.Take(a.Count));

    internal static IEnumerable<string> Successors(FlowNode node) =>
        node.Type is NodeType.Condition or NodeType.Parallel
            ? node.Branches.Select(b => b.Next).Distinct()
            : node.Next is null ? [] : [node.Next];

    private void Visit(string id, IReadOnlyList<ParallelScope> scope)
    {
        if (!_flow.Nodes.TryGetValue(id, out var node)) return;

        if (node.Type == NodeType.Join)
        {
            VisitJoin(node, scope);
            return;
        }

        // 先于“共用节点”检查：结束节点通常已被主路径访问过，这里给出更明确的提示
        if (node.Type == NodeType.End && scope.Count > 0)
        {
            _errors.Add($"并行分支在合并前到达了结束节点 '{id}'");
            return;
        }

        if (_scopes.TryGetValue(id, out var existing))
        {
            if (!existing.SequenceEqual(scope))
                _errors.Add($"节点 '{id}' 被多个并行分支共用或跳出了并行块，并行分支只能在合并节点汇聚");
            return;
        }
        _scopes[id] = scope;

        switch (node.Type)
        {
            case NodeType.Parallel:
                for (var i = 0; i < node.Branches.Count; i++)
                    Visit(node.Branches[i].Next, [.. scope, new ParallelScope(id, i)]);
                break;

            default:
                foreach (var next in Successors(node)) Visit(next, scope);
                break;
        }
    }

    private void VisitJoin(FlowNode join, IReadOnlyList<ParallelScope> scope)
    {
        if (scope.Count == 0)
        {
            _errors.Add($"合并节点 '{join.Id}' 没有对应的并行节点");
            return;
        }

        var fork = scope[^1].ForkId;
        if (_joinOfFork.TryGetValue(fork, out var mapped) && mapped != join.Id)
        {
            _errors.Add($"并行节点 '{fork}' 的分支汇聚到了不同的合并节点（'{mapped}'、'{join.Id}'）");
            return;
        }
        _joinOfFork[fork] = join.Id;

        var outer = scope.Take(scope.Count - 1).ToList();
        if (_scopes.TryGetValue(join.Id, out var existing))
        {
            if (!existing.SequenceEqual(outer)) _errors.Add($"合并节点 '{join.Id}' 同时合并了不同并行节点的分支");
            return;
        }
        _scopes[join.Id] = outer;
        if (join.Next is not null) Visit(join.Next, outer);
    }
}
