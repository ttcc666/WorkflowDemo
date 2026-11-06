using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine.Extensions;

namespace Approval.Core.Actions;

internal sealed class MappingActionHandler : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    private enum Transform { Copy, String, Number, Boolean, Date, Array, Object }

    private sealed record Rule(string Source, string Target, Transform Transform, JsonElement Default, bool OmitNull,
        int? Precision, Rule[]? Items, string? FilterPath, JsonElement FilterEquals);

    private sealed record MappingConfig(Rule[] Mappings);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<Transform>(allowIntegerValues: false) }
    };

    public ApprovalActionDescriptor Descriptor { get; } = new("Mapping", "数据映射", "声明式 JSON 字段转换",
        [ActionExecutionMode.Sync], AllowsContinue: true);

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config)
    {
        if (version != 1) return ["Mapping 配置版本未知"];
        try { Validate(Read(config), nested: false, depth: 0); return []; }
        catch (Exception ex) when (ex is JsonException or FormatException) { return ["Mapping 字段规则无效或冲突"]; }
    }

    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        var budget = new Budget();
        var result = Apply(Read(config), ActionVariables.Snapshot(context), ct, budget);
        var output = JsonSerializer.SerializeToElement(result);
        return Task.FromResult(output.GetRawText().Length <= ApprovalActionValidation.MaxOutputBytes
            ? new ApprovalActionResult(true, output) : new(false));
    }

    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) =>
        ExecuteAsync(context, config, ct);

    private static Rule[] Read(JsonElement config) =>
        config.Deserialize<MappingConfig>(Json)?.Mappings ?? throw new FormatException("Mapping 缺少字段规则");

    private sealed class Budget
    {
        internal int Items = 4096;
        internal int Characters = ApprovalActionValidation.MaxOutputBytes;
    }

    private static void Validate(Rule[] rules, bool nested, int depth)
    {
        if (depth > 4 || rules.Length is 0 or > 64) throw new FormatException();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null || !(nested ? ActionVariables.IsPath(rule.Source) : ActionVariables.IsSource(rule.Source)) ||
                !ActionVariables.IsPath(rule.Target) || rule.Target.Contains('[') || rule.Target.Split('.').Length > 8 ||
                !targets.Add(rule.Target) ||
                rule.Precision is { } precision && (precision is < 0 or > 10 || rule.Transform != Transform.Number) ||
                rule.Items is not null && rule.Transform is not (Transform.Array or Transform.Object) ||
                rule.FilterPath is not null && (rule.Transform != Transform.Array || !ActionVariables.IsPath(rule.FilterPath) ||
                    rule.FilterEquals.ValueKind is JsonValueKind.Undefined or JsonValueKind.Array or JsonValueKind.Object) ||
                rule.FilterPath is null && rule.FilterEquals.ValueKind != JsonValueKind.Undefined)
                throw new FormatException();
            if (rule.Items is { } items) Validate(items, nested: true, depth + 1);
        }
        foreach (var target in targets)
            for (var index = target.IndexOf('.'); index >= 0; index = target.IndexOf('.', index + 1))
                if (targets.Contains(target[..index])) throw new FormatException();
    }

    private static Dictionary<string, object?> Apply(Rule[] rules, JsonElement source, CancellationToken ct, Budget budget)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();
            ActionVariables.Charge(ref budget.Items, 1);
            if (!ActionVariables.TryResolve(source, rule.Source, out var value) || value.ValueKind == JsonValueKind.Null) value = rule.Default;
            if (rule.OmitNull && value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            ActionVariables.Charge(ref budget.Characters, rule.Target.Length + 3);
            var transformed = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null : Convert(value, rule, ct, budget);
            var fields = rule.Target.Split('.');
            var parent = result;
            foreach (var field in fields.AsSpan(0, fields.Length - 1))
            {
                if (!parent.TryGetValue(field, out var child)) parent[field] = child = new Dictionary<string, object?>(StringComparer.Ordinal);
                parent = (Dictionary<string, object?>)child!;
            }
            parent.Add(fields[^1], transformed);
        }
        return result;
    }

    private static object Convert(JsonElement value, Rule rule, CancellationToken ct, Budget budget)
    {
        var text = rule.Transform is Transform.Number or Transform.String or Transform.Boolean or Transform.Date
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText() : "";
        switch (rule.Transform)
        {
            case Transform.Copy: return Copy(value, budget);
            case Transform.String when value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object):
                ActionVariables.Charge(ref budget.Characters, text.Length);
                return text;
            case Transform.Number when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                return rule.Precision is { } precision ? decimal.Round(number, precision, MidpointRounding.ToEven) : number;
            case Transform.Boolean when bool.TryParse(text, out var flag): return flag;
            case Transform.Date when DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ssK", "O"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date):
                return date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            case Transform.Object when value.ValueKind == JsonValueKind.Object:
                return rule.Items is null ? Copy(value, budget) : Apply(rule.Items, value, ct, budget);
            case Transform.Array when value.ValueKind == JsonValueKind.Array:
                var items = new List<object>();
                foreach (var item in value.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    ActionVariables.Charge(ref budget.Items, 1);
                    if (rule.FilterPath is { } filter && (!ActionVariables.TryResolve(item, filter, out var field) ||
                        !JsonElement.DeepEquals(field, rule.FilterEquals))) continue;
                    items.Add(rule.Items is null ? Copy(item, budget) : Apply(rule.Items, item, ct, budget));
                }
                return items;
            default: throw new FormatException("Mapping 无法转换字段类型");
        }
    }

    private static JsonElement Copy(JsonElement value, Budget budget)
    {
        ActionVariables.Charge(ref budget.Characters, value.GetRawText().Length);
        return value;
    }
}
