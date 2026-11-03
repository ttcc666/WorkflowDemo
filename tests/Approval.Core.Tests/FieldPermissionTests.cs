using Approval.Forms;
using Approval.Forms.Runtime;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Forms.Schema;

namespace Approval.Core.Tests;

/// <summary>节点字段权限：审批人同意时修改可编辑字段、参与人看不到隐藏字段（表单数据与修改轨迹）、发布校验与提示。</summary>
public sealed class FieldPermissionTests : IAsyncLifetime
{
    /// <summary>
    /// 部门审批（dave，可改金额 / 备注，隐藏薪资）→ 金额 &gt; 1000 时财务审批（角色 Finance，可改预支，隐藏薪资）
    /// → 抄送（carol、dave，隐藏薪资与备注）→ 结束。
    /// </summary>
    private const string Flow = """
    {
      "startNodeId": "start",
      "settings": { "startable": true, "titleTemplate": "{申请人}报销 {form.amount} 元" },
      "form": { "fields": [
        { "key": "amount", "label": "金额", "type": "Money", "required": true, "min": 0.01 },
        { "key": "advance", "label": "预支", "type": "Money" },
        { "key": "payable", "label": "应付", "type": "Formula", "formula": "amount - ifnull(advance, 0)" },
        { "key": "salary", "label": "薪资", "type": "Money" },
        { "key": "note", "label": "备注", "type": "Text" },
        { "key": "handover", "label": "交接人", "type": "User" }
      ] },
      "nodes": {
        "start":   { "name": "提交", "type": "Start", "next": "dept" },
        "dept":    { "name": "部门审批", "type": "Approval", "approvers": [{ "type": "User", "value": "dave" }],
                     "fieldPermissions": { "amount": "Editable", "note": "Editable", "salary": "Hidden" }, "next": "cond" },
        "cond":    { "name": "金额判断", "type": "Condition", "branches": [
                     { "priority": 1, "expression": "amount > 1000", "next": "finance" },
                     { "priority": 99, "expression": null, "next": "cc" } ] },
        "finance": { "name": "财务审批", "type": "Approval", "approvers": [{ "type": "Role", "value": "Finance" }],
                     "fieldPermissions": { "advance": "Editable", "salary": "Hidden" }, "next": "cc" },
        "cc":      { "name": "抄送", "type": "Cc", "approvers": [{ "type": "User", "value": "carol" }, { "type": "User", "value": "dave" }],
                     "fieldPermissions": { "salary": "Hidden", "note": "Hidden" }, "next": "end" },
        "end":     { "name": "结束", "type": "End" }
      }
    }
    """;

    private readonly TestHost _host = new(build: b => b.AddApprovalForms());

    public Task InitializeAsync()
    {
        _host.Identity.Add("fiona", "Finance");
        return _host.PublishAsync("expense", Flow);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private Task<ApprovalInstance> StartAsync(string formJson) =>
        _host.RunAsync(sp => sp.GetRequiredService<ApprovalFormService>().StartAsync("expense", "alice", Json(formJson)));

    private Task ApproveAsync(Guid taskId, string userId, string? formJson, string? comment = null) =>
        _host.RunAsync(sp => sp.GetRequiredService<ApprovalFormService>()
            .ApproveAsync(taskId, userId, formJson is null ? null : Json(formJson), comment));

    private async Task<Guid> TaskOfAsync(string userId) => Assert.Single(await _host.TodoAsync(userId)).TaskId;

    private async Task<InstanceDetail> ViewAsync(Guid id, string user, bool admin = false) =>
        (await _host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetInstanceForViewerAsync(id, new ApprovalViewer(user, admin))))!;

    private static JsonElement FormOf(InstanceDetail detail) => Json(detail.FormDataJson);

    // ───────────── 同意时修改可编辑字段 ─────────────

