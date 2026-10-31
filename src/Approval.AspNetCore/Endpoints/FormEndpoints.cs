using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

/// <summary>表单审批：发起中心、发起页、草稿、提交（需 <c>AddFormApprovals()</c>）。</summary>
internal static class FormEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/forms/startable", (HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.ListStartableAsync(http.GetApprovalUserId(), ct));

        // 草稿：只能读写自己的；他人的草稿与不存在一样返回 404
        group.MapGet("/forms/drafts", (HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.ListDraftsAsync(http.GetApprovalUserId(), ct));

        group.MapGet("/forms/drafts/{id:guid}", (Guid id, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.GetDraftAsync(http.GetApprovalUserId(), id, ct));

        group.MapDelete("/forms/drafts/{id:guid}", async (Guid id, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
        {
            await forms.DeleteDraftAsync(http.GetApprovalUserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPut("/forms/{key}/draft", async (string key, SaveDraftRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            var draft = await forms.SaveDraftAsync(http.GetApprovalUserId(), key, body.DraftId, body.FormData, body.Comment, ct);
            return Results.Ok(new { draftId = draft.Id, draft.UpdatedAt });
        });

        group.MapGet("/forms/{key}", (string key, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.GetStartFormAsync(key, http.GetApprovalUserId(), ct));

        // 校验失败返回 400 + errors（字段路径 → 错误），前端回显到对应表单项；带 draftId 时提交成功后删除草稿
        group.MapPost("/forms/{key}/start", async (string key, StartFormRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            var instance = await forms.StartAsync(key, http.GetApprovalUserId(), body.FormData, body.Comment, body.DraftId, ct);
            return Results.Ok(new { instanceId = instance.Id });
        });

        // 退回发起人后重新提交：表单审批可带新的表单数据，其他业务只提交意见
        group.MapPost("/tasks/{id:guid}/resubmit", async (Guid id, ResubmitRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            await forms.ResubmitAsync(id, http.GetApprovalUserId(), body.FormData, body.Comment, ct);
            return Results.NoContent();
        });
    }
}
