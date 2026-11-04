using Approval.Core.Domain;
using System.Text.Json;

namespace Approval.Core.Engine;

/// <summary>合并节点的到达计数：并行节点激活时记录分支数，分支到达合并节点时累加，到齐后继续。</summary>
public sealed class JoinState
{
    public Guid ActivationId { get; set; } = Guid.NewGuid();
    public int Expected { get; set; }
    public int Arrived { get; set; }
    public HashSet<int> ArrivedBranches { get; set; } = [];
}

/// <summary>实例的运行时状态，序列化存放在 <see cref="ApprovalInstance.StateJson"/>（没有进行中的并行块时为空）。</summary>
public sealed class ExecutionState
{
    /// <summary>key 为合并节点 ID。</summary>
    public Dictionary<string, JoinState> Joins { get; set; } = [];

    public static ExecutionState Load(ApprovalInstance instance) =>
        string.IsNullOrWhiteSpace(instance.StateJson)
            ? new ExecutionState()
            : JsonSerializer.Deserialize<ExecutionState>(instance.StateJson, JsonSerializerOptions.Web) ?? new ExecutionState();

    public void SaveTo(ApprovalInstance instance) =>
        instance.StateJson = Joins.Count == 0 ? null : JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
}