    [Fact]
    public async Task 同意时修改可编辑字段_按新值重新计算与流转_标题更新_轨迹记录差异()
    {
        var instance = await StartAsync("""{ "amount": 2000, "advance": 500, "note": "差旅" }""");

        // 金额改为 800 后不再需要财务审批
        await ApproveAsync(await TaskOfAsync("dave"), "dave", """{ "amount": 800, "note": "核减" }""", "金额核减");

        var done = await _host.GetInstanceAsync(instance.Id);
        var form = Json(done.FormDataJson);
        Assert.Equal(InstanceStatus.Approved, done.Status);
        Assert.Equal("alice报销 800 元", done.Title);
        Assert.Equal(800m, form.GetProperty("amount").GetDecimal());
        Assert.Equal(300m, form.GetProperty("payable").GetDecimal());
        Assert.Equal("核减", form.GetProperty("note").GetString());
        Assert.Empty(await _host.TodoAsync("fiona"));

        var logs = await _host.LogsAsync(instance.Id);
        var edit = Assert.Single(logs, l => l.Action == ApprovalAction.FormEdit);
        var approve = logs.Single(l => l.Action == ApprovalAction.Approve);
        Assert.True(edit.Sequence < approve.Sequence);
        Assert.Equal(("dave", "dept", "修改了 金额、应付、备注"), (edit.OperatorId, edit.NodeId, edit.Comment));
        Assert.Equal("金额核减", approve.Comment);
        var changes = Json(edit.Details!).EnumerateArray().ToList();
        Assert.Equal(["amount", "payable", "note"], changes.Select(c => c.GetProperty("field").GetString()));
        Assert.Equal((2000m, 800m), (changes[0].GetProperty("before").GetDecimal(), changes[0].GetProperty("after").GetDecimal()));
        Assert.Equal(("差旅", "核减"), (changes[2].GetProperty("before").GetString(), changes[2].GetProperty("after").GetString()));
    }

    [Fact]
    public async Task 只能修改本节点的可编辑字段_校验失败不改变状态()
    {
        var instance = await StartAsync("""{ "amount": 2000, "salary": 9000 }""");
        var task = await TaskOfAsync("dave");

        var hidden = await Assert.ThrowsAsync<ApprovalException>(() => ApproveAsync(task, "dave", """{ "salary": 1, "payable": 1, "hacker": 1 }"""));
        var invalid = await Assert.ThrowsAsync<ApprovalValidationException>(() => ApproveAsync(task, "dave", """{ "AMOUNT": -5 }"""));

        Assert.Equal("当前节点不能修改：薪资、应付、hacker", hidden.Message);
        Assert.Equal(["amount"], invalid.Errors.Keys);
        Assert.Equal(2000m, Json((await _host.GetInstanceAsync(instance.Id)).FormDataJson).GetProperty("amount").GetDecimal());
        Assert.Equal(task, await TaskOfAsync("dave"));
    }

    [Fact]
    public async Task 无权处理时先报无权_不暴露字段校验结果()
    {
        await StartAsync("""{ "amount": 2000 }""");
        var task = await TaskOfAsync("dave");

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => ApproveAsync(task, "bob", """{ "salary": 1 }"""));

        Assert.Equal(ApprovalErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task 提交的值没有变化时不记录修改轨迹()
    {
        var instance = await StartAsync("""{ "amount": 2000, "note": "差旅" }""");

        await ApproveAsync(await TaskOfAsync("dave"), "dave", """{ "amount": 2000, "note": "差旅" }""");

        Assert.DoesNotContain(await _host.LogsAsync(instance.Id), l => l.Action == ApprovalAction.FormEdit);
        Assert.Single(await _host.TodoAsync("fiona"));
    }

    [Fact]
    public async Task 修改表单时保留动作输出等表单之外的变量()
    {
        // 引擎直接发起表单业务（模拟同步动作写入的输出变量 risk）
        var instance = await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(
            new StartApprovalRequest("expense", "Form", "E-1", "alice") { FormData = new { amount = 2000, risk = new { level = "high" } } }));

        await ApproveAsync(await TaskOfAsync("dave"), "dave", """{ "amount": 900 }""");

        var form = Json((await _host.GetInstanceAsync(instance.Id)).FormDataJson);
        Assert.Equal(900m, form.GetProperty("amount").GetDecimal());
        Assert.Equal("high", form.GetProperty("risk").GetProperty("level").GetString());
    }

