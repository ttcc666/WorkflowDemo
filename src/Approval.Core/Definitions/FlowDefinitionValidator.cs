using System.Text.RegularExpressions;
using Approval.Core.Actions;
using Approval.Core.Notifications;

namespace Approval.Core.Definitions;

/// <summary>校验所需的已注册扩展。为 null 的集合不做对应检查。</summary>
public sealed class FlowValidationContext
{
    public required IReadOnlySet<string> ApproverTypes { get; init; }

    /// <summary>可用的通知渠道（含站内信 InApp）。</summary>
    public IReadOnlySet<string>? Channels { get; init; }

    public IReadOnlyDictionary<string, IApprovalActionHandler>? Actions { get; init; }
}

/// <summary>发布前对流程定义做结构校验，避免运行时才发现配置错误。</summary>
public static partial class FlowDefinitionValidator
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    public static IReadOnlyList<string> Validate(FlowDefinition definition, IReadOnlySet<string> knownApproverTypes) =>
        Validate(definition, new FlowValidationContext { ApproverTypes = knownApproverTypes });

    public static IReadOnlyList<string> Validate(FlowDefinition definition, FlowValidationContext context)
    {
        var errors = new List<string>();
        var nodes = definition.Nodes;

        if (nodes.Count == 0)
        {
            errors.Add("流程定义没有任何节点");
            return errors;
        }

        if (!nodes.TryGetValue(definition.StartNodeId, out var start) || start.Type != NodeType.Start)
            errors.Add($"StartNodeId '{definition.StartNodeId}' 必须指向 Start 类型节点");
        if (nodes.Values.Count(n => n.Type == NodeType.Start) != 1)
            errors.Add("流程必须有且仅有一个 Start 节点");
        if (!nodes.Values.Any(n => n.Type == NodeType.End))
            errors.Add("流程至少需要一个 End 节点");

        ValidateNotifications(errors, "流程级通知", definition.Notifications, null, context);

        foreach (var (key, node) in nodes)
        {
            if (node.Id != key)
                errors.Add($"节点 key '{key}' 与其 Id '{node.Id}' 不一致");

            switch (node.Type)
            {
                case NodeType.End:
                    if (node.Next is not null) errors.Add($"End 节点 '{key}' 不能有 Next");
                    break;

                case NodeType.Condition:
                    if (node.Branches.Count == 0) errors.Add($"条件节点 '{key}' 没有分支");
                    if (node.Branches.Count(b => string.IsNullOrWhiteSpace(b.Expression)) > 1)
                        errors.Add($"条件节点 '{key}' 只能有一个默认分支");
                    if (!node.Branches.Any(b => string.IsNullOrWhiteSpace(b.Expression)))
                        errors.Add($"条件节点 '{key}' 缺少默认分支（Expression 为空），条件都不满足时流程将无法推进");
                    foreach (var branch in node.Branches.Where(b => !nodes.ContainsKey(b.Next)))
                        errors.Add($"条件节点 '{key}' 的分支指向不存在的节点 '{branch.Next}'");
                    break;

                case NodeType.Parallel:
                    if (node.Next is not null) errors.Add($"并行节点 '{key}' 的后续节点应配置在分支中");
                    if (node.Branches.Count < 2) errors.Add($"并行节点 '{key}' 至少需要两个分支");
                    if (node.Branches.Select(b => b.Next).Distinct().Count() != node.Branches.Count)
                        errors.Add($"并行节点 '{key}' 有重复的分支");
                    foreach (var branch in node.Branches.Where(b => !nodes.ContainsKey(b.Next)))
                        errors.Add($"并行节点 '{key}' 的分支指向不存在的节点 '{branch.Next}'");
                    break;

                case NodeType.Approval or NodeType.Cc:
                    if (node.Approvers.Count == 0)
                        errors.Add($"节点 '{key}' 未配置{(node.Type == NodeType.Cc ? "抄送人" : "审批人")}");
                    foreach (var rule in node.Approvers.Where(r => !context.ApproverTypes.Contains(r.Type)))
                        errors.Add($"节点 '{key}' 使用了未注册的审批人类型 '{rule.Type}'");
                    goto default;

                case NodeType.Action:
                    if (node.Actions.Count == 0) errors.Add($"动作节点 '{key}' 未配置任何动作");
                    for (var i = 0; i < node.Actions.Count; i++)
                        ValidateAction(errors, $"动作节点 '{key}' 的第 {i + 1} 个动作", node.Actions[i], context);
                    goto default;

                default:
                    if (node.Next is null) errors.Add($"节点 '{key}' 缺少 Next");
                    else if (!nodes.ContainsKey(node.Next)) errors.Add($"节点 '{key}' 的 Next 指向不存在的节点 '{node.Next}'");
                    break;
            }

            if (node.Type != NodeType.Action && node.Actions.Count > 0)
                errors.Add($"节点 '{key}' 不是动作节点，不能配置动作");
            ValidateNotifications(errors, $"节点 '{key}'", node.Notifications, node.Type, context);
            if (node.Timeout is { } timeout) ValidateTimeout(errors, key, node, timeout);
        }

        if (errors.Count == 0) CheckReachabilityAndCycles(definition, errors);
        if (errors.Count == 0) errors.AddRange(FlowStructure.Analyze(definition).Errors);
        return errors;
    }

    private static void ValidateNotifications(List<string> errors, string owner, List<NotificationRule> rules, NodeType? nodeType,
        FlowValidationContext context)
    {
        var allowedEvents = NotificationDefaults.AllowedEvents(nodeType);
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var name = $"{owner}的第 {i + 1} 条通知";

            if (allowedEvents.Count == 0)
            {
                errors.Add($"{owner}不支持配置通知");
                return;
            }
            if (rule.Events.Count == 0) errors.Add($"{name}未选择触发事件");
            foreach (var evt in rule.Events.Where(e => !allowedEvents.Contains(e)))
                errors.Add($"{name}的事件 {evt} 不适用于该节点");

            if (rule.Recipients.Count == 0) errors.Add($"{name}未配置接收人");
            foreach (var recipient in rule.Recipients.Where(r =>
                         !context.ApproverTypes.Contains(r.Type) &&
                         !NotificationDefaults.NotificationRecipientTypes.Contains(r.Type, StringComparer.OrdinalIgnoreCase)))
                errors.Add($"{name}使用了未注册的接收人类型 '{recipient.Type}'");

            if (rule.Channels.Count == 0) errors.Add($"{name}未选择发送渠道");
            if (context.Channels is not null)
            {
                foreach (var channel in rule.Channels.Where(c => !context.Channels.Contains(c)))
                    errors.Add($"{name}使用了未注册的渠道 '{channel}'");
            }
        }
    }

    private static void ValidateTimeout(List<string> errors, string key, FlowNode node, TimeoutPolicy timeout)
    {
        if (node.Type != NodeType.Approval) errors.Add($"节点 '{key}' 不是审批节点，不能设置时限");
        if (timeout.Minutes <= 0) errors.Add($"节点 '{key}' 的时限必须大于 0 分钟");
        if (timeout.RepeatMinutes is <= 0) errors.Add($"节点 '{key}' 的催办间隔必须大于 0 分钟");
        if (timeout.MaxReminders < 1) errors.Add($"节点 '{key}' 的最多提醒次数至少为 1");
    }

    private static void ValidateAction(List<string> errors, string name, ActionStep step, FlowValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(step.Type))
        {
            errors.Add($"{name}未选择动作类型");
            return;
        }
        if (!string.IsNullOrWhiteSpace(step.OutputVariable))
        {
            if (step.Mode == ActionMode.Async) errors.Add($"{name}是异步动作，不能设置输出变量");
            if (!Identifier().IsMatch(step.OutputVariable)) errors.Add($"{name}的输出变量名 '{step.OutputVariable}' 只能包含字母、数字和下划线");
        }

        if (context.Actions is null) return;
        if (!context.Actions.TryGetValue(step.Type, out var handler))
        {
            errors.Add($"{name}使用了未注册的动作类型 '{step.Type}'");
            return;
        }

        var descriptor = handler.Descriptor;
        if (step.Mode == ActionMode.Async && !descriptor.SupportsAsync) errors.Add($"{name}（{descriptor.DisplayName}）不支持异步执行");
        if (step.Mode == ActionMode.Sync && !descriptor.SupportsSync) errors.Add($"{name}（{descriptor.DisplayName}）不支持同步执行");

        var parameters = new Dictionary<string, string>(step.Parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in descriptor.Parameters.Where(p => p.Required && p.DefaultValue is null))
        {
            if (!parameters.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value))
                errors.Add($"{name}缺少必填参数「{parameter.Label}」");
        }
        errors.AddRange(handler.ValidateParameters(parameters).Select(e => $"{name}：{e}"));
    }

    private static void CheckReachabilityAndCycles(FlowDefinition definition, List<string> errors)
    {
        // 0 = 未访问, 1 = 访问中, 2 = 已完成；DFS 中遇到“访问中”的节点即存在环
        var state = new Dictionary<string, int>();

        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var s))
            {
                if (s == 2) return true;
                errors.Add($"流程存在环路（回到节点 '{id}'）");
                return false;
            }
            state[id] = 1;
            if (FlowStructure.Successors(definition.Nodes[id]).Any(next => !Visit(next))) return false;
            state[id] = 2;
            return true;
        }

        if (!Visit(definition.StartNodeId)) return;

        foreach (var unreachable in definition.Nodes.Keys.Where(k => !state.ContainsKey(k)))
            errors.Add($"节点 '{unreachable}' 从开始节点不可达");
    }

}
