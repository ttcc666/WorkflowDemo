using Approval.Forms;
using Approval.Forms.Runtime;
using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Forms.Schema;

namespace Approval.Core.Tests;

/// <summary>计算字段：发布校验（语法、引用、循环）与服务端计算（明细列公式 → 合计 → 顶层公式，忽略提交的值）。</summary>
public sealed class FormFormulaTests
{
    /// <summary>采购：明细金额 = 数量 × 单价（合计），应付 = 合计 - 预付，折后 = round(应付 × 折扣, 2)；应付超过 1000 走经理审批。</summary>
    private const string PurchaseFlow = """
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "settings": { "startable": true, "titleTemplate": "采购（应付 {form.payable} 元）" },
      "form": { "fields": [
        { "key": "items", "label": "明细", "type": "Table", "required": true, "columns": [
          { "key": "name", "label": "物品", "type": "Text", "required": true },
          { "key": "qty", "label": "数量", "type": "Number", "required": true },
          { "key": "price", "label": "单价", "type": "Money", "required": true },
          { "key": "amount", "label": "金额", "type": "Formula", "formula": "qty * price", "sum": true } ] },
        { "key": "advance", "label": "预付", "type": "Money" },
        { "key": "rate", "label": "折扣", "type": "Number" },
        { "key": "discounted", "label": "折后", "type": "Formula", "formula": "round(payable * rate, 2)" },
        { "key": "payable", "label": "应付", "type": "Formula", "formula": "items_amount_sum - ifnull(advance, 0)", "precision": 1 },
        { "key": "ratio", "label": "预付比例", "type": "Formula", "formula": "advance / items_amount_sum * 100", "precision": 0 }
      ] },
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "cond" },
        "cond":   { "name": "金额", "type": "Condition", "branches": [
                      { "priority": 1, "expression": "payable > 1000", "next": "boss" },
                      { "priority": 99, "expression": null, "next": "clerk" } ] },
        "boss":   { "name": "经理", "type": "Approval", "approvers": [{ "type": "User", "value": "boss" }], "next": "end" },
        "clerk":  { "name": "文员", "type": "Approval", "approvers": [{ "type": "User", "value": "clerk" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static FormSchema Schema => FlowDefinition.Parse(PurchaseFlow).GetForm()!;

    private static List<string> SchemaErrors(Action<FormSchema> change)
    {
        var flow = FlowDefinition.Parse(PurchaseFlow);
        change(flow.GetForm()!);
        var errors = new List<string>();
        FormSchemaValidator.Validate(flow, errors);
        return errors;
    }

    [Fact]
    public void Valid_formulas_publish_without_errors() => Assert.Empty(SchemaErrors(_ => { }));

    [Theory]
    [InlineData("payable", "items_amount_sum -", "公式有误")]
    [InlineData("payable", "missing + 1", "不存在的字段 'missing'")]
    [InlineData("discounted", "items + 1", "'items'，它不是数值字段")]
    [InlineData("payable", "discounted + 1", "循环引用")]
    [InlineData("payable", "payable * 2", "循环引用")]
    [InlineData("payable", "", "没有填写公式")]
    public void Invalid_top_level_formulas_are_reported(string key, string formula, string expected)
    {
        var errors = SchemaErrors(form => form.Fields.Single(f => f.Key == key).Formula = formula);

        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void Column_formulas_can_only_reference_numeric_columns_of_the_same_row()
    {
        var errors = SchemaErrors(form =>
        {
            var columns = form.Fields.Single(f => f.Key == "items").Columns;
            columns.Single(c => c.Key == "amount").Formula = "qty * price + advance";
            columns.Add(new FormField { Key = "total", Label = "总价", Type = FormFieldType.Formula, Formula = "name + 1" });
        });

        Assert.Contains(errors, e => e.Contains("「金额」的公式引用了不存在的字段 'advance'"));
        Assert.Contains(errors, e => e.Contains("「总价」的公式引用了 'name'，它不是数值字段"));
    }

    [Fact]
    public void Formula_fields_cannot_be_required_or_have_defaults_and_others_cannot_have_formulas()
    {
        var errors = SchemaErrors(form =>
        {
            var payable = form.Fields.Single(f => f.Key == "payable");
            payable.Required = true;
            payable.Precision = 7;
            form.Fields.Single(f => f.Key == "advance").Formula = "1 + 1";
        });

        Assert.Contains(errors, e => e.Contains("「应付」是计算字段，不能设置必填"));
        Assert.Contains(errors, e => e.Contains("「应付」的小数位数必须在 0 到 6 之间"));
        Assert.Contains(errors, e => e.Contains("「预付」不是计算字段，不能设置公式"));
    }

    [Fact]
    public void Normalization_computes_row_formulas_sums_and_dependent_formulas_ignoring_submitted_values()
    {
        var result = FormDataValidator.Validate(Schema, Json("""
            { "items": [{ "name": "笔", "qty": 3, "price": 2.5, "amount": 999 }, { "name": "纸", "qty": "2", "price": 10 }],
              "advance": 5, "rate": 0.9, "payable": 1, "discounted": 1, "ratio": 1 }
            """));

        Assert.True(result.IsValid, string.Join(";", result.Errors.SelectMany(e => e.Value)));
        var data = Json(result.Json);
        Assert.Equal([7.5m, 20m], data.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("amount").GetDecimal()));
        Assert.Equal(27.5m, data.GetProperty("items_amount_sum").GetDecimal());
        Assert.Equal(22.5m, data.GetProperty("payable").GetDecimal());          // 精度 1
        Assert.Equal(20.25m, data.GetProperty("discounted").GetDecimal());      // 依赖 payable，先算 payable
        Assert.Equal(18m, data.GetProperty("ratio").GetDecimal());              // 18.18… → 精度 0
        Assert.Contains("\"amount\":20}", result.Json);                          // 没有多余的尾随 0
    }

    [Fact]
    public void Null_inputs_and_division_by_zero_yield_null()
    {
        var result = FormDataValidator.Validate(Schema, Json("""{ "items": [{ "name": "笔", "qty": 0, "price": 0 }] }"""));

        var data = Json(result.Json);
        Assert.Equal(0m, data.GetProperty("payable").GetDecimal());               // ifnull(advance, 0)
        Assert.Equal(JsonValueKind.Null, data.GetProperty("discounted").ValueKind); // rate 为空
        Assert.Equal(JsonValueKind.Null, data.GetProperty("ratio").ValueKind);      // advance 为空
    }

    [Fact]
    public async Task Computed_values_drive_conditions_and_titles()
    {
        await using var host = new TestHost(build: b => b.AddApprovalForms());
        host.Identity.Add("alice");
        await host.PublishAsync("purchase", PurchaseFlow);
        var forms = (IServiceProvider sp) => sp.GetRequiredService<ApprovalFormService>();

        var big = await host.RunAsync(sp => forms(sp).StartAsync("purchase", "alice",
            Json("""{ "items": [{ "name": "电脑", "qty": 2, "price": 600 }], "payable": 1 }""")));
        var small = await host.RunAsync(sp => forms(sp).StartAsync("purchase", "alice",
            Json("""{ "items": [{ "name": "电脑", "qty": 2, "price": 600 }], "advance": 300 }""")));

        Assert.Equal("采购（应付 1200 元）", big.Title);
        Assert.Equal("boss", Assert.Single((await host.GetInstanceAsync(big.Id)).Tasks).AssigneeValue);
        Assert.Equal("clerk", Assert.Single((await host.GetInstanceAsync(small.Id)).Tasks).AssigneeValue);
        Assert.Equal(InstanceStatus.Running, (await host.GetInstanceAsync(small.Id)).Status);
    }
}
