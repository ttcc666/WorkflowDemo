using Approval.Core.Domain;
using Approval.Core.Definitions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Approval.Core.Engine;

/// <summary>合并节点的到达计数：并行节点激活时记录分支数，分支到达合并节点时累加，到齐后继续。</summary>
public sealed class JoinState
{
    public int Expected { get; set; }
    public HashSet<int> ArrivedBranches { get; set; } = [];
}

/// <summary>实例的并行汇聚状态和已提交的同步动作输出。</summary>
public sealed class ExecutionState
{
    /// <summary>key 为合并节点 ID。</summary>
    public Dictionary<string, JoinState> Joins { get; set; } = [];

    /// <summary>输出键及其来源节点、JSON 值。</summary>
    public Dictionary<string, ActionOutput> ActionOutputs { get; set; } = [];

    /// <summary>已经随审批提交的异步动作 ID。退回不会重新投递同一步骤。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HashSet<Guid>? QueuedActions { get; set; }

    public static ExecutionState Load(ApprovalInstance instance) =>
        string.IsNullOrWhiteSpace(instance.StateJson)
            ? new ExecutionState()
            : JsonSerializer.Deserialize<ExecutionState>(instance.StateJson, JsonSerializerOptions.Web) ?? new ExecutionState();

    public void SaveTo(ApprovalInstance instance) =>
        instance.StateJson = Joins.Count == 0 && ActionOutputs.Count == 0 && QueuedActions is null
            ? null : JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    internal Dictionary<string, JsonElement> OutputsFor(FlowStructure structure, string nodeId) =>
        ActionOutputs.Where(pair => pair.Value.NodeId == nodeId || structure.CanReach(pair.Value.NodeId, nodeId))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

    internal void ClearOutputsFrom(FlowStructure structure, string targetId)
    {
        foreach (var (key, output) in ActionOutputs.ToArray())
            if (output.NodeId == targetId || structure.CanReach(targetId, output.NodeId)) ActionOutputs.Remove(key);
    }
}

/// <summary>同步动作的持久化输出。</summary>
/// <param name="NodeId">产生该输出的动作节点。</param>
/// <param name="Value">已验证的 JSON 值。</param>
public sealed record ActionOutput(string NodeId, JsonElement Value);
