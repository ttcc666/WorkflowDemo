using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Forms;

namespace Approval.Core.Tests;

public sealed class FormTests
{
    /// <summary>请假：天数 &gt; 3 走负责人，否则直接结束；明细合计 &gt; 5000 时还需财务审批。</summary>
    private const string LeaveFlow = """
    {
      "startNodeId": "start",
      "settings": { "startable": true, "category": "人事", "titleTemplate": "{申请人}请假 {form.days} 天" },
      "form": { "fields": [
        { "key": "leaveType", "label": "请假类型", "type": "Select", "required": true,
          "options": [{ "label": "年假", "value": "annual" }, { "label": "事假", "value": "personal" }] },
        { "key": "days", "label": "天数", "type": "Number", "required": true, "min": 0.5, "max": 30, "precision": 1 },
        { "key": "handover", "label": "交接人", "type": "User" },
        { "key": "reason", "label": "事由", "type": "TextArea", "maxLength": 10 }
      ] },
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "dept" },
        "dept":   { "name": "部门审批", "type": "Approval", "approvers": [{ "type": "Role", "value": "DeptReviewer" }], "next": "cond" },
        "cond":   { "name": "天数", "type": "Condition", "branches": [
                      { "priority": 1, "expression": "days > 3", "next": "leader" },
                      { "priority": 99, "expression": null, "next": "end" } ] },
        "leader": { "name": "负责人审批", "type": "Approval", "approvers": [{ "type": "Role", "value": "Leader" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static readonly FormSchema ExpenseSchema = new()
    {
        Fields =
        [
            new FormField { Key = "title", Label = "标题", Type = FormFieldType.Text, Required = true, MaxLength = 20 },
            new FormField
            {
                Key = "items", Label = "明细", Type = FormFieldType.Table, Required = true, MaxRows = 3,
                Columns =
                [
                    new FormField { Key = "name", Label = "项目", Type = FormFieldType.Text, Required = true },
                    new FormField { Key = "amount", Label = "金额", Type = FormFieldType.Money, Required = true, Sum = true }
                ]
            },
            new FormField { Key = "period", Label = "期间", Type = FormFieldType.DateRange },
            new FormField { Key = "tags", Label = "标签", Type = FormFieldType.MultiSelect, Options = [new("A", "a"), new("B", "b")] },
            new FormField { Key = "agree", Label = "承诺", Type = FormFieldType.Switch, Required = true },
            new FormField { Key = "note", Label = "说明", Type = FormFieldType.Description, Help = "仅展示" }
        ]
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static ApprovalFormService Forms(IServiceProvider sp) => sp.GetRequiredService<ApprovalFormService>();

    private static TestHost NewHost(Action<ApprovalOptions>? configure = null) => new(configure, b => b.AddFormApprovals());

    // ───────────── 表单结构校验（发布时） ─────────────

    [Fact]
    public void 表单结构错误在发布时报告()
    {
        var flow = FlowDefinition.Parse(LeaveFlow);
        flow.Form!.Fields.AddRange(
        [
            new FormField { Key = "1bad", Label = "非法标识", Type = FormFieldType.Text },
            new FormField { Key = "days", Label = "重复", Type = FormFieldType.Number },
            new FormField { Key = "kind", Label = "无选项", Type = FormFieldType.Select },
            new FormField { Key = "range", Label = "范围", Type = FormFieldType.Number, Min = 5, Max = 1 },
            new FormField { Key = "empty", Label = "空表", Type = FormFieldType.Table },
            new FormField
            {
                Key = "t", Label = "明细", Type = FormFieldType.Table,
                Columns = [new FormField { Key = "u", Label = "人员列", Type = FormFieldType.User }, new FormField { Key = "x", Label = "文本合计", Type = FormFieldType.Text, Sum = true }]
            },
            new FormField { Key = "d", Label = "说明", Type = FormFieldType.Description, Required = true },
            new FormField { Key = "n", Label = "默认值", Type = FormFieldType.Number, Max = 10, DefaultValue = Json("20") }
        ]);
        flow.Settings!.TitleTemplate = "{申请人} {form.missing} {未知}";
        flow.Nodes["leader"].Approvers.Add(new ApproverRule("FormField", "reason"));

        var errors = string.Join("\n", ValidateForm(flow));

        Assert.Contains("'1bad' 只能包含字母", errors);
        Assert.Contains("'days' 重复", errors);
        Assert.Contains("「无选项」没有选项", errors);
        Assert.Contains("「范围」的最小值不能大于最大值", errors);
        Assert.Contains("「空表」没有列", errors);
        Assert.Contains("明细列不支持类型 User", errors);
        Assert.Contains("「文本合计」不是数值列", errors);
        Assert.Contains("「说明」是说明文字，不能设为必填", errors);
        Assert.Contains("「默认值」的默认值无效", errors);
        Assert.Contains("{form.missing} 不存在", errors);
        Assert.Contains("{未知} 不存在", errors);
        Assert.Contains("按表单字段 'reason' 取人", errors);
    }

    [Fact]
    public void 合计字段不能与普通字段重名()
    {
        var flow = FlowDefinition.Parse(LeaveFlow);
        flow.Form!.Fields.Add(new FormField
        {
            Key = "items", Label = "明细", Type = FormFieldType.Table,
            Columns = [new FormField { Key = "amount", Label = "金额", Type = FormFieldType.Money, Sum = true }]
        });
        flow.Form.Fields.Add(new FormField { Key = "items_amount_sum", Label = "冲突", Type = FormFieldType.Number });

        Assert.Contains(ValidateForm(flow), e => e.Contains("合计字段 'items_amount_sum' 与已有字段重名"));
    }

    [Fact]
    public void 合法的表单与设置可以发布_并在定义中保留()
    {
        var flow = FlowDefinition.Parse(LeaveFlow);

        Assert.Empty(ValidateForm(flow));
        var roundTrip = FlowDefinition.Parse(flow.ToJson());
        Assert.True(roundTrip.Settings!.Startable);
        Assert.Equal(FormFieldType.Select, roundTrip.Form!.Fields[0].Type);
        Assert.Equal(0.5m, roundTrip.Form.Fields[1].Min);
    }

    private static IReadOnlyList<string> ValidateForm(FlowDefinition flow)
    {
        var errors = new List<string>();
        FormSchemaValidator.Validate(flow, errors);
        return errors;
    }

    // ───────────── 表单数据校验与规范化 ─────────────

    [Fact]
    public void 数据校验_必填类型选项范围长度()
    {
        var schema = FlowDefinition.Parse(LeaveFlow).Form!;

        var result = FormDataValidator.Validate(schema, Json("""{ "leaveType": "unknown", "days": 31.25, "reason": "超过十个字符的事由内容" }"""));

        Assert.Equal(["请假类型的选项无效"], result.Errors["leaveType"]);
        Assert.Contains("天数最多 1 位小数", result.Errors["days"]);
        Assert.Contains("天数不能大于 30", result.Errors["days"]);
        Assert.Equal(["事由不能超过 10 个字符"], result.Errors["reason"]);
        Assert.False(result.Errors.ContainsKey("handover"));

        var missing = FormDataValidator.Validate(schema, Json("{}"));
        Assert.Equal(["请选择请假类型"], missing.Errors["leaveType"]);
        Assert.Equal(["请填写天数"], missing.Errors["days"]);
    }

    [Fact]
    public void 数据规范化_类型转换并剔除未定义字段()
    {
        var schema = FlowDefinition.Parse(LeaveFlow).Form!;

        var result = FormDataValidator.Validate(schema, Json("""{ "LEAVETYPE": "annual", "days": "2.5", "reason": "  休息  ", "hacker": true }"""));

        Assert.True(result.IsValid, string.Join(";", result.Errors.SelectMany(e => e.Value)));
        var data = Json(result.Json);
        Assert.Equal("annual", data.GetProperty("leaveType").GetString());
        Assert.Equal(2.5m, data.GetProperty("days").GetDecimal());
        Assert.Equal("休息", data.GetProperty("reason").GetString());
        // 未填写的可选字段写为 null，条件表达式中变量始终存在
        Assert.Equal(JsonValueKind.Null, data.GetProperty("handover").ValueKind);
        Assert.False(data.TryGetProperty("hacker", out _));
    }

    [Fact]
    public void 明细表_单元格错误路径与合计()
    {
        var invalid = FormDataValidator.Validate(ExpenseSchema,
            Json("""{ "title": "差旅", "items": [{ "name": "机票", "amount": 1200.5 }, { "name": "", "amount": "abc" }], "agree": false }"""));

        Assert.Equal(["请填写项目"], invalid.Errors["items.1.name"]);
        Assert.Equal(["金额应为数字"], invalid.Errors["items.1.amount"]);
        Assert.Equal(["请勾选承诺"], invalid.Errors["agree"]);

        var valid = FormDataValidator.Validate(ExpenseSchema, Json("""
            { "title": "差旅", "items": [{ "name": "机票", "amount": 1200.5, "extra": 1 }, { "name": "酒店", "amount": "800" }],
              "period": ["2026-01-01", "2026-01-03"], "tags": ["a", "a", "b"], "agree": true, "note": "忽略" }
            """));

        Assert.True(valid.IsValid, string.Join(";", valid.Errors.SelectMany(e => e.Value)));
        var data = Json(valid.Json);
        Assert.Equal(2000.5m, data.GetProperty("items_amount_sum").GetDecimal());
        Assert.False(data.GetProperty("items")[0].TryGetProperty("extra", out _));
        Assert.Equal(["a", "b"], data.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.False(data.TryGetProperty("note", out _));
    }

    [Fact]
    public void 明细表_行数上限与日期区间顺序()
    {
        var rows = string.Join(",", Enumerable.Range(1, 4).Select(i => $$"""{ "name": "x{{i}}", "amount": 1 }"""));

        var result = FormDataValidator.Validate(ExpenseSchema,
            Json($$"""{ "title": "t", "items": [{{rows}}], "period": ["2026-02-01", "2026-01-01"], "agree": true }"""));

        Assert.Equal(["明细最多 3 行"], result.Errors["items"]);
        Assert.Equal(["期间的开始日期不能晚于结束日期"], result.Errors["period"]);
    }

    [Fact]
    public void 非对象表单数据被拒绝()
    {
        var ex = Assert.Throws<ApprovalException>(() => FormDataValidator.Validate(ExpenseSchema, Json("[1, 2]")));
        Assert.Contains("JSON 对象", ex.Message);
    }

    // ───────────── 发起中心与表单审批 ─────────────

    [Fact]
    public async Task 发起中心只列出可发起且有权限的流程()
    {
        await using var host = NewHost();
        host.Identity.Add("hana", "HR");
        await host.PublishAsync("leave", LeaveFlow);
        await host.PublishAsync("hr.only", LeaveFlow.Replace("\"startable\": true", "\"startable\": true, \"initiatorRoles\": [\"HR\"]"));
        await host.PublishAsync("cert", Flows.Certificate);

        var alice = await host.RunAsync(sp => Forms(sp).ListStartableAsync("alice"));
        var hana = await host.RunAsync(sp => Forms(sp).ListStartableAsync("hana"));

        Assert.Equal(["leave"], alice.Select(f => f.Key));
        Assert.Equal(["hr.only", "leave"], hana.Select(f => f.Key).Order());
        Assert.Equal("人事", alice[0].Category);

        var forbidden = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => Forms(sp).GetStartFormAsync("hr.only", "alice")));
        var notStartable = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => Forms(sp).GetStartFormAsync("cert", "alice")));
        Assert.Equal(ApprovalErrorCode.Forbidden, forbidden.Code);
        Assert.Equal(ApprovalErrorCode.NotFound, notStartable.Code);
    }

    [Fact]
    public async Task 发起表单审批_标题模板与条件分支()
    {
        await using var host = NewHost();
        host.Identity.Add("dave", "DeptReviewer");
        host.Identity.Add("leo", "Leader");
        await host.PublishAsync("leave", LeaveFlow);

        var shortLeave = await host.RunAsync(sp => Forms(sp).StartAsync("leave", "alice", Json("""{ "leaveType": "annual", "days": 2 }"""), "请批准"));
        var longLeave = await host.RunAsync(sp => Forms(sp).StartAsync("leave", "alice", Json("""{ "leaveType": "annual", "days": "5" }""")));

        Assert.Equal("alice请假 2 天", shortLeave.Title);
        Assert.Equal("Form", shortLeave.BusinessType);
        Assert.Equal(32, shortLeave.BusinessKey.Length);

        foreach (var todo in await host.TodoAsync("dave")) await host.ApproveAsync(todo.TaskId, "dave");
        Assert.Equal(InstanceStatus.Approved, (await host.GetInstanceAsync(shortLeave.Id)).Status);
        Assert.Equal("leader", (await host.GetInstanceAsync(longLeave.Id)).CurrentNodeId);
    }

    [Fact]
    public async Task 表单校验失败抛出字段错误()
    {
        await using var host = NewHost();
        await host.PublishAsync("leave", LeaveFlow);

        var ex = await Assert.ThrowsAsync<ApprovalValidationException>(() =>
            host.RunAsync(sp => Forms(sp).StartAsync("leave", "alice", Json("""{ "days": 0 }"""))));

        Assert.Equal(ApprovalErrorCode.Validation, ex.Code);
        Assert.Equal(["leaveType", "days"], ex.Errors.Keys);
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetStartedByAsync("alice")));
    }

    [Fact]
    public async Task 退回发起人后修改表单重新提交_按新数据流转()
    {
        await using var host = NewHost();
        host.Identity.Add("dave", "DeptReviewer");
        await host.PublishAsync("leave", LeaveFlow);
        var instance = await host.RunAsync(sp => Forms(sp).StartAsync("leave", "alice", Json("""{ "leaveType": "annual", "days": 5 }""")));
        var deptTask = Assert.Single(await host.TodoAsync("dave"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(deptTask.TaskId, "dave", "天数太多", "start"));
        var resubmit = Assert.Single(await host.TodoAsync("alice"));

        // 新数据仍需通过校验
        await Assert.ThrowsAsync<ApprovalValidationException>(() =>
            host.RunAsync(sp => Forms(sp).ResubmitAsync(resubmit.TaskId, "alice", Json("""{ "leaveType": "x", "days": 1 }"""))));
        await host.RunAsync(sp => Forms(sp).ResubmitAsync(resubmit.TaskId, "alice", Json("""{ "leaveType": "annual", "days": 1 }"""), "改为 1 天"));
        await host.ApproveMyTaskAsync("dave");

        var done = await host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, done.Status);
        Assert.Equal("alice请假 1 天", done.Title);
        Assert.Equal(1m, Json(done.FormDataJson).GetProperty("days").GetDecimal());
    }

    [Fact]
    public async Task 非表单业务重新提交不能修改表单数据()
    {
        await using var host = NewHost();
        host.Identity.Add("dave", "DeptReviewer");
        await host.PublishAsync("cert", Flows.Certificate);
        await host.StartAsync("cert", "C-1", "alice", new { certType = "Normal" });
        var task = Assert.Single(await host.TodoAsync("dave"));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(task.TaskId, "dave", "请补充", "start"));
        var resubmit = Assert.Single(await host.TodoAsync("alice"));

        var ex = await Assert.ThrowsAsync<ApprovalException>(() =>
            host.RunAsync(sp => Forms(sp).ResubmitAsync(resubmit.TaskId, "alice", Json("""{ "certType": "Special" }"""))));

        Assert.Contains("由业务单据决定", ex.Message);
        // 不带表单数据时正常重新提交
        await host.RunAsync(sp => Forms(sp).ResubmitAsync(resubmit.TaskId, "alice", null, "已补充"));
        Assert.Single(await host.TodoAsync("dave"));
    }
}
