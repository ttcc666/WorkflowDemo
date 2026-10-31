using System.Text.Json;
using Approval.Core.Forms;

namespace Approval.Core.Tests;

/// <summary>公式：与前端共用 tests/shared/formula-cases.json，两边必须得到相同结果。</summary>
public sealed class FormulaTests
{
    public sealed record Case(string Expr, Dictionary<string, decimal?>? Vars, decimal? Result, int? Precision, bool Error)
    {
        public override string ToString() => Expr;
    }

    private static readonly List<Case> Cases = LoadCases();

    private static List<Case> LoadCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "formula-cases.json")));
        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => new Case(
                c.GetProperty("expr").GetString()!,
                c.TryGetProperty("vars", out var vars)
                    ? vars.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : (decimal?)p.Value.GetDecimal())
                    : null,
                c.TryGetProperty("result", out var result) && result.ValueKind != JsonValueKind.Null ? result.GetDecimal() : null,
                c.TryGetProperty("precision", out var precision) ? precision.GetInt32() : null,
                c.TryGetProperty("error", out var error) && error.GetBoolean()))
            .ToList();
    }

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Shared_case(int index)
    {
        var c = Cases[index];

        var parsed = Formula.TryParse(c.Expr, out var formula, out var error);

        if (c.Error)
        {
            Assert.False(parsed, $"{c.Expr} 应解析失败");
            Assert.False(string.IsNullOrEmpty(error));
            return;
        }
        Assert.True(parsed, $"{c.Expr}：{error}");
        var vars = new Dictionary<string, decimal?>(c.Vars ?? [], StringComparer.OrdinalIgnoreCase);
        var value = formula!.Evaluate(name => vars.GetValueOrDefault(name));
        Assert.Equal(c.Result, value is { } v ? Formula.Round(v, c.Precision ?? 6) : null);
    }

    [Fact]
    public void Shared_cases_are_loaded() => Assert.True(Cases.Count > 50);

    [Fact]
    public void Identifiers_are_collected_case_insensitively()
    {
        var formula = Formula.Parse("if(Total > 100, total - ifnull(advance, 0), round(qty * price, 2))");

        Assert.Equal(["advance", "price", "qty", "Total"], formula.Identifiers.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("TOTAL", formula.Identifiers);
    }

    [Fact]
    public void Too_long_or_too_deep_formulas_are_rejected()
    {
        Assert.False(Formula.TryParse(new string('1', Formula.MaxLength + 1), out _, out var tooLong));
        Assert.Contains("500", tooLong);
        Assert.False(Formula.TryParse(new string('(', 40) + "1" + new string(')', 40), out _, out var tooDeep));
        Assert.Contains("嵌套", tooDeep);
    }

    [Fact]
    public void Overflow_yields_null() =>
        Assert.Null(Formula.Parse("a * a * a").Evaluate(_ => 79228162514264337593543950335m));
}
