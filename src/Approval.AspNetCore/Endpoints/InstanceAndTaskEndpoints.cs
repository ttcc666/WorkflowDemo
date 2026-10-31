using System.Text.Json;
using Approval.Core;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

/// <summary>工作台与实例：待办、已办、我发起的、详情、撤回。</summary>
internal static class InstanceEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/todo", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetTodoAsync(http.GetApprovalUserId(), ct));

        group.MapGet("/done", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetDoneAsync(http.GetApprovalUserId(), ct));

        group.MapGet("/started", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetStartedByAsync(http.GetApprovalUserId(), ct));

        // 详情、按业务查询、实例的流程定义：只对有关的人可见（IApprovalAccessPolicy），无权时与不存在一样返回 404
        group.MapGet("/instances/{id:guid}", async (Guid id, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            await q.GetInstanceForViewerAsync(id, await http.GetApprovalViewerAsync(ct), ct) ?? throw NotVisible());

        group.MapGet("/instances", async (string businessType, string businessKey, HttpContext http, ApprovalQueryService q,
                CancellationToken ct) =>
            await q.GetByBusinessForViewerAsync(businessType, businessKey, await http.GetApprovalViewerAsync(ct), ct));

        // 实例绑定版本的流程定义公开视图（画流程图、展示表单），不含动作参数、通知与时限配置
        group.MapGet("/instances/{id:guid}/definition", async (Guid id, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            await q.GetPublicDefinitionAsync(id, await http.GetApprovalViewerAsync(ct), ct) ?? throw NotVisible());

        // 当前用户在该流程中可处理的任务；前端据此显示操作按钮
        group.MapGet("/instances/{id:guid}/my-task", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            new { task = await e.FindHandleableTaskAsync(id, http.GetApprovalUserId(), ct) });

        group.MapPost("/instances/{id:guid}/withdraw", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.WithdrawAsync(id, http.GetApprovalUserId(), body.Comment, ct);
            return Results.NoContent();
        });
    }

    private static ApprovalException NotVisible() => new("审批不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
}

/// <summary>任务操作：同意、驳回 / 退回、转交、加签、重新提交。</summary>
internal static class TaskEndpoints
{
    public static void Map(RouteGroupBuilder group, bool formsEnabled)
    {
        group.MapGet("/tasks/{id:guid}/can-handle", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            new { canHandle = await e.CanHandleAsync(id, http.GetApprovalUserId(), ct) });

        group.MapPost("/tasks/{id:guid}/approve", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.ApproveAsync(id, http.GetApprovalUserId(), body.Comment, ct);
            return Results.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/reject", async (Guid id, RejectRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            var userId = http.GetApprovalUserId();
            if (string.IsNullOrWhiteSpace(body.TargetNodeId))
                await e.RejectAsync(id, userId, body.Comment ?? "", ct);
            else
                await e.ReturnAsync(id, userId, body.Comment ?? "", body.TargetNodeId, ct);
            return Results.NoContent();
        });

        group.MapGet("/tasks/{id:guid}/return-targets", (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            e.GetReturnTargetsAsync(id, http.GetApprovalUserId(), ct));

        group.MapPost("/tasks/{id:guid}/add-sign", async (Guid id, AddSignRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.AddSignAsync(id, http.GetApprovalUserId(), body.UserIds ?? [], body.Mode, body.Comment, ct);
            return Results.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/transfer", async (Guid id, TransferRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.TransferAsync(id, http.GetApprovalUserId(), body.ToUserId, body.Comment, ct);
            return Results.NoContent();
        });

        // 退回发起人后重新提交；本接口只提交意见，业务数据由业务单据决定。启用表单审批时由 FormEndpoints 提供（可带表单数据）
        if (formsEnabled) return;
        group.MapPost("/tasks/{id:guid}/resubmit", async (Guid id, ResubmitRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            if (body.FormData is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
                throw new ApprovalException("未启用表单审批，重新提交不能修改表单数据");
            await e.ResubmitAsync(id, http.GetApprovalUserId(), body.Comment, ct: ct);
            return Results.NoContent();
        });
    }
}
