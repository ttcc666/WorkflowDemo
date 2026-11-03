using System.Text.Json.Nodes;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Forms;

/// <summary>审批中修改表单的一个字段：修改前后的值。修改轨迹的 <see cref="ApprovalLog.Details"/> 为这些记录的 JSON 数组。</summary>
public sealed record FormFieldChange(string Field, string Label, JsonNode? Before, JsonNode? After);

/// <summary>
/// 节点字段权限（<see cref="FlowNode.FieldPermissions"/>）的运行时规则：审批节点可编辑的字段、参与人看不到的字段，
/// 以及按隐藏字段过滤表单数据与修改轨迹。
/// </summary>
internal static class FieldVisibility
{
    public static IReadOnlySet<string> None { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>审批节点可在同意时修改的字段（按表单顺序；计算字段与说明文字除外）。</summary>
    public static List<FormField> EditableFields(FlowDefinition flow, string nodeId)
    {
        if (flow.Form is null || !flow.Nodes.TryGetValue(nodeId, out var node) || node.Type != NodeType.Approval) return [];
        var keys = node.FieldsWith(FieldAccess.Editable);
        return flow.Form.Fields.Where(f => keys.Contains(f.Key) && f.Type is not (FormFieldType.Formula or FormFieldType.Description)).ToList();
    }

    /// <summary>
    /// 参与人看不到的字段：在其参与的节点（处理人 / 候选人 / 抄送人所在节点）都设为隐藏的字段；
    /// 被隐藏的明细表的合计字段一并隐藏。没有参与节点时不隐藏。
    /// </summary>
    public static IReadOnlySet<string> HiddenFields(FlowDefinition flow, IEnumerable<string> nodes)
    {
        HashSet<string>? hidden = null;
        foreach (var nodeId in nodes)
        {
            var fields = flow.Nodes.TryGetValue(nodeId, out var node) ? node.FieldsWith(FieldAccess.Hidden) : None;
            if (hidden is null) hidden = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
            else hidden.IntersectWith(fields);
            if (hidden.Count == 0) return None;
        }
        if (hidden is null) return None;

        var tables = flow.Form?.Fields.Where(f => f.Type == FormFieldType.Table && hidden.Contains(f.Key)).ToList() ?? [];
        foreach (var table in tables)
        {
            foreach (var column in table.Columns.Where(c => c.Sum)) hidden.Add(FormField.SumKey(table.Key, column.Key));
        }
        return hidden;
    }

    /// <summary>去掉表单数据中的隐藏字段。</summary>
    public static string RemoveFields(string formJson, IReadOnlySet<string> hidden)
    {
        if (hidden.Count == 0 || JsonNode.Parse(formJson) is not JsonObject form) return formJson;
        foreach (var key in form.Select(p => p.Key).Where(hidden.Contains).ToList()) form.Remove(key);
        return form.ToJsonString(FormData.JsonOptions);
    }

    /// <summary>修改表单的轨迹去掉隐藏字段的差异并按剩余字段重写轨迹文本；修改的字段全部被隐藏时不返回该轨迹。</summary>
    public static IReadOnlyList<ApprovalLog> FilterLogs(IReadOnlyList<ApprovalLog> logs, IReadOnlySet<string> hidden)
    {
        if (hidden.Count == 0) return logs;

        var result = new List<ApprovalLog>(logs.Count);
        foreach (var log in logs)
        {
            if (log.Action != ApprovalAction.FormEdit || string.IsNullOrEmpty(log.Details) || JsonNode.Parse(log.Details) is not JsonArray changes)
            {
                result.Add(log);
                continue;
            }

            var visible = changes.Where(c => FieldOf(c) is not { } field || !hidden.Contains(field)).ToList();
            if (visible.Count == changes.Count) result.Add(log);
            else if (visible.Count > 0)
                result.Add(new ApprovalLog
                {
                    TaskId = log.TaskId,
                    Id = log.Id,
                    InstanceId = log.InstanceId,
                    Sequence = log.Sequence,
                    NodeId = log.NodeId,
                    NodeName = log.NodeName,
                    Action = log.Action,
                    OperatorId = log.OperatorId,
                    Comment = Summary(visible.Select(c => (c as JsonObject)?["label"]?.GetValue<string>() ?? FieldOf(c) ?? "")),
                    Details = new JsonArray(visible.Select(c => c?.DeepClone()).ToArray()).ToJsonString(FormData.JsonOptions),
                    CreatedAt = log.CreatedAt
                });
        }
        return result;
    }

    /// <summary>修改表单的轨迹文本：只写字段名，不含值（值在 Details 中，按查看者过滤）。</summary>
    public static string Summary(IEnumerable<string> labels) => "修改了 " + string.Join("、", labels);

    /// <summary>两份规范化后的表单数据的差异：表单字段（按表单顺序）与明细合计字段。</summary>
    public static List<FormFieldChange> Diff(FormSchema schema, string beforeJson, string afterJson)
    {
        var before = JsonNode.Parse(beforeJson) as JsonObject ?? new JsonObject();
        var after = JsonNode.Parse(afterJson) as JsonObject ?? new JsonObject();
        var changes = new List<FormFieldChange>();

        void Compare(string key, string label)
        {
            var (a, b) = (before[key], after[key]);
            if (!JsonNode.DeepEquals(a, b)) changes.Add(new FormFieldChange(key, label, a?.DeepClone(), b?.DeepClone()));
        }

        foreach (var field in schema.Fields.Where(f => f.Type != FormFieldType.Description))
        {
            Compare(field.Key, field.Label);
            if (field.Type != FormFieldType.Table) continue;
            foreach (var column in field.Columns.Where(c => c.Sum))
                Compare(FormField.SumKey(field.Key, column.Key), $"{field.Label}合计（{column.Label}）");
        }
        return changes;
    }

    private static string? FieldOf(JsonNode? change) =>
        change is JsonObject o && o["field"] is JsonValue v && v.TryGetValue<string>(out var field) ? field : null;
}
