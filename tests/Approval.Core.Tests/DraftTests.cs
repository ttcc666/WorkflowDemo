using System.Text.Json;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>表单草稿：保存不校验必填、按最新表单调整、每人上限、提交后删除、只能访问自己的草稿。</summary>
public sealed class DraftTests : IAsyncLifetime
{
    private const string LeaveFlow = """
    {
      "startNodeId": "start",
      "settings": { "startable": true },
      "form": { "fields": [
        { "key": "leaveType", "label": "请假类型", "type": "Select", "required": true,
          "options": [{ "label": "年假", "value": "annual" }, { "label": "事假", "value": "personal" }] },
        { "key": "days", "label": "天数", "type": "Number", "required": true, "min": 0.5 },
        { "key": "reason", "label": "事由", "type": "TextArea" }
      ] },
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private readonly TestHost _host = new(build: b => b.AddFormApprovals());

    public async Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        await _host.PublishAsync("leave", LeaveFlow);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private Task<T> FormsAsync<T>(Func<ApprovalFormService, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<ApprovalFormService>()));

    [Fact]
    public async Task Saving_skips_required_checks_and_drops_invalid_values()
    {
        var draft = await FormsAsync(f => f.SaveDraftAsync("alice", "leave", null,
            Json("""{ "leaveType": "unknown", "days": "abc", "reason": "  回家  ", "hacker": 1 }"""), " 先存一下 "));

        var data = Json(draft.FormDataJson);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("leaveType").ValueKind);   // 选项无效：丢弃
        Assert.Equal(JsonValueKind.Null, data.GetProperty("days").ValueKind);        // 类型不对：丢弃
        Assert.Equal("回家", data.GetProperty("reason").GetString());
        Assert.False(data.TryGetProperty("hacker", out _));
        Assert.Equal("先存一下", draft.Comment);

        // 范围不满足的值保留（提交时再校验）
        var partial = await FormsAsync(f => f.SaveDraftAsync("alice", "leave", draft.Id, Json("""{ "days": 0.1 }"""), null));
        Assert.Equal(draft.Id, partial.Id);
        Assert.Equal(0.1m, Json(partial.FormDataJson).GetProperty("days").GetDecimal());
        Assert.Single(await FormsAsync(f => f.ListDraftsAsync("alice")));
    }

    [Fact]
    public async Task Submitting_from_draft_deletes_it_in_the_same_transaction()
    {
        var draft = await FormsAsync(f => f.SaveDraftAsync("alice", "leave", null, Json("""{ "days": 2 }"""), null));

        // 校验失败：发起失败，草稿保留
        await Assert.ThrowsAsync<ApprovalValidationException>(() =>
            FormsAsync(f => f.StartAsync("leave", "alice", Json("""{ "days": 2 }"""), draftId: draft.Id)));
        Assert.Single(await FormsAsync(f => f.ListDraftsAsync("alice")));

        var instance = await FormsAsync(f => f.StartAsync("leave", "alice", Json("""{ "leaveType": "annual", "days": 2 }"""), draftId: draft.Id));

        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Empty(await FormsAsync(f => f.ListDraftsAsync("alice")));
    }

    [Fact]
    public async Task Other_users_drafts_are_not_found()
    {
        var draft = await FormsAsync(f => f.SaveDraftAsync("alice", "leave", null, Json("{}"), null));

        foreach (var action in new Func<ApprovalFormService, Task>[]
                 {
                     f => f.GetDraftAsync("bob", draft.Id),
                     f => f.DeleteDraftAsync("bob", draft.Id),
                     f => f.SaveDraftAsync("bob", "leave", draft.Id, Json("{}"), null),
                     f => f.StartAsync("leave", "bob", Json("""{ "leaveType": "annual", "days": 1 }"""), draftId: draft.Id)
                 })
        {
            var ex = await Assert.ThrowsAsync<ApprovalException>(() => FormsAsync(async f => { await action(f); return 0; }));
            Assert.Equal(ApprovalErrorCode.NotFound, ex.Code);
        }
        Assert.Single(await FormsAsync(f => f.ListDraftsAsync("alice")));
    }

    [Fact]
    public async Task Draft_count_and_size_are_limited()
    {
        for (var i = 0; i < ApprovalFormService.MaxDraftsPerUser; i++)
            await FormsAsync(f => f.SaveDraftAsync("alice", "leave", null, Json("{}"), null));

        var tooMany = await Assert.ThrowsAsync<ApprovalException>(() => FormsAsync(f => f.SaveDraftAsync("alice", "leave", null, Json("{}"), null)));
        Assert.Contains("最多保存", tooMany.Message);

        var existing = (await FormsAsync(f => f.ListDraftsAsync("alice")))[0].Id;
        var huge = JsonSerializer.Serialize(new { reason = new string('长', 100_000) });
        var tooBig = await Assert.ThrowsAsync<ApprovalException>(() => FormsAsync(f => f.SaveDraftAsync("alice", "leave", existing, Json(huge), null)));
        Assert.Contains("KB", tooBig.Message);
    }

    [Fact]
    public async Task Opening_after_flow_update_adjusts_to_latest_form()
    {
        var draft = await FormsAsync(f => f.SaveDraftAsync("alice", "leave", null, Json("""{ "leaveType": "personal", "days": 3, "reason": "家事" }"""), null));
        var unchanged = await FormsAsync(f => f.GetDraftAsync("alice", draft.Id));
        Assert.False(unchanged.Adjusted);

        // 新版本：删除 reason，days 改为文本，新增 handover
        await _host.PublishAsync("leave", LeaveFlow
            .Replace("""{ "key": "reason", "label": "事由", "type": "TextArea" }""", """{ "key": "handover", "label": "交接人", "type": "User" }""")
            .Replace("""{ "key": "days", "label": "天数", "type": "Number", "required": true, "min": 0.5 }""",
                """{ "key": "days", "label": "天数", "type": "Text" }"""));

        var adjusted = await FormsAsync(f => f.GetDraftAsync("alice", draft.Id));

        Assert.True(adjusted.Adjusted);
        var data = Json(adjusted.FormDataJson);
        Assert.Equal("personal", data.GetProperty("leaveType").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("days").ValueKind);   // 数字不再是文本类型：丢弃
        Assert.Equal(JsonValueKind.Null, data.GetProperty("handover").ValueKind);
        Assert.False(data.TryGetProperty("reason", out _));
    }
}
