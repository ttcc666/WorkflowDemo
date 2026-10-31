using System.Collections;
using System.Dynamic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Approval.Core.Templates;

/// <summary>占位符的默认编码方式，可在占位符中用 <c>{标题|json}</c>、<c>{标题|url}</c>、<c>{标题|raw}</c> 覆盖。</summary>
public enum TemplateEncoding { Raw, Json, Url }

/// <summary>
/// 文本模板：把 <c>{变量名}</c> 替换为变量值。变量名可以是中文别名（如 <c>{标题}</c>）、
/// 英文名（如 <c>{title}</c>）或表单字段（<c>{form.amount}</c>、<c>{表单.amount}</c>，支持多级）。
/// 未知变量原样保留，便于发现拼写错误；JSON 等非占位符的花括号不受影响。
/// </summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{([\p{L}\p{N}_]+(?:\.[\p{L}\p{N}_]+)*)(?:\|(raw|json|url))?\}")]
    private static partial Regex Placeholder();

    public static string Render(string? template, IReadOnlyDictionary<string, object?> variables,
        TemplateEncoding encoding = TemplateEncoding.Raw, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrEmpty(template)) return "";

        return Placeholder().Replace(template, match =>
        {
            if (!TryResolve(match.Groups[1].Value, variables, out var value)) return match.Value;

            var text = Format(value, timeZone);
            var mode = match.Groups[2].Success ? Enum.Parse<TemplateEncoding>(match.Groups[2].Value, ignoreCase: true) : encoding;
            return mode switch
            {
                // 只转义为 JSON 字符串内容，不加引号：模板写成 "title": "{标题}"
                TemplateEncoding.Json => JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
                TemplateEncoding.Url => Uri.EscapeDataString(text),
                _ => text
            };
        });
    }

    /// <summary>模板中出现的变量名（用于设计器提示、测试）。</summary>
    public static IEnumerable<string> VariableNames(string? template) =>
        string.IsNullOrEmpty(template) ? [] : Placeholder().Matches(template).Select(m => m.Groups[1].Value);

    private static bool TryResolve(string path, IReadOnlyDictionary<string, object?> variables, out object? value)
    {
        if (variables.TryGetValue(path, out value)) return true;

        // 多级路径：form.applicant.name → variables["form"]["applicant"]["name"]
        var segments = path.Split('.');
        if (!variables.TryGetValue(segments[0], out value)) return false;
        foreach (var segment in segments.Skip(1))
        {
            if (!TryGetMember(value, segment, out value)) return false;
        }
        return true;
    }

    private static bool TryGetMember(object? target, string name, out object? value)
    {
        switch (target)
        {
            case IReadOnlyDictionary<string, object?> dict when dict.TryGetValue(name, out value):
                return true;
            case IDictionary<string, object?> dict:
                // ExpandoObject 区分大小写，这里按不区分大小写查找
                foreach (var (key, v) in dict)
                {
                    if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = v;
                    return true;
                }
                break;
        }
        value = null;
        return false;
    }

    private static string Format(object? value, TimeZoneInfo? timeZone) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => (timeZone is null || d.Kind != DateTimeKind.Utc ? d : TimeZoneInfo.ConvertTimeFromUtc(d, timeZone))
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        ExpandoObject or IDictionary => JsonSerializer.Serialize(value),
        IEnumerable items => string.Join("、", items.Cast<object?>().Select(i => Format(i, timeZone))),
        _ => value.ToString() ?? ""
    };
}
