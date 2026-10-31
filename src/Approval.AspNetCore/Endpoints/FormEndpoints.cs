using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

/// <summary>表单审批：发起中心、发起页、提交（需 <c>AddFormApprovals()</c>）。</summary>
internal static class FormEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/forms/startable", (HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.ListStartableAsync(http.GetApprovalUserId(), ct));

        group.MapGet("/forms/{key}", (string key, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.GetStartFormAsync(key, http.GetApprovalUserId(), ct));

        // 校验失败返回 400 + errors（字段路径 → 错误），前端回显到对应表单项
        group.MapPost("/forms/{key}/start", async (string key, StartFormRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            var instance = await forms.StartAsync(key, http.GetApprovalUserId(), body.FormData, body.Comment, ct);
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
