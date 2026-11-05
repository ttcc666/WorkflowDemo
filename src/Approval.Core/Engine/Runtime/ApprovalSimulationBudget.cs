namespace Approval.Core.Engine.Runtime;

/// <summary>一次只读模拟的累计工作预算；正式审批不使用，去重和过滤不退还预算。</summary>
internal sealed class ApprovalSimulationBudget
{
    private const int MaxAssignees = 2000, MaxRoleMembers = 5000, MaxResolverCalls = 500, MaxTraces = 2000;
    private int _assignees, _roleMembers, _resolverCalls, _traces, _nodeTraces;
    private string? _failure;

    internal void BeginNode() { ThrowIfFailed(); _nodeTraces = 0; }
    internal void ResolverCall() => Consume(ref _resolverCalls, 1, MaxResolverCalls, "Resolver 调用累计");
    internal void Assignees(int count) => Consume(ref _assignees, count, MaxAssignees, "解析对象累计");
    internal void RoleMembers(int count) => Consume(ref _roleMembers, count, MaxRoleMembers, "角色成员累计");

    internal void BeforeRoleExpansion()
    {
        ThrowIfFailed();
        if (_roleMembers == MaxRoleMembers) Reject($"模拟请求的角色成员累计超过 {MaxRoleMembers} 项预算，请减少节点或角色范围");
    }

    internal void BeforeTrace()
    {
        ThrowIfFailed();
        if (_nodeTraces == 200) Reject("解析说明超过 200 条预算");
        if (_traces == MaxTraces) Reject($"模拟请求的解析说明累计超过 {MaxTraces} 项预算，请减少节点或规则范围");
    }

    internal void Trace()
    {
        BeforeTrace();
        _nodeTraces++; _traces++;
    }

    internal void ThrowIfFailed()
    {
        if (_failure is not null) throw ApprovalAutomaticTraversal.Invalid(_failure);
    }

    internal void Reject(string message)
    {
        _failure ??= message;
        ThrowIfFailed();
    }

    private void Consume(ref int used, int count, int limit, string dimension)
    {
        ThrowIfFailed();
        if (count > limit - used) Reject($"模拟请求的{dimension}超过 {limit} 项预算，请减少节点或规则范围");
        used += count;
    }
}
