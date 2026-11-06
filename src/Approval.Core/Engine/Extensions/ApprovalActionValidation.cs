using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Approval.Core.Engine.Extensions;

internal static partial class ApprovalActionValidation
{
    internal const int MaxStepsPerNode = 20;
    internal const int MaxOutputs = 100;
    internal const int MaxOutputBytes = 16 * 1024;
    internal const int MaxTotalOutputBytes = 256 * 1024;
    internal const int MaxConfigBytes = 32 * 1024;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]{0,99}\\z")]
    private static partial Regex Id();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}\\z")]
    private static partial Regex OutputKey();

    internal static bool IsId(string? value) => value is not null && Id().IsMatch(value);

    internal static void Validate(FlowDefinition flow, List<string> errors,
        Func<IReadOnlyDictionary<string, IApprovalActionHandler>>? actions, bool dispatchEnabled)
    {
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in flow.Nodes.Values)
        {
            if (node.Type != NodeType.Action)
            {
                if (node.Actions is not null) errors.Add($"节点 '{node.Id}' 不能配置动作");
                continue;
            }

            if (node.Approvers.Count != 0 || node.Branches.Count != 0)
                errors.Add($"动作节点 '{node.Id}' 不能配置审批人或分支");
            if (node.Actions is not { Count: > 0 and <= MaxStepsPerNode })
            {
                errors.Add($"动作节点 '{node.Id}' 必须配置 1–{MaxStepsPerNode} 个步骤");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in node.Actions)
            {
                var location = $"动作节点 '{node.Id}'";
                if (step is null || !IsId(step.Id) || !ids.Add(step.Id))
                {
                    errors.Add($"{location} 的步骤 ID 无效或重复");
                    continue;
                }
                location += $" 步骤 '{step.Id}'";
                if (step.Name?.Length > 200) errors.Add($"{location} 名称过长");
                if (step.Mode == ActionExecutionMode.Async && !dispatchEnabled)
                    errors.Add($"{location} 需要启用 Outbox Dispatcher");
                if (step.OutputKey is { } key &&
                    (step.Mode != ActionExecutionMode.Sync || !OutputKey().IsMatch(key) || !outputs.Add(key)))
                    errors.Add($"{location} 的输出键无效、重复或用于异步步骤");

                if (FindError(step, actions?.Invoke()) is { } error) errors.Add($"{location}：{error}");
            }
        }
        if (outputs.Count > MaxOutputs) errors.Add($"流程最多配置 {MaxOutputs} 个动作输出");
    }

    internal static string? FindError(ActionStep step, IReadOnlyDictionary<string, IApprovalActionHandler>? handlers)
    {
        if (!IsId(step.Type) || handlers is null || !handlers.TryGetValue(step.Type, out var handler))
            return "动作类型未注册";
        var descriptor = handler.Descriptor;
        if (!Enum.IsDefined(step.Mode) || !descriptor.SupportedModes.Contains(step.Mode))
            return "动作不支持该执行模式";
        if (step.OutputKey is not null && handler is Actions.NotificationActionHandler)
            return "通知动作没有输出变量";
        if (step.Mode == ActionExecutionMode.Async
                ? step.FailurePolicy != ActionFailurePolicy.Retry
                : step.FailurePolicy != ActionFailurePolicy.Fail &&
                  (step.FailurePolicy != ActionFailurePolicy.Continue || !descriptor.AllowsContinue))
            return "动作不支持该失败策略";
        if (step.ConfigVersion < 1 || step.Config.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(step.Config.GetRawText()) > MaxConfigBytes)
            return "动作配置必须是带有效版本的 JSON 对象，且大小不能超过 32 KiB";
        try
        {
            using var config = JsonDocument.Parse(step.Config.GetRawText(),
                new JsonDocumentOptions { MaxDepth = 24, AllowDuplicateProperties = false });
        }
        catch (JsonException) { return "动作配置包含重复字段或嵌套过深"; }
        return handler.ValidateConfig(step.ConfigVersion, step.Mode, step.Config).FirstOrDefault();
    }
}
