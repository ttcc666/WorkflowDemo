using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;

namespace Approval.Core.Engine.Runtime;

/// <summary>正式执行的副作用适配；自动导航与只读预演共享。</summary>
internal sealed class ApprovalRuntimeNavigator(
    ApprovalAssigneeService assignees, IConditionEvaluator evaluator, ApprovalTaskCoordinator tasks, ApprovalEventWriter events,
    ApprovalActionExecutor actions)
{
    internal Task<FlowNode?> AdvanceAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow,
        string nodeId, CancellationToken ct, string? sourceNodeId = null, ParallelScope? directBranch = null)
    {
        var structure = operation.StructureOf(flow);
        var state = operation.StateOf(instance);
        var traversal = new ApprovalAutomaticTraversal(flow, structure, evaluator,
            state.Joins, () => instance.Status == InstanceStatus.Running,
            () =>
            {
                if (++operation.Steps > ApprovalAutomaticTraversal.MaxAutoSteps)
                    throw ApprovalAutomaticTraversal.Invalid("流程自动推进步数超限，请检查流程定义");
            }, async (node, token) =>
            {
                events.AddLog(operation, instance, node, ApprovalAction.NodeEntered, ApprovalEngine.SystemOperator, "进入节点");
                if (node.Type == NodeType.Action)
                {
                    await actions.ExecuteAsync(operation, instance, structure, flow, node, token);
                    return true;
                }
                var form = FormData.Parse(instance.FormDataJson);
                if (node.Type == NodeType.Cc)
                {
                    var recipients = await assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: false, token);
                    if (recipients.Count > 0)
                    {
                        foreach (var recipient in recipients) events.AddCarbonCopy(instance, node, recipient);
                        events.AddLog(operation, instance, node, ApprovalAction.Cc, ApprovalEngine.SystemOperator,
                            "抄送：" + string.Join("、", recipients.Select(ApprovalEventWriter.Describe)));
                        await events.RaiseAsync(operation, NotifyEvent.Enter, instance, flow, node, new EventInfo(recipients), token,
                            fallback: NotificationDefaults.CcRule);
                    }
                }
                if (node.Type == NodeType.Approval)
                {
                    var resolved = await assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: node.Mode != ApproveMode.Any, token);
                    var eligible = await assignees.FilterEligibleAsync(instance, resolved, token);
                    if (eligible.Count > 0)
                    {
                        await tasks.CreateTasksAsync(operation, instance, flow, node, eligible, token);
                        return false;
                    }
                    if (node.EmptyApproverPolicy != EmptyApproverPolicy.AutoApprove)
                        throw ApprovalAutomaticTraversal.Invalid($"节点 '{node.Name}' 未找到审批人");
                    events.AddLog(operation, instance, node, ApprovalAction.AutoApprove, ApprovalEngine.SystemOperator, "未找到审批人，自动通过");
                }
                return true;
            }) { Form = node => ApprovalActionExecutor.ConditionVariables(flow, structure, state, node, instance.FormDataJson) };
        return traversal.AdvanceAsync(nodeId, ct, sourceNodeId, directBranch);
    }
}
