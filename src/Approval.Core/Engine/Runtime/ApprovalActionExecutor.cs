using System.Text;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Extensions;
using Approval.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine.Runtime;

internal sealed record ApprovalActionRuntime(ApprovalOperationContext Operation, ApprovalInstance Instance, FlowNode Node,
    ApprovalEventWriter Events, ExecutionState State);

internal sealed class ApprovalActionExecutor(ApprovalDbContextAccessor accessor, ApprovalExtensionCatalog extensions,
    ApprovalEventWriter events, TimeProvider clock, ILogger<ApprovalEngine> logger)
{
    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new { });
    private readonly ApprovalUnitOfWork _unit = accessor.UnitOfWork;

    internal async Task ExecuteAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowStructure structure,
        FlowNode node, CancellationToken ct)
    {
        var state = operation.StateOf(instance);
        using var document = JsonDocument.Parse(instance.FormDataJson);
        if (node.Actions is not { Count: > 0 } actions) throw ApprovalAutomaticTraversal.Invalid("动作节点没有步骤");
        foreach (var step in actions)
        {
            ct.ThrowIfCancellationRequested();
            if (++operation.Steps > ApprovalAutomaticTraversal.MaxAutoSteps)
                throw ApprovalAutomaticTraversal.Invalid("流程自动推进步数超限，请检查流程定义");

            var executionId = step.Mode == ActionExecutionMode.Sync
                ? _unit.NewId("ApprovalEngine.Action")
                : Guid.ParseExact(ApprovalCommandHash.Of(instance.Id.ToString("N"), node.Id, step.Id)[..32], "N");
            if (step.Mode == ActionExecutionMode.Async && state.QueuedActions?.Contains(executionId) == true) continue;
            string? configError;
            using (_unit.Extension()) configError = ApprovalActionValidation.FindError(step, extensions.Actions);
            if (configError is not null) throw ApprovalAutomaticTraversal.Invalid($"动作 '{node.Id}/{step.Id}' 配置已不可用");
            var context = new ApprovalActionContext(instance.Id, instance.DefinitionKey, instance.DefinitionVersion,
                instance.BusinessType, instance.BusinessKey, instance.InitiatorId, accessor.ActorId, node.Id, node.Name,
                step.Id, instance.Round, instance.Title, instance.Status, executionId, step.ConfigVersion,
                document.RootElement, JsonSerializer.SerializeToElement(state.OutputsFor(structure, node.Id)), step.Mode);
            var handler = extensions.Actions[step.Type];
            if (step.Mode == ActionExecutionMode.Async)
            {
                var stamp = handler is Actions.HttpActionHandler http ? http.DestinationStamp(step.Config) : null;
                events.QueueAction(operation, instance, node, new(1, step.Type, context, step.Config, stamp));
                (state.QueuedActions ??= []).Add(executionId);
                events.AddLog(operation, instance, ApprovalAction.ActionExecuted, accessor.ActorId, null, node.Id, node.Name,
                    JsonSerializer.Serialize(new { step.Id, step.Type, mode = "Async", status = "Queued", outboxId = executionId, instance.Round },
                        JsonSerializerOptions.Web));
                continue;
            }
            var startedAt = clock.GetUtcNow();
            var timestamp = clock.GetTimestamp();
            string? failure = null;
            var bytes = 0;
            try
            {
                ApprovalActionResult result;
                if (handler is Actions.NotificationActionHandler notification)
                    result = await notification.ExecuteAsync(context with { Runtime = new(operation, instance, node, events, state) }, step.Config, ct);
                else
                    using (_unit.Extension()) result = await handler.ExecuteAsync(context, step.Config, ct);
                ct.ThrowIfCancellationRequested();
                if (!result.Succeeded) failure = "Rejected";
                else if (step.OutputKey is { } key)
                {
                    var value = result.Output ?? JsonSerializer.SerializeToElement<object?>(null);
                    bytes = StoreOutput(state, key, node.Id, value);
                }
            }
            catch (Exception)
            {
                ct.ThrowIfCancellationRequested();
                failure = "ExecutionFailed";
            }

            var completedAt = clock.GetUtcNow();
            var details = JsonSerializer.Serialize(new
            {
                step.Id, step.Type, mode = step.Mode.ToString(), executionId, instance.Round,
                status = failure is null ? "Succeeded" : "Failed", failure, startedAt, completedAt,
                durationMs = clock.GetElapsedTime(timestamp).TotalMilliseconds, outputBytes = bytes
            }, JsonSerializerOptions.Web);
            if (failure is not null && step.FailurePolicy != ActionFailurePolicy.Continue)
            {
                logger.LogWarning("审批动作执行失败。InstanceId={InstanceId} NodeId={NodeId} ActionId={ActionId} ExecutionId={ExecutionId} Reason={Reason}",
                    instance.Id, node.Id, step.Id, executionId, failure);
                throw new ApprovalException($"动作 '{node.Id}/{step.Id}' 执行失败（{failure}）") { Code = ApprovalErrorCode.InvalidOperation };
            }
            events.AddLog(operation, instance, ApprovalAction.ActionExecuted, accessor.ActorId, null, node.Id, node.Name, details);
        }
    }

    internal static IReadOnlyDictionary<string, object?> ConditionVariables(FlowDefinition flow, FlowStructure structure,
        ExecutionState state, FlowNode node, string formJson)
    {
        var form = FormData.Parse(formJson);
        if (!flow.Nodes.Values.Any(n => n.Type == NodeType.Action)) return form;
        var variables = new Dictionary<string, object?>(form, StringComparer.OrdinalIgnoreCase);
        variables.Remove("form");
        variables.Remove("outputs");
        using var document = JsonDocument.Parse(formJson);
        variables.Add("form", FormData.Convert(document.RootElement));
        variables.Add("outputs", FormData.Convert(JsonSerializer.SerializeToElement(state.OutputsFor(structure, node.Id))));
        return variables;
    }

    internal static int StoreOutput(ExecutionState state, string key, string nodeId, JsonElement value)
    {
        var bytes = Encoding.UTF8.GetByteCount(value.GetRawText());
        if (bytes > ApprovalActionValidation.MaxOutputBytes ||
            state.ActionOutputs.Count >= ApprovalActionValidation.MaxOutputs ||
            bytes + state.ActionOutputs.Values.Sum(o => Encoding.UTF8.GetByteCount(o.Value.GetRawText())) > ApprovalActionValidation.MaxTotalOutputBytes)
            throw ApprovalAutomaticTraversal.Invalid("动作输出超过存储预算");
        using var document = JsonDocument.Parse(value.GetRawText(), new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
        state.ActionOutputs.Add(key, new(nodeId, document.RootElement.Clone()));
        return bytes;
    }
}
