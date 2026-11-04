using System.Text.RegularExpressions;
using Approval.Core.Abstractions;
using Approval.Core.Notifications;
using Approval.Core.Resolvers;
using Approval.Core.Templates;

namespace Approval.Core.Definitions;

/// <summary>校验所需的已注册扩展。为 null 的集合不做对应检查。</summary>
public sealed class FlowValidationContext
{
    public IApprovalDefinitionExtension? Extension { get; init; }
    public required IReadOnlySet<string> ApproverTypes { get; init; }

    /// <summary>审批人类型描述（参数类型、是否有序），用于校验规则参数与审批方式。</summary>
    public IReadOnlyDictionary<string, ApproverTypeDescriptor>? ApproverDescriptors { get; init; }

    /// <summary>宿主声明的通知路由（含内置站内信 InApp）；名称保留以兼容现有契约。</summary>
    public IReadOnlySet<string>? Channels { get; init; }

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
        if (definition.SchemaVersion != FlowDefinition.CurrentSchemaVersion)
            return [$"不支持流程协议版本 {definition.SchemaVersion}，当前支持 v{FlowDefinition.CurrentSchemaVersion}"];
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

        var userFields = context.Extension?.UserFields(definition);
        ValidateNotifications(errors, "流程级通知", definition.Notifications, null, context, userFields);
        if (context.Extension is { } extension) extension.Validate(definition, errors);
        else if (definition.Form is not null || nodes.Values.Any(n => n.FieldPermissions.Count > 0))
            errors.Add("此定义包含模块快照或字段权限，请注册拥有它的可选模块");

        foreach (var (key, node) in nodes)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || node.Name.Length > 200)
                errors.Add($"节点 '{key}' 的标识或名称长度无效");
            if (!Enum.IsDefined(node.Type) || !Enum.IsDefined(node.Mode) || !Enum.IsDefined(node.EmptyApproverPolicy))
                errors.Add($"节点 '{key}' 包含未知节点类型或审批策略");
            if (node.Id != key)
                errors.Add($"节点 key '{key}' 与其 Id '{node.Id}' 不一致");

            switch (node.Type)
            {
                case NodeType.End:
                    if (node.Next is not null) errors.Add($"End 节点 '{key}' 不能有 Next");
                    break;

                case NodeType.Condition:
                    foreach (var branch in node.Branches.Where(b => !string.IsNullOrWhiteSpace(b.Expression)))
                        if (Conditions.DynamicExpressoConditionEvaluator.ValidateSyntax(branch.Expression!) is { } syntax)
                            errors.Add($"条件节点 '{key}' 的表达式无效：{syntax}");
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
                    {
                        var noun = node.Type == NodeType.Cc ? "抄送人" : "审批人";
                        if (node.Approvers.Count == 0)
                            errors.Add($"节点 '{key}' 未配置{noun}");
                        foreach (var rule in node.Approvers.Where(r => !context.ApproverTypes.Contains(r.Type)))
                            errors.Add($"节点 '{key}' 使用了未注册的审批人类型 '{rule.Type}'");
                        foreach (var rule in node.Approvers) ValidateRuleValue(errors, $"节点 '{key}' 的{noun}", rule, context, userFields);

                        // 逐级审批等有序审批人按顺序生成任务，只有依次审批才有意义
                        var ordered = node.Approvers.Select(r => context.ApproverDescriptors?.GetValueOrDefault(r.Type)).FirstOrDefault(d => d?.Ordered == true);
                        if (node.Type == NodeType.Approval && ordered is not null && node.Mode != ApproveMode.Sequential)
                            errors.Add($"节点 '{key}' 的审批人「{ordered.DisplayName}」需按顺序审批，审批方式请改为依次审批");
                        goto default;
                    }
                default:
                    if (node.Next is null) errors.Add($"节点 '{key}' 缺少 Next");
                    else if (!nodes.ContainsKey(node.Next)) errors.Add($"节点 '{key}' 的 Next 指向不存在的节点 '{node.Next}'");
                    break;
            }

            ValidateNotifications(errors, $"节点 '{key}'", node.Notifications, node.Type, context, userFields);
            if (node.Timeout is { } timeout) ValidateTimeout(errors, key, node, timeout);
            if (node.RequireQualifiedAssignee && (node.Type != NodeType.Approval
                || node.EmptyApproverPolicy == EmptyApproverPolicy.AutoApprove || node.Timeout?.Action == TimeoutAction.AutoApprove))
                errors.Add($"节点 '{key}' 的岗位资格要求只能用于人工审批，不能设置自动通过");
        }

        if (errors.Count == 0) CheckReachabilityAndCycles(definition, errors);
        if (errors.Count == 0) errors.AddRange(FlowStructure.Analyze(definition).Errors);
        return errors;
    }

    /// <summary>按审批人类型描述校验参数：用户 / 角色不能为空，级数为 1–10，<c>@字段</c> 须为表单人员字段。</summary>
    private static void ValidateRuleValue(List<string> errors, string owner, ApproverRule rule, FlowValidationContext context,
        IReadOnlySet<string>? userFields)
    {
        if (context.ApproverDescriptors?.GetValueOrDefault(rule.Type) is not { } descriptor) return;

        var name = $"{owner}「{descriptor.DisplayName}」";
        switch (descriptor.ValueKind)
        {
            case ApproverValueKind.User or ApproverValueKind.Role when string.IsNullOrWhiteSpace(rule.Value):
            case ApproverValueKind.FormUserField when string.IsNullOrWhiteSpace(rule.Value) && userFields is null:
                errors.Add($"{name}未设置");
                break;

            case ApproverValueKind.Level:
                if (!ApproverLevel.TryParse(rule.Value, out var level))
                    errors.Add($"{name}的级数 '{rule.Value}' 无效：应为 1–{ApproverLevel.Max} 的整数，可写 N@表单人员字段");
                else if (level.Field is { } field && userFields is not null && !userFields.Contains(field))
                    errors.Add($"{name}以表单字段 '{field}' 为基准，但表单中没有该人员字段（User / Users）");
                break;
        }
    }

    private static void ValidateNotifications(List<string> errors, string owner, List<NotificationRule> rules, NodeType? nodeType,
        FlowValidationContext context, IReadOnlySet<string>? userFields)
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
            foreach (var recipient in rule.Recipients) ValidateRuleValue(errors, $"{name}的接收人", recipient, context, userFields);

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
