using Approval.Core.Domain;
using Approval.Core.Definitions;

namespace Approval.Core.Engine.Runtime;

/// <summary>一次引擎调用的状态；同事务中的下一条命令和所有读取调用各自创建上下文。</summary>
internal sealed class ApprovalOperationContext
{
    internal int Steps { get; set; }
    internal Guid? TaskId { get; set; }
    internal bool OutboxWritten { get; set; }
    internal Dictionary<Guid, FlowDefinition> LoadedFlows { get; } = [];
    internal Dictionary<FlowDefinition, FlowStructure> Structures { get; } = [];
    internal Dictionary<Guid, ExecutionState> States { get; } = [];

    // ───────────────────────── 辅助方法 ─────────────────────────

    internal FlowStructure StructureOf(FlowDefinition flow)
    {
        if (!Structures.TryGetValue(flow, out var structure)) Structures[flow] = structure = FlowStructure.Analyze(flow);
        return structure;
    }

    internal ExecutionState StateOf(ApprovalInstance instance)
    {
        if (!States.TryGetValue(instance.Id, out var state)) States[instance.Id] = state = ExecutionState.Load(instance);
        return state;
    }
}
