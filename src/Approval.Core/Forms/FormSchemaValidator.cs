using System.Text.Json;
using System.Text.RegularExpressions;
using Approval.Core.Definitions;
using Approval.Core.Templates;

namespace Approval.Core.Forms;

/// <summary>发布时校验表单结构与发起设置。</summary>
public static partial class FormSchemaValidator
{
    public const int MaxFields = 100;
    public const int MaxOptions = 200;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    private static readonly HashSet<FormFieldType> OptionTypes = [FormFieldType.Select, FormFieldType.MultiSelect, FormFieldType.Radio];
    private static readonly HashSet<FormFieldType> NumberTypes = [FormFieldType.Number, FormFieldType.Money];

    private static readonly HashSet<FormFieldType> ColumnTypes =
        [FormFieldType.Text, FormFieldType.Number, FormFieldType.Money, FormFieldType.Date, FormFieldType.Select, FormFieldType.Formula];

    /// <summary>标题模板中可用的非表单变量。</summary>
    public static readonly IReadOnlySet<string> TitleVariables =
        new HashSet<string>(["流程名", "flowName", "申请人", "initiator", "发起时间", "createdAt"], StringComparer.OrdinalIgnoreCase);

    public static void Validate(FlowDefinition definition, List<string> errors)
    {
        var form = definition.Form;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (form is not null)
        {
            if (form.Fields.Count > MaxFields) errors.Add($"表单字段不能超过 {MaxFields} 个");
            foreach (var field in form.Fields)
            {
                ValidateField(errors, field, "表单", isColumn: false);
                if (field.Type == FormFieldType.Description) continue;
                if (!string.IsNullOrEmpty(field.Key) && !keys.Add(field.Key)) errors.Add($"表单字段标识 '{field.Key}' 重复");
                if (field.Type is FormFieldType.User or FormFieldType.Users) userFields.Add(field.Key);
            }

            // 明细合计字段写入顶层，不能与普通字段同名
            var sumKeys = new List<string>();
            foreach (var table in form.Fields.Where(f => f.Type == FormFieldType.Table))
            {
                foreach (var column in table.Columns.Where(c => c.Sum))
                {
                    var sumKey = FormField.SumKey(table.Key, column.Key);
                    if (!keys.Add(sumKey)) errors.Add($"合计字段 '{sumKey}' 与已有字段重名");
                    sumKeys.Add(sumKey);
                }
                // 明细列的公式按行计算，只能引用本行的数值列
                FormulaPlan.Build(table.Columns, [], $"明细表「{table.Label}」的列", errors);
            }

            // 计算字段：公式语法、引用（数值字段、明细合计、其他计算字段）、循环引用
            FormulaPlan.Build(form.Fields, sumKeys, "表单", errors);

            // 审批人 / 通知接收人按表单字段取人时，字段必须是人员类型
            var rules = definition.Nodes.Values
                .SelectMany(n => n.Approvers.Select(a => (Node: n.Name, Rule: a))
                    .Concat(n.Notifications.SelectMany(r => r.Recipients).Select(a => (Node: n.Name, Rule: a))))
                .Concat(definition.Notifications.SelectMany(r => r.Recipients).Select(a => (Node: "流程级通知", Rule: a)))
                .Where(x => string.Equals(x.Rule.Type, "FormField", StringComparison.OrdinalIgnoreCase));
            foreach (var (node, rule) in rules.Where(x => !userFields.Contains(x.Rule.Value)).DistinctBy(x => (x.Node, x.Rule.Value)))
                errors.Add($"「{node}」按表单字段 '{rule.Value}' 取人，但表单中没有该人员字段（User / Users）");
        }

        if (definition.Settings is { } settings) ValidateSettings(errors, settings, keys, form is not null);
    }

    private static void ValidateSettings(List<string> errors, FlowSettings settings, HashSet<string> keys, bool hasForm)
    {
        if (settings.InitiatorRoles.Any(string.IsNullOrWhiteSpace)) errors.Add("发起设置中的发起角色不能为空");
        if (settings.Category is { Length: > 50 }) errors.Add("发起设置中的分类不能超过 50 个字符");
        if (settings.Description is { Length: > 500 }) errors.Add("发起设置中的描述不能超过 500 个字符");
        if (settings.TitleTemplate is { Length: > 200 }) errors.Add("标题模板不能超过 200 个字符");

        foreach (var name in TemplateRenderer.VariableNames(settings.TitleTemplate).Distinct())
        {
            var segments = name.Split('.');
            var isForm = segments[0] is "form" or "表单";
            if (isForm && segments.Length == 2 && hasForm && keys.Contains(segments[1])) continue;
            if (!isForm && segments.Length == 1 && TitleVariables.Contains(name)) continue;
            errors.Add($"标题模板中的变量 {{{name}}} 不存在");
        }
    }

