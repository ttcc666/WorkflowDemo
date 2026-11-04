using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Templates;
namespace Approval.Forms.Schema;
public sealed class FormDefinitionValidator : IApprovalDefinitionExtension
{
    public IReadOnlySet<string>? UserFields(FlowDefinition definition) => definition.GetForm()?.Fields
        .Where(f => f.Type is FormFieldType.User or FormFieldType.Users).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public void Validate(FlowDefinition definition, List<string> errors)
    {
        FormSchemaValidator.Validate(definition, errors);
        foreach (var (key, node) in definition.Nodes)
            if (node.FieldPermissions.Count > 0) ValidateFieldPermissions(errors, key, node, definition.GetForm());
    }
    public static IReadOnlyList<string> Validate(FlowDefinition definition, IReadOnlySet<string> types) =>
        Approval.Core.Definitions.FlowDefinitionValidator.Validate(definition, new FlowValidationContext { ApproverTypes = types, Extension = new FormDefinitionValidator() });
    /// <summary>字段权限：审批节点可设可编辑 / 隐藏，抄送节点只能设隐藏；字段须为表单字段（不区分大小写、不能重复），计算字段不能设为可编辑。</summary>
    private static void ValidateFieldPermissions(List<string> errors, string key, FlowNode node, FormSchema? form)
    {
        if (node.Type is not (NodeType.Approval or NodeType.Cc))
        {
            errors.Add($"节点 '{key}' 不是审批或抄送节点，不能设置字段权限");
            return;
        }
        if (form is not { Fields.Count: > 0 })
        {
            errors.Add($"节点 '{key}' 设置了字段权限，但流程没有表单");
            return;
        }

        var fields = new Dictionary<string, FormField>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in form.Fields.Where(f => f.Type != FormFieldType.Description)) fields.TryAdd(field.Key, field);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, access) in node.FieldPermissions)
        {
            if (!seen.Add(name)) errors.Add($"节点 '{key}' 的字段权限中字段 '{name}' 重复");
            if (!Enum.IsDefined(access)) errors.Add($"节点 '{key}' 的字段 '{name}' 的权限值无效");
            if (!fields.TryGetValue(name, out var field))
            {
                errors.Add($"节点 '{key}' 的字段权限中，表单没有字段 '{name}'");
                continue;
            }
            if (access != FieldAccess.Editable) continue;
            if (node.Type == NodeType.Cc) errors.Add($"抄送节点 '{key}' 不能把字段「{field.Label}」设为可编辑");
            else if (field.Type == FormFieldType.Formula) errors.Add($"节点 '{key}' 的字段「{field.Label}」是计算字段，不能设为可编辑");
        }
    }

    /// <summary>
    /// 不阻止发布的提示：节点隐藏的字段可能通过其他途径间接暴露——标题对所有参与人可见、通知按模板发给接收人、
    /// 计算字段的结果在该节点仍可见。
    /// </summary>
    public IReadOnlyList<string> Warnings(FlowDefinition definition)
    {
        var warnings = new List<string>();
        if (definition.GetForm() is not { } form) return warnings;

        // 字段 → 隐藏它的节点（含被隐藏明细表的合计字段）
        var hiddenAt = new Dictionary<string, List<FlowNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in definition.Nodes.Values)
        {
            foreach (var field in FieldVisibility.HiddenFields(definition, [node.Id]))
            {
                if (!hiddenAt.TryGetValue(field, out var nodes)) hiddenAt[field] = nodes = [];
                nodes.Add(node);
            }
        }
        if (hiddenAt.Count == 0) return warnings;

        string NodesOf(string field) => string.Join("、", hiddenAt[field].Select(n => $"「{n.Name}」"));

        void CheckTemplates(string owner, string consequence, params string?[] templates)
        {
            var fields = templates.SelectMany(TemplateRenderer.VariableNames)
                .Select(name => name.Split('.'))
                .Where(s => s.Length >= 2 && s[0] is "form" or "表单" && hiddenAt.ContainsKey(s[1]))
                .Select(s => s[1])
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields) warnings.Add($"{owner}引用了字段 '{field}'，它在节点{NodesOf(field)}被隐藏，{consequence}");
        }

        CheckTemplates("标题模板", "但标题对所有参与人可见", definition.Settings?.TitleTemplate);
        foreach (var rule in definition.Notifications) CheckTemplates("流程级通知", "接收人仍会在消息中看到", rule.Title, rule.Content);
        foreach (var node in definition.Nodes.Values)
        {
            foreach (var rule in node.Notifications) CheckTemplates($"节点「{node.Name}」的通知", "接收人仍会在消息中看到", rule.Title, rule.Content);
        }

        foreach (var field in form.Fields.Where(f => f.Type == FormFieldType.Formula))
        {
            if (!Formula.TryParse(field.Formula, out var formula, out _)) continue;
            foreach (var reference in formula!.Identifiers.Where(hiddenAt.ContainsKey))
            {
                foreach (var node in hiddenAt[reference].Where(n => !n.FieldsWith(FieldAccess.Hidden).Contains(field.Key)))
                    warnings.Add($"计算字段「{field.Label}」引用了 '{reference}'，它在节点「{node.Name}」被隐藏，但计算结果在该节点仍可见");
            }
        }
        return warnings;
    }

}
