using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Approval.Core.Abstractions;

namespace Approval.Core.Actions;

internal static partial class ActionVariables
{
    private const string Member = "[A-Za-z_][A-Za-z0-9_-]{0,63}";
    private const int MaxBoundCharacters = 32 * 1024;
    private const int MaxContextCharacters = 512 * 1024;

    [GeneratedRegex("\\A" + Member + "(?:\\." + Member + "|\\[[0-9]{1,6}\\])*\\z", RegexOptions.NonBacktracking)]
    private static partial Regex Path();

    [GeneratedRegex(Member + "|[0-9]{1,6}", RegexOptions.NonBacktracking)]
    private static partial Regex Token();

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.NonBacktracking)]
    private static partial Regex Template();

    internal static JsonElement Snapshot(ApprovalActionContext context)
    {
        if (context.Form.GetRawText().Length + context.Outputs.GetRawText().Length > MaxContextCharacters)
            throw new FormatException("动作上下文超过预算");
        return JsonSerializer.SerializeToElement(new
        {
            form = context.Form, outputs = context.Outputs,
            business = new { type = context.BusinessType, key = context.BusinessKey },
            initiator = new { id = context.InitiatorId },
            instance = new { id = context.InstanceId, title = context.Title, status = context.Status.ToString(), context.Round },
            node = new { id = context.NodeId, name = context.NodeName },
            @operator = new { id = context.OperatorId }
        }, JsonSerializerOptions.Web);
    }

    internal static bool IsPath(string? path) => path is { Length: > 0 and <= 256 } && Path().IsMatch(path);

    internal static bool IsSource(string? path) => IsPath(path) &&
        path!.Split('.', '[')[0] is "form" or "outputs" or "business" or "initiator" or "instance" or "node" or "operator";

    internal static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (!IsPath(path)) return false;
        foreach (Match token in Token().Matches(path))
        {
            if (token.Index > 0 && path[token.Index - 1] == '[')
            {
                if (value.ValueKind != JsonValueKind.Array ||
                    !int.TryParse(token.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index >= value.GetArrayLength()) return false;
                value = value[index];
            }
            else if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(token.Value, out value))
                return false;
        }
        return true;
    }

    internal static bool IsTemplate(string value) =>
        Template().Matches(value).All(match => IsSource(match.Groups[1].Value.Trim())) &&
        !Template().Replace(value, "").Contains("{{", StringComparison.Ordinal);

    internal static JsonElement Bind(JsonElement value, JsonElement root)
    {
        var remaining = MaxBoundCharacters;
        object BindValue(JsonElement input)
        {
            Charge(ref remaining, 1);
            if (input.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var property in input.EnumerateObject())
                {
                    Charge(ref remaining, property.Name.Length);
                    result.Add(property.Name, BindValue(property.Value));
                }
                return result;
            }
            if (input.ValueKind == JsonValueKind.Array) return input.EnumerateArray().Select(BindValue).ToArray();
            var bound = input.ValueKind == JsonValueKind.String ? BindText(input.GetString()!, root) : input;
            Charge(ref remaining, bound is JsonElement json ? json.GetRawText().Length : ((string)bound).Length);
            return bound;
        }
        return JsonSerializer.SerializeToElement(BindValue(value));
    }

    private static object BindText(string value, JsonElement root)
    {
        var match = Template().Match(value);
        if (match.Success && match.Length == value.Length) return Resolve(root, match.Groups[1].Value.Trim());
        return Render(value, root);
    }

    internal static string Render(string value, JsonElement root)
    {
        var remaining = MaxBoundCharacters - value.Length;
        var rendered = Template().Replace(value, match =>
        {
            var resolved = Resolve(root, match.Groups[1].Value.Trim());
            var replacement = resolved.ValueKind switch
            {
                JsonValueKind.String => resolved.GetString()!,
                JsonValueKind.Null => "",
                _ => resolved.GetRawText()
            };
            Charge(ref remaining, replacement.Length);
            return replacement;
        });
        return rendered;
    }

    internal static void Charge(ref int remaining, int length)
    {
        if ((remaining -= length) < 0) throw new FormatException("动作数据超过预算");
    }

    private static JsonElement Resolve(JsonElement root, string path) =>
        IsSource(path) && TryResolve(root, path, out var resolved) ? resolved : throw new FormatException("动作变量未找到");
}