    private static void ValidateField(List<string> errors, FormField field, string owner, bool isColumn)
    {
        var name = $"{owner}字段「{(string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label)}」";

        if (string.IsNullOrWhiteSpace(field.Label)) errors.Add($"{owner}字段 '{field.Key}' 缺少名称");
        if (field.Type != FormFieldType.Description && !Identifier().IsMatch(field.Key))
            errors.Add($"{name}的标识 '{field.Key}' 只能包含字母、数字和下划线，且不能以数字开头");
        if (field.Span is < 1 or > 24) errors.Add($"{name}的宽度必须在 1 到 24 之间");
        if (isColumn && !ColumnTypes.Contains(field.Type)) errors.Add($"{name}：明细列不支持类型 {field.Type}");

        if (OptionTypes.Contains(field.Type))
        {
            if (field.Options.Count == 0) errors.Add($"{name}没有选项");
            if (field.Options.Count > MaxOptions) errors.Add($"{name}的选项不能超过 {MaxOptions} 个");
            if (field.Options.Any(o => string.IsNullOrWhiteSpace(o.Value) || string.IsNullOrWhiteSpace(o.Label)))
                errors.Add($"{name}有空的选项");
            if (field.Options.Select(o => o.Value).Distinct().Count() != field.Options.Count) errors.Add($"{name}有重复的选项值");
        }

        if (NumberTypes.Contains(field.Type))
        {
            if (field.Min > field.Max) errors.Add($"{name}的最小值不能大于最大值");
            if (field.Precision is < 0 or > 6) errors.Add($"{name}的小数位数必须在 0 到 6 之间");
        }
        else if (field.Sum && !FormField.IsNumeric(field.Type)) errors.Add($"{name}不是数值列，不能合计");

        if (field.Type == FormFieldType.Formula)
        {
            // 值由公式计算：不能填写，因此不支持必填、默认值与范围
            if (string.IsNullOrWhiteSpace(field.Formula)) errors.Add($"{name}没有填写公式");
            if (field.Precision is < 0 or > 6) errors.Add($"{name}的小数位数必须在 0 到 6 之间");
            if (field.Required || field.DefaultValue is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } || field.Min is not null
                || field.Max is not null)
                errors.Add($"{name}是计算字段，不能设置必填、默认值或范围");
            return;
        }
        if (!string.IsNullOrWhiteSpace(field.Formula)) errors.Add($"{name}不是计算字段，不能设置公式");

        if (field.MaxLength is < 1 or > FormDataValidator.MaxTextLength)
            errors.Add($"{name}的最大长度必须在 1 到 {FormDataValidator.MaxTextLength} 之间");

        if (field.Type == FormFieldType.Table)
        {
            if (field.MaxRows is < 1 or > 500) errors.Add($"{name}的最多行数必须在 1 到 500 之间");
            if (field.Columns.Count == 0) errors.Add($"{name}没有列");
            if (field.Columns.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != field.Columns.Count)
                errors.Add($"{name}有重复的列标识");
            foreach (var column in field.Columns) ValidateField(errors, column, $"{name}的列", isColumn: true);
            if (field.DefaultValue is not null) errors.Add($"{name}不支持默认值");
            return;
        }
        if (field.Columns.Count > 0) errors.Add($"{name}不是明细表，不能配置列");

        if (field.Type == FormFieldType.Description)
        {
            if (field.Required) errors.Add($"{name}是说明文字，不能设为必填");
            return;
        }

        // 默认值必须能通过字段自身的校验（必填除外）
        if (field.DefaultValue is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } defaultValue)
        {
            var defaultErrors = new Dictionary<string, List<string>>();
            FormDataValidator.Normalize(new FormField
            {
                Key = field.Key, Label = field.Label, Type = field.Type, Options = field.Options, Min = field.Min, Max = field.Max,
                Precision = field.Precision, MaxLength = field.MaxLength
            }, defaultValue, field.Key, defaultErrors);
            if (defaultErrors.Count > 0) errors.Add($"{name}的默认值无效：{string.Join("；", defaultErrors.Values.SelectMany(e => e))}");
        }
    }
}