    [Fact]
    public async Task 非表单业务不能在审批中修改表单_可编辑字段为空()
    {
        _host.Identity.Add("dave", "DeptReviewer");
        await _host.PublishAsync("cert", Flows.Certificate);
        await _host.StartAsync("cert", "C-1", "alice", new { certType = "Normal" });
        var todo = Assert.Single(await _host.TodoAsync("dave"));

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => ApproveAsync(todo.TaskId, "dave", """{ "certType": "Special" }"""));
        var editable = await _host.RunAsync(async sp =>
        {
            var task = await sp.GetRequiredService<ApprovalEngine>().FindHandleableTaskAsync(todo.InstanceId, "dave");
            return await sp.GetRequiredService<ApprovalFormService>().GetEditableFieldsAsync(task!);
        });

        Assert.Contains("由业务单据决定", ex.Message);
        Assert.Empty(editable);
    }

    [Fact]
    public async Task 可编辑字段按表单顺序返回()
    {
        var instance = await StartAsync("""{ "amount": 2000 }""");

        var editable = await _host.RunAsync(async sp =>
        {
            var task = await sp.GetRequiredService<ApprovalEngine>().FindHandleableTaskAsync(instance.Id, "dave");
            return await sp.GetRequiredService<ApprovalFormService>().GetEditableFieldsAsync(task!);
        });

        Assert.Equal(["amount", "note"], editable);
    }

    // ───────────── 隐藏字段 ─────────────

    [Fact]
    public async Task 隐藏字段_只以隐藏节点参与的人看不到_发起人与管理员可见()
    {
        var instance = await StartAsync("""{ "amount": 2000, "salary": 9000, "note": "差旅" }""");
        await _host.ApproveMyTaskAsync("dave");
        await _host.ApproveMyTaskAsync("fiona");

        var alice = await ViewAsync(instance.Id, "alice");
        var admin = await ViewAsync(instance.Id, "root", admin: true);
        var fiona = await ViewAsync(instance.Id, "fiona");
        var carol = await ViewAsync(instance.Id, "carol");
        // dave 既是部门审批人（只隐藏薪资）又是抄送人（隐藏薪资与备注）：只隐藏两个节点都隐藏的薪资
        var dave = await ViewAsync(instance.Id, "dave");

        Assert.Equal(9000m, FormOf(alice).GetProperty("salary").GetDecimal());
        Assert.Empty(alice.HiddenFields);
        Assert.True(FormOf(admin).TryGetProperty("salary", out _));
        Assert.Equal(["salary"], fiona.HiddenFields);
        Assert.False(FormOf(fiona).TryGetProperty("salary", out _));
        Assert.Equal(2000m, FormOf(fiona).GetProperty("amount").GetDecimal());
        Assert.Equal(["note", "salary"], carol.HiddenFields);
        Assert.False(FormOf(carol).TryGetProperty("note", out _));
        Assert.Equal(["salary"], dave.HiddenFields);
        Assert.Equal("差旅", FormOf(dave).GetProperty("note").GetString());
    }

    [Fact]
    public async Task 修改轨迹的字段差异按隐藏规则过滤()
    {
        var both = await StartAsync("""{ "amount": 2000, "note": "a" }""");
        await ApproveAsync(await TaskOfAsync("dave"), "dave", """{ "amount": 800, "note": "b" }""");
        var noteOnly = await StartAsync("""{ "amount": 500, "note": "a" }""");
        await ApproveAsync(await TaskOfAsync("dave"), "dave", """{ "note": "b" }""");

        // carol 只以抄送（隐藏备注）参与：差异中去掉备注，轨迹文本按剩余字段重写；只改了备注的轨迹不返回
        var carolEdit = Assert.Single((await ViewAsync(both.Id, "carol")).Logs, l => l.Action == ApprovalAction.FormEdit);
        var aliceEdit = Assert.Single((await ViewAsync(both.Id, "alice")).Logs, l => l.Action == ApprovalAction.FormEdit);

        Assert.Equal("修改了 金额、应付", carolEdit.Comment);
        Assert.Equal(["amount", "payable"], Json(carolEdit.Details!).EnumerateArray().Select(c => c.GetProperty("field").GetString()));
        Assert.Equal("修改了 金额、应付、备注", aliceEdit.Comment);
        Assert.DoesNotContain((await ViewAsync(noteOnly.Id, "carol")).Logs, l => l.Action == ApprovalAction.FormEdit);
        Assert.Contains((await ViewAsync(noteOnly.Id, "alice")).Logs, l => l is { Action: ApprovalAction.FormEdit, Comment: "修改了 备注" });
    }

    [Fact]
    public async Task 隐藏明细表时合计字段一并隐藏()
    {
        var flow = FlowDefinition.Parse(Flow);
        flow.GetForm()!.Fields.Add(new FormField
        {
            Key = "items", Label = "明细", Type = FormFieldType.Table,
            Columns = [new FormField { Key = "amount", Label = "金额", Type = FormFieldType.Money, Sum = true }]
        });
        flow.Nodes["cc"].FieldPermissions["items"] = FieldAccess.Hidden;

        var hidden = FieldVisibility.HiddenFields(flow, ["cc"]);

        Assert.Equal(["items", "items_amount_sum", "note", "salary"], hidden.Order());
        Assert.Empty(FieldVisibility.HiddenFields(flow, []));
        Assert.Empty(FieldVisibility.HiddenFields(flow, ["cc", "unknown"]));
    }

    // ───────────── 发布校验与提示 ─────────────

    [Fact]
    public void 字段权限的发布校验()
    {
        var flow = FlowDefinition.Parse(Flow);
        flow.Nodes["dept"].FieldPermissions["missing"] = FieldAccess.Hidden;
        flow.Nodes["dept"].FieldPermissions["payable"] = FieldAccess.Editable;
        flow.Nodes["dept"].FieldPermissions["NOTE"] = FieldAccess.Hidden;
        flow.Nodes["cc"].FieldPermissions["amount"] = FieldAccess.Editable;
        flow.Nodes["cond"].FieldPermissions["amount"] = FieldAccess.Hidden;

        var errors = string.Join("\n", Validate(flow));

        Assert.Contains("节点 'dept' 的字段权限中，表单没有字段 'missing'", errors);
        Assert.Contains("「应付」是计算字段，不能设为可编辑", errors);
        Assert.Contains("字段 'NOTE' 重复", errors);
        Assert.Contains("抄送节点 'cc' 不能把字段「金额」设为可编辑", errors);
        Assert.Contains("节点 'cond' 不是审批或抄送节点", errors);

        var noForm = FlowDefinition.Parse(Flow);
        noForm.Form = null;
        noForm.Settings!.TitleTemplate = null;
        Assert.Contains(Validate(noForm), e => e.Contains("设置了字段权限，但流程没有表单"));
    }

    [Fact]
    public void 合法的字段权限可以发布_并在定义中保留()
    {
        var flow = FlowDefinition.Parse(Flow);

        Assert.Empty(Validate(flow));
        var roundTrip = FlowDefinition.Parse(flow.ToJson());
        Assert.Equal(FieldAccess.Editable, roundTrip.Nodes["dept"].FieldPermissions["amount"]);
        Assert.Contains("\"salary\":\"Hidden\"", flow.ToJson());
    }

    [Fact]
    public void 隐藏字段可能间接暴露时给出提示()
    {
        var flow = FlowDefinition.Parse(Flow);
        Assert.Empty(new FormDefinitionValidator().Warnings(flow));

        flow.Settings!.TitleTemplate = "{申请人} {form.salary}";
        flow.Nodes["finance"].Notifications.Add(new NotificationRule
        {
            Events = [NotifyEvent.Enter], Recipients = [new ApproverRule("Assignees", "")], Channels = ["InApp"], Content = "备注：{表单.note}"
        });
        flow.Nodes["finance"].FieldPermissions["advance"] = FieldAccess.Hidden;

        var warnings = string.Join("\n", new FormDefinitionValidator().Warnings(flow));

        Assert.Contains("标题模板引用了字段 'salary'，它在节点「部门审批」、「财务审批」、「抄送」被隐藏", warnings);
        Assert.Contains("节点「财务审批」的通知引用了字段 'note'，它在节点「抄送」被隐藏", warnings);
        Assert.Contains("计算字段「应付」引用了 'advance'，它在节点「财务审批」被隐藏", warnings);
    }

    private static IReadOnlyList<string> Validate(FlowDefinition flow) =>
        FormDefinitionValidator.Validate(flow, new HashSet<string>(["User", "Role", "Initiator", "FormField"], StringComparer.OrdinalIgnoreCase));
}
