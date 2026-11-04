using Approval.Forms;
using System.Text.Json;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class FormEndpointTests
{
    private const string LeaveFlow = """
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "settings": { "startable": true, "category": "人事", "titleTemplate": "{申请人}请假 {form.days} 天" },
      "form": { "fields": [
        { "key": "days", "label": "天数", "type": "Number", "required": true, "min": 0.5 },
        { "key": "handover", "label": "交接人", "type": "User" },
        { "key": "items", "label": "明细", "type": "Table",
          "columns": [{ "key": "amount", "label": "金额", "type": "Money", "required": true, "sum": true }] }
      ] },
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static Task<ApiHost> StartAsync() => ApiHost.StartAsync(build: b => b.AddApprovalForms());

    [Fact]
    public async Task 发起成功返回实例ID()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow, name: "请假");

        var result = await host.PostJsonAsync("/api/approval/forms/leave/start", "alice",
            new { formData = new { days = 2, handover = "hana" }, comment = "请批准" });

        var instanceId = result.GetProperty("instanceId").GetGuid();
        var detail = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "alice");
        Assert.Equal("alice请假 2 天", detail.GetProperty("title").GetString());
        Assert.Equal("Form", detail.GetProperty("businessType").GetString());
        Assert.Equal("请假", Assert.Single((await host.GetJsonAsync("/api/approval/todo", "bob")).GetProperty("items").EnumerateArray())
            .GetProperty("definitionName").GetString());
    }

    [Fact]
    public async Task 校验失败返回400与字段错误()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow);

        var response = await host.SendAsync(HttpMethod.Post, "/api/approval/forms/leave/start", "alice",
            new { formData = new { days = 0, handover = "nobody", items = new[] { new { amount = (decimal?)null } } } });
        var problem = await response.ProblemAsync(400);

        Assert.Equal("Validation", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.Equal("天数不能小于 0.5", errors.GetProperty("days")[0].GetString());
        Assert.Equal("请填写金额", errors.GetProperty("items.0.amount")[0].GetString());
    }

    [Fact]
    public async Task 人员字段校验用户存在()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow);

        var problem = await (await host.SendAsync(HttpMethod.Post, "/api/approval/forms/leave/start", "alice",
            new { formData = new { days = 1, handover = "nobody" } })).ProblemAsync(400);

        Assert.Contains("nobody", problem.GetProperty("errors").GetProperty("handover")[0].GetString());
    }

    [Fact]
    public async Task 发起中心与表单定义()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow, name: "请假");
        await host.PublishAsync("hidden", LeaveFlow.Replace("\"startable\": true", "\"startable\": false"));
        await host.PublishAsync("hr", LeaveFlow.Replace("\"startable\": true", "\"startable\": true, \"initiatorRoles\": [\"HR\"]"));

        var alice = (await host.GetJsonAsync("/api/approval/forms/startable", "alice")).EnumerateArray()
            .Select(f => f.GetProperty("key").GetString()).ToList();
        var hana = (await host.GetJsonAsync("/api/approval/forms/startable", "hana")).EnumerateArray()
            .Select(f => f.GetProperty("key").GetString()).Order().ToList();
        var form = await host.GetJsonAsync("/api/approval/forms/leave", "alice");

        Assert.Equal(["leave"], alice);
        Assert.Equal(["hr", "leave"], hana);
        Assert.Equal("请假", form.GetProperty("name").GetString());
        Assert.Equal("Number", form.GetProperty("form").GetProperty("fields")[0].GetProperty("type").GetString());
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/forms/hidden", "alice")).ProblemAsync(404);
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/forms/hr", "alice")).ProblemAsync(403);
        var me = await host.GetJsonAsync("/api/approval/me", "alice");
        Assert.True(me.GetProperty("formsEnabled").GetBoolean());
        Assert.Equal("Form", me.GetProperty("formBusinessType").GetString());
    }

    [Fact]
    public async Task 未启用表单审批时不映射表单接口()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.SendAsync(HttpMethod.Get, "/api/approval/forms/startable", "alice");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await host.GetJsonAsync("/api/approval/me", "alice")).GetProperty("formsEnabled").GetBoolean());
    }

    [Fact]
    public async Task 草稿保存_列表_打开_提交后删除()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow, name: "请假");

        var saved = await host.SendAsync(HttpMethod.Put, "/api/approval/forms/leave/draft", "alice", new { formData = new { days = 0 }, comment = "未完成" });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var draftId = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("draftId").GetGuid();

        var list = (await host.GetJsonAsync("/api/approval/forms/drafts", "alice")).EnumerateArray().ToList();
        var draft = await host.GetJsonAsync($"/api/approval/forms/drafts/{draftId}", "alice");
        Assert.Equal("请假", Assert.Single(list).GetProperty("definitionName").GetString());
        Assert.Equal("未完成", draft.GetProperty("comment").GetString());
        Assert.False(draft.GetProperty("adjusted").GetBoolean());
        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/forms/drafts/{draftId}", "bob")).ProblemAsync(404);

        await host.PostJsonAsync("/api/approval/forms/leave/start", "alice", new { formData = new { days = 1 }, draftId });

        Assert.Empty((await host.GetJsonAsync("/api/approval/forms/drafts", "alice")).EnumerateArray());
        await (await host.SendAsync(HttpMethod.Delete, $"/api/approval/forms/drafts/{draftId}", "alice")).ProblemAsync(404);
    }

    [Fact]
    public async Task 退回后带表单重新提交()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", LeaveFlow);
        var instanceId = (await host.PostJsonAsync("/api/approval/forms/leave/start", "alice", new { formData = new { days = 5 } }))
            .GetProperty("instanceId").GetGuid();
        var task = (await host.GetJsonAsync("/api/approval/todo", "bob")).GetProperty("items").EnumerateArray().Single().GetProperty("taskId").GetGuid();
        await host.PostJsonAsync($"/api/approval/tasks/{task}/reject", "bob", new { comment = "天数太多", targetNodeId = "start" });
        var resubmit = (await host.GetJsonAsync("/api/approval/todo", "alice")).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Resubmit", resubmit.GetProperty("kind").GetString());

        await host.PostJsonAsync($"/api/approval/tasks/{resubmit.GetProperty("taskId").GetGuid()}/resubmit", "alice",
            new { comment = "改为 3 天", formData = new { days = 3 } });

        var detail = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "alice");
        Assert.Equal("alice请假 3 天", detail.GetProperty("title").GetString());
        Assert.Equal(3, JsonDocument.Parse(detail.GetProperty("formDataJson").GetString()!).RootElement.GetProperty("days").GetInt32());
    }

    /// <summary>审核节点：天数可修改，交接人隐藏。</summary>
    private static readonly string PermissionFlow = LeaveFlow.Replace(
        "\"value\": \"Reviewer\" }], \"next\": \"end\"",
        "\"value\": \"Reviewer\" }], \"fieldPermissions\": { \"days\": \"Editable\", \"handover\": \"Hidden\" }, \"next\": \"end\"");

    [Fact]
    public async Task 同意时修改可编辑字段_我的任务返回可编辑字段()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", PermissionFlow);
        var instanceId = (await host.PostJsonAsync("/api/approval/forms/leave/start", "alice", new { formData = new { days = 5, handover = "hana" } }))
            .GetProperty("instanceId").GetGuid();

        var myTask = await host.GetJsonAsync($"/api/approval/instances/{instanceId}/my-task", "bob");
        var taskId = myTask.GetProperty("task").GetProperty("id").GetGuid();
        Assert.Equal(["days"], myTask.GetProperty("editableFields").EnumerateArray().Select(f => f.GetString()));
        Assert.Empty((await host.GetJsonAsync($"/api/approval/instances/{instanceId}/my-task", "alice")).GetProperty("editableFields").EnumerateArray());

        var denied = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{taskId}/approve", "bob",
            new { formData = new { handover = "bob" } })).ProblemAsync(400);
        var invalid = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{taskId}/approve", "bob",
            new { formData = new { days = 0 } })).ProblemAsync(400);
        Assert.Equal("当前节点不能修改：交接人", denied.GetProperty("title").GetString());
        Assert.Equal("天数不能小于 0.5", invalid.GetProperty("errors").GetProperty("days")[0].GetString());

        await host.PostJsonAsync($"/api/approval/tasks/{taskId}/approve", "bob", new { comment = "改为 3 天", formData = new { days = 3 } });

        var detail = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "alice");
        var edit = detail.GetProperty("logs").EnumerateArray().Single(l => l.GetProperty("action").GetString() == "FormEdit");
        Assert.Equal("alice请假 3 天", detail.GetProperty("title").GetString());
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.Equal("修改了 天数", edit.GetProperty("comment").GetString());
        Assert.Equal(5, JsonDocument.Parse(edit.GetProperty("details").GetString()!).RootElement[0].GetProperty("before").GetInt32());
    }

    [Fact]
    public async Task 参与人看不到隐藏字段_发起人可见()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("leave", PermissionFlow);
        var instanceId = (await host.PostJsonAsync("/api/approval/forms/leave/start", "alice", new { formData = new { days = 2, handover = "hana" } }))
            .GetProperty("instanceId").GetGuid();

        var bob = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "bob");
        var alice = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "alice");

        Assert.Equal(["handover"], bob.GetProperty("hiddenFields").EnumerateArray().Select(f => f.GetString()));
        Assert.False(JsonDocument.Parse(bob.GetProperty("formDataJson").GetString()!).RootElement.TryGetProperty("handover", out _));
        Assert.Empty(alice.GetProperty("hiddenFields").EnumerateArray());
        Assert.Equal("hana", JsonDocument.Parse(alice.GetProperty("formDataJson").GetString()!).RootElement.GetProperty("handover").GetString());
    }

    [Fact]
    public async Task 校验接口返回不阻止发布的提示()
    {
        await using var host = await StartAsync();

        var result = await host.PostJsonAsync("/api/approval/definitions/validate", "admin",
            JsonDocument.Parse(PermissionFlow.Replace("{申请人}请假 {form.days} 天", "{申请人} {form.handover}")).RootElement);

        Assert.Empty(result.GetProperty("errors").EnumerateArray());
        Assert.Contains("标题模板引用了字段 'handover'", Assert.Single(result.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task 未启用表单审批时同意不能修改表单()
    {
        await using var host = await ApiHost.StartAsync();
        var plainFlow = Approval.Core.Definitions.FlowDefinition.Parse(PermissionFlow);
        plainFlow.Form = null;
        foreach (var node in plainFlow.Nodes.Values) node.FieldPermissions.Clear();
        await host.PublishAsync("leave", plainFlow.ToJson());
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("leave", DocHandler.Type, "D-1", "alice")));
        var taskId = (await host.GetJsonAsync("/api/approval/todo", "bob")).GetProperty("items")[0].GetProperty("taskId").GetGuid();

        var problem = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{taskId}/approve", "bob",
            new { formData = new { days = 1 } })).ProblemAsync(400);

        Assert.Contains("未启用表单审批", problem.GetProperty("title").GetString());
        await host.PostJsonAsync($"/api/approval/tasks/{taskId}/approve", "bob", new { comment = "同意" });
    }
}
