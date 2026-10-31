using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Conditions;

namespace Approval.Core.Forms;

/// <summary>表单数据校验结果：<see cref="Json"/> 为规范化后的数据，<see cref="Errors"/> 为“字段路径 → 错误”。</summary>
public sealed record FormValidationResult(string Json, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// 按 <see cref="FormSchema"/> 校验并规范化表单数据（服务端不信任前端）：
/// 去掉未定义的字段、转换类型（数字字符串 → 数字）、检查必填 / 选项 / 范围 / 长度 / 行数，并计算明细合计。
/// 未填写的可选字段写为 null（数组类为空数组、开关为 false），保证条件表达式中变量始终存在。
/// 明细单元格的错误路径为 <c>表格.行号.列</c>（如 <c>items.0.amount</c>），与前端表单项的 prop 一致。
/// </summary>
public static class FormDataValidator
{
    public const int MaxTextLength = 4000;
    public const int DefaultMaxRows = 100;
    public const int MaxUsers = 100;
    private const string DateFormat = "yyyy-MM-dd";

    public static FormValidationResult Validate(FormSchema schema, JsonElement data)
    {
        var (output, errors) = NormalizeAll(schema, data);
        return new FormValidationResult(
            output.ToJsonString(FormData.JsonOptions),
            errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    /// <summary>
    /// 宽松规范化（草稿）：不检查必填、范围等规则，只保证结构正确——剔除未定义字段，类型不对的值丢弃（置空）。
    /// </summary>
    public static string NormalizeLenient(FormSchema schema, JsonElement data) =>
        NormalizeAll(schema, data).Output.ToJsonString(FormData.JsonOptions);

    private static (JsonObject Output, Dictionary<string, List<string>> Errors) NormalizeAll(FormSchema schema, JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            data = JsonDocument.Parse("{}").RootElement;
        if (data.ValueKind != JsonValueKind.Object) throw new ApprovalException("表单数据必须是 JSON 对象");

        // 字段名不区分大小写（与条件表达式变量一致）
        var input = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in data.EnumerateObject()) input[property.Name] = property.Value;

        var errors = new Dictionary<string, List<string>>();
        var output = new JsonObject();
        foreach (var field in schema.Fields.Where(f => f.Type != FormFieldType.Description))
        {
            JsonElement? value = input.TryGetValue(field.Key, out var v) ? v : null;
            output[field.Key] = Normalize(field, value, field.Key, errors);
            if (field.Type == FormFieldType.Table) AddSums(field, output);
        }
        return (output, errors);
    }

    /// <summary>校验失败时抛出 <see cref="ApprovalValidationException"/>，否则返回规范化后的 JSON。</summary>
    public static string ValidateOrThrow(FormSchema schema, JsonElement data)
    {
        var result = Validate(schema, data);
        return result.IsValid ? result.Json : throw new ApprovalValidationException(result.Errors, "表单填写有误，请检查标红的字段");
    }

    /// <summary>校验单个字段的值（发布时校验默认值也使用此方法）。</summary>
    internal static JsonNode? Normalize(FormField field, JsonElement? value, string path, Dictionary<string, List<string>> errors)
    {
        void Error(string message)
        {
            if (!errors.TryGetValue(path, out var list)) errors[path] = list = [];
            list.Add(message);
        }

        if (field.Type == FormFieldType.Switch)
        {
            var on = value?.ValueKind == JsonValueKind.True;
            if (value is { ValueKind: not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null) }) Error($"{field.Label}格式不正确");
            else if (field.Required && !on) Error($"请勾选{field.Label}");
            return JsonValue.Create(on);
        }

        if (IsEmpty(value))
        {
            if (field.Required) Error(RequiredMessage(field));
            return field.Type is FormFieldType.MultiSelect or FormFieldType.Users or FormFieldType.Table ? new JsonArray() : null;
        }

        var element = value!.Value;
        switch (field.Type)
        {
            case FormFieldType.Text or FormFieldType.TextArea:
            {
                if (element.ValueKind != JsonValueKind.String) { Error($"{field.Label}应为文本"); return null; }
                var text = element.GetString()!.Trim();
                var limit = Math.Min(field.MaxLength ?? (field.Type == FormFieldType.Text ? 200 : 2000), MaxTextLength);
                if (text.Length > limit) Error($"{field.Label}不能超过 {limit} 个字符");
                return JsonValue.Create(text);
            }

            case FormFieldType.Number or FormFieldType.Money:
            {
                if (!TryGetDecimal(element, out var number)) { Error($"{field.Label}应为数字"); return null; }
                var precision = field.Precision ?? (field.Type == FormFieldType.Money ? 2 : null);
                if (precision is { } p && decimal.Round(number, p) != number)
                    Error(p == 0 ? $"{field.Label}应为整数" : $"{field.Label}最多 {p} 位小数");
                if (field.Min is { } min && number < min) Error($"{field.Label}不能小于 {min.ToString(CultureInfo.InvariantCulture)}");
                if (field.Max is { } max && number > max) Error($"{field.Label}不能大于 {max.ToString(CultureInfo.InvariantCulture)}");
                return JsonValue.Create(number);
            }

            case FormFieldType.Date:
                if (!TryGetDate(element, out var date)) { Error($"{field.Label}应为日期（{DateFormat}）"); return null; }
                return JsonValue.Create(date);

            case FormFieldType.DateRange:
            {
                if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2
                    || !TryGetDate(element[0], out var from) || !TryGetDate(element[1], out var to))
                {
                    Error($"{field.Label}应为开始和结束日期");
                    return null;
                }
                if (string.CompareOrdinal(from, to) > 0) Error($"{field.Label}的开始日期不能晚于结束日期");
                return new JsonArray(from, to);
            }

            case FormFieldType.Select or FormFieldType.Radio:
                if (element.ValueKind != JsonValueKind.String || field.Options.All(o => o.Value != element.GetString()))
                {
                    Error($"{field.Label}的选项无效");
                    return null;
                }
                return JsonValue.Create(element.GetString());

            case FormFieldType.MultiSelect:
            {
                var values = StringArray(element);
                if (values is null || values.Any(v => field.Options.All(o => o.Value != v))) { Error($"{field.Label}的选项无效"); return new JsonArray(); }
                return ToArray(values);
            }

            case FormFieldType.User:
                if (element.ValueKind != JsonValueKind.String || element.GetString()!.Trim() is not { Length: > 0 and <= 100 } user)
                {
                    Error($"{field.Label}应为一个用户");
                    return null;
                }
                return JsonValue.Create(user);

            case FormFieldType.Users:
            {
                var users = StringArray(element);
                if (users is null || users.Any(u => u.Length > 100)) { Error($"{field.Label}应为用户列表"); return new JsonArray(); }
                if (users.Count > MaxUsers) Error($"{field.Label}最多选择 {MaxUsers} 人");
                return ToArray(users);
            }

            case FormFieldType.Table:
                return NormalizeTable(field, element, path, errors, Error);

            default:
                return null;
        }
    }

    private static JsonArray NormalizeTable(FormField field, JsonElement element, string path, Dictionary<string, List<string>> errors,
        Action<string> error)
    {
        var rows = new JsonArray();
        if (element.ValueKind != JsonValueKind.Array) { error($"{field.Label}格式不正确"); return rows; }

        var maxRows = field.MaxRows ?? DefaultMaxRows;
        if (element.GetArrayLength() > maxRows) { error($"{field.Label}最多 {maxRows} 行"); return rows; }

        var index = 0;
        foreach (var row in element.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) { error($"{field.Label}第 {index + 1} 行格式不正确"); return new JsonArray(); }

            var cells = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in row.EnumerateObject()) cells[property.Name] = property.Value;

            var normalized = new JsonObject();
            foreach (var column in field.Columns)
            {
                JsonElement? cell = cells.TryGetValue(column.Key, out var c) ? c : null;
                normalized[column.Key] = Normalize(column, cell, $"{path}.{index}.{column.Key}", errors);
            }
            rows.Add(normalized);
            index++;
        }

        if (field.Required && rows.Count == 0) error($"请至少添加一行{field.Label}");
        return rows;
    }

    private static void AddSums(FormField table, JsonObject output)
    {
        var rows = output[table.Key] as JsonArray ?? [];
        foreach (var column in table.Columns.Where(c => c.Sum))
        {
            var sum = rows.OfType<JsonObject>()
                .Select(r => r[column.Key] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : 0m)
                .Sum();
            output[FormField.SumKey(table.Key, column.Key)] = JsonValue.Create(sum);
        }
    }

    private static string RequiredMessage(FormField field) => field.Type switch
    {
        FormFieldType.Select or FormFieldType.MultiSelect or FormFieldType.Radio or FormFieldType.User or FormFieldType.Users
            or FormFieldType.Date or FormFieldType.DateRange => $"请选择{field.Label}",
        FormFieldType.Table => $"请至少添加一行{field.Label}",
        _ => $"请填写{field.Label}"
    };

    private static bool IsEmpty(JsonElement? value) => value is not { } v || v.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()),
        JsonValueKind.Array => v.GetArrayLength() == 0,
        _ => false
    };

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryGetDate(JsonElement element, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.String) return false;
        if (!DateOnly.TryParseExact(element.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        value = date.ToString(DateFormat, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>字符串数组（去空白、去重）；格式不对返回 null。</summary>
    private static List<string>? StringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return null;
        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return null;
            var text = item.GetString()!.Trim();
            if (text.Length > 0 && !result.Contains(text)) result.Add(text);
        }
        return result;
    }

    private static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}
