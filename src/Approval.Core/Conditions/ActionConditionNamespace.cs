using System.Text.RegularExpressions;
using Approval.Core.Definitions;

namespace Approval.Core.Conditions;

internal static partial class ActionConditionNamespace
{
    internal const string Name = "workflow";

    // Tokenize string/character literals as a unit so their contents cannot be mistaken for a root path.
    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[\\p{L}_][\\p{L}\\p{Nd}_]*|\\?\\.|\\?\\[|[^\\s]")]
    private static partial Regex Token();

    internal static void Validate(FlowDefinition definition, IReadOnlySet<string> fields, List<string> errors)
    {
        if (!definition.Nodes.Values.Any(n => n.Type == NodeType.Action)) return;
        if (fields.Contains(Name))
            errors.Add("含动作节点的流程保留 workflow 条件命名空间，请重命名同名表单字段");
        foreach (var node in definition.Nodes.Values.Where(n => n.Type == NodeType.Condition))
            if (FindAliasConflict(node, fields.Contains) is { } conflict) errors.Add(conflict);
    }

    internal static string? FindAliasConflict(FlowNode node, Func<string, bool> hasField)
    {
        if (!hasField("form") && !hasField("outputs")) return null;
        foreach (var branch in node.Branches)
        {
            if (string.IsNullOrWhiteSpace(branch.Expression)) continue;
            var tokens = Token().Matches(branch.Expression);
            for (var i = 0; i + 1 < tokens.Count; i++)
            {
                var alias = tokens[i].Value;
                if (string.Equals(alias, "form", StringComparison.OrdinalIgnoreCase)) alias = "form";
                else if (string.Equals(alias, "outputs", StringComparison.OrdinalIgnoreCase)) alias = "outputs";
                else continue;
                if (hasField(alias) && IsQualifiedRoot(tokens, i))
                    return $"条件节点 '{node.Id}' 的旧别名 '{alias}' 与表单字段重名。请用 workflow.{alias} 引用流程变量，用 workflow.form.{alias} 引用该表单字段。";
            }
        }
        return null;
    }

    internal static IReadOnlySet<string> QualifiedRoots(string expression)
    {
        var tokens = Token().Matches(expression);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < tokens.Count; i++)
            if ((char.IsLetter(tokens[i].Value[0]) || tokens[i].Value[0] == '_') && IsQualifiedRoot(tokens, i))
                roots.Add(tokens[i].Value);
        return roots;
    }

    private static bool IsQualifiedRoot(MatchCollection tokens, int root)
    {
        if (root > 0 && tokens[root - 1].Value is "." or "?.") return false;
        var after = root + 1;
        var before = root - 1;
        while (before >= 0 && after < tokens.Count && tokens[before].Value == "(" && tokens[after].Value == ")")
        {
            before--;
            after++;
        }
        // foo(form).Member qualifies the call result, while (form).Member qualifies the alias.
        if (after > root + 1 && before >= 0)
        {
            var previous = tokens[before].Value;
            if (char.IsLetter(previous[0]) || previous[0] == '_' || previous is ")" or "]") return false;
        }
        return after < tokens.Count && tokens[after].Value is "." or "?." or "[" or "?[";
    }
}
