namespace Approval.Forms.Schema;

/// <summary>
/// 一组计算字段（顶层字段，或某个明细表的列）的计算顺序：解析公式、检查引用，按依赖拓扑排序。
/// 发布时（<see cref="FormSchemaValidator"/>）报告错误；规范化时（<see cref="FormDataValidator"/>）只按顺序计算，忽略无效公式。
/// </summary>
internal static class FormulaPlan
{
    public sealed record Step(FormField Field, Formula Formula);

    /// <param name="fields">本范围内的全部字段（用于检查引用的字段是否存在、是否为数值）。</param>
    /// <param name="extraNumericNames">本范围额外可引用的数值名称（顶层的明细合计字段）。</param>
    /// <param name="owner">错误信息中的范围说明，如“表单”“明细表「费用明细」的列”。</param>
    public static List<Step> Build(IEnumerable<FormField> fields, IEnumerable<string> extraNumericNames, string owner, List<string>? errors)
    {
        var all = fields.Where(f => f.Type != FormFieldType.Description && !string.IsNullOrEmpty(f.Key)).ToList();
        var numeric = new HashSet<string>(all.Where(f => FormField.IsNumeric(f.Type)).Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        numeric.UnionWith(extraNumericNames);
        var known = new HashSet<string>(all.Select(f => f.Key), StringComparer.OrdinalIgnoreCase);

        var parsed = new Dictionary<string, Step>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in all.Where(f => f.Type == FormFieldType.Formula))
        {
            var name = $"{owner}字段「{(string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label)}」";
            if (!Formula.TryParse(field.Formula, out var formula, out var error))
            {
                errors?.Add($"{name}的公式有误：{error}");
                continue;
            }

            var valid = true;
            foreach (var identifier in formula!.Identifiers)
            {
                if (numeric.Contains(identifier)) continue;
                valid = false;
                errors?.Add(known.Contains(identifier)
                    ? $"{name}的公式引用了 '{identifier}'，它不是数值字段"
                    : $"{name}的公式引用了不存在的字段 '{identifier}'");
            }
            if (valid) parsed.TryAdd(field.Key, new Step(field, formula));
        }

        // 拓扑排序：先算被依赖的计算字段；0 = 未访问，1 = 访问中，2 = 已完成
        var order = new List<Step>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        bool Visit(Step step)
        {
            var key = step.Field.Key;
            if (state.TryGetValue(key, out var s))
            {
                if (s == 2) return true;
                var cycle = path.SkipWhile(p => !string.Equals(p, key, StringComparison.OrdinalIgnoreCase)).Append(key);
                errors?.Add($"{owner}的计算字段存在循环引用：{string.Join(" → ", cycle)}");
                return false;
            }
            state[key] = 1;
            path.Add(key);
            foreach (var dependency in step.Formula.Identifiers)
            {
                if (parsed.TryGetValue(dependency, out var next) && !Visit(next)) return false;
            }
            path.RemoveAt(path.Count - 1);
            state[key] = 2;
            order.Add(step);
            return true;
        }

        foreach (var step in parsed.Values)
        {
            path.Clear();
            if (!Visit(step)) return [];
        }
        return order;
    }
}
