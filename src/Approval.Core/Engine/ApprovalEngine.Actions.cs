using System.Text.Json.Nodes;
using Approval.Core.Actions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Templates;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine;

// 动作节点：同步动作在事务内执行，异步动作写入 Outbox 由后台执行
public sealed partial class ApprovalEngine
{
    private async Task RunActionsAsync(ApprovalInstance instance, FlowNode node, CancellationToken ct)
    {
        foreach (var step in node.Actions)
        {
            if (!_actions.TryGetValue(step.Type, out var handler))
                throw new ApprovalException($"未注册的动作类型 '{step.Type}'") { Code = ApprovalErrorCode.InvalidDefinition };

            var label = string.IsNullOrWhiteSpace(step.Name) ? handler.Descriptor.DisplayName : step.Name;
            var form = FormData.Parse(instance.FormDataJson);
            var parameters = ResolveParameters(handler.Descriptor, step, TemplateVariables.Build(instance, form, node, _options));

            if (step.Mode == ActionMode.Async)
            {
                AddOutbox(OutboxKind.Action, handler.Descriptor.Type, instance, node, label,
                    _ => new AsyncActionPayload(node.Id, label, parameters));
                AddLog(instance, node, ApprovalAction.Action, SystemOperator, $"{label}：已提交后台执行");
                continue;
            }

            ApprovalActionResult result;
            try
            {
                result = await handler.ExecuteAsync(new ApprovalActionContext
                {
                    Instance = instance,
                    NodeId = node.Id,
                    StepName = label,
                    Parameters = parameters,
                    FormData = form
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (!step.ContinueOnError)
                    throw new ApprovalException($"节点 '{node.Name}' 的动作 '{label}' 执行失败：{ex.Message}", ex);

                _logger.LogWarning(ex, "动作执行失败（已配置失败继续）：{Flow} {Node} {Step}", instance.DefinitionKey, node.Id, label);
                AddLog(instance, node, ApprovalAction.Action, SystemOperator, Truncate($"{label}：执行失败（已忽略）：{ex.Message}", 1000));
                continue;
            }

            ApplyActionResult(instance, step, result);
            AddLog(instance, node, ApprovalAction.Action, SystemOperator,
                Truncate($"{label}：执行成功" + (result.Message is null ? "" : $"，{result.Message}"), 1000));
        }
    }

    /// <summary>参数：先填默认值，再按参数描述对占位符做模板替换（代码类参数不替换）。</summary>
    private Dictionary<string, string> ResolveParameters(ApprovalActionDescriptor descriptor, ActionStep step,
        IReadOnlyDictionary<string, object?> variables)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in descriptor.Parameters.Where(p => p.DefaultValue is not null))
            result[parameter.Name] = parameter.DefaultValue!;

        foreach (var (name, value) in step.Parameters)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var meta = descriptor.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var encoding = meta is null ? TemplateEncoding.Raw : meta.Template;
            result[name] = encoding is { } e ? TemplateRenderer.Render(value, variables, e, _options.DisplayTimeZone) : value;
        }
        return result;
    }

    /// <summary>把同步动作的结果写回表单：FormUpdates 合并到表单，Output 写入 OutputVariable。</summary>
    private static void ApplyActionResult(ApprovalInstance instance, ActionStep step, ApprovalActionResult result)
    {
        var form = JsonNode.Parse(instance.FormDataJson) as JsonObject ?? new JsonObject();
        var original = form.DeepClone();

        if (result.FormUpdates is not null)
        {
            foreach (var (key, value) in result.FormUpdates) form[key] = value?.DeepClone();
        }
        if (!string.IsNullOrWhiteSpace(step.OutputVariable)) form[step.OutputVariable] = result.Output?.DeepClone();

        if (!JsonNode.DeepEquals(original, form)) instance.FormDataJson = form.ToJsonString(FormData.JsonOptions);
    }
}
