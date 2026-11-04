using System.Collections.ObjectModel;
using System.Dynamic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Approval.Core.Conditions;

/// <summary>把表单 JSON 转成条件表达式可用的强类型变量（数字 → decimal，布尔、字符串保持原类型）。</summary>
public static class FormData
{
    public static IReadOnlyDictionary<string, object?> Parse(string? json)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ApprovalException("表单数据必须是 JSON 对象");

        foreach (var property in document.RootElement.EnumerateObject())
            result[property.Name] = Convert(property.Value);
        return result;
    }

    /// <summary>Resolver 的顶层只读快照；递归复制 Parse 的动态对象与数组，保留原读取类型。</summary>
    internal static IReadOnlyDictionary<string, object?> Snapshot(IReadOnlyDictionary<string, object?> form) =>
        new ReadOnlyDictionary<string, object?>(form.ToDictionary(p => p.Key, p => Copy(p.Value), StringComparer.OrdinalIgnoreCase));

    private static object? Copy(object? value)
    {
        if (value is ExpandoObject members)
        {
            var copy = new ExpandoObject();
            var dictionary = (IDictionary<string, object?>)copy;
            foreach (var member in members) dictionary[member.Key] = Copy(member.Value);
            return copy;
        }
        return value is object?[] items ? items.Select(Copy).ToArray() : value;
    }

    /// <summary>表单序列化选项：camelCase，中文等非 ASCII 字符不转义，便于在数据库中直接阅读。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(object? formData) => formData switch
    {
        null => "{}",
        string json => json,
        _ => JsonSerializer.Serialize(formData, JsonOptions)
    };

    private static object? Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(Convert).ToArray(),
        // 嵌套对象（如 HTTP 动作的输出）转为动态对象，条件表达式可写 risk.body.level == "high"
        JsonValueKind.Object => ToExpando(element),
        _ => null
    };

    private static ExpandoObject ToExpando(JsonElement element)
    {
        var expando = new ExpandoObject();
        IDictionary<string, object?> members = expando;
        foreach (var property in element.EnumerateObject()) members[property.Name] = Convert(property.Value);
        return expando;
    }
}
