using Approval.AspNetCore.Contracts;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Engine;
using Approval.Core.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Endpoints;

/// <summary>工作台与实例：待办、已办、我发起的、抄送我的（分页 + 筛选）、详情、撤回。</summary>
internal static class InstanceEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/todo", ([AsParameters] TodoListRequest request, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetTodoAsync(http.GetApprovalUserId(), request.ToQuery(), request.ToPage(), ct));

        // 工作台角标：只取数量
        group.MapGet("/todo/count", async (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            new CountResponse(await q.GetTodoCountAsync(http.GetApprovalUserId(), ct)));

        group.MapGet("/done", ([AsParameters] InstanceListRequest request, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetDoneAsync(http.GetApprovalUserId(), request.ToQuery(), request.ToPage(), ct));

        group.MapGet("/started", ([AsParameters] InstanceListRequest request, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetStartedByAsync(http.GetApprovalUserId(), request.ToQuery(), request.ToPage(), ct));

        group.MapGet("/cc", ([AsParameters] InstanceListRequest request, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetCarbonCopiedAsync(http.GetApprovalUserId(), request.ToQuery(), request.ToPage(), ct));

        // 详情、按业务查询、实例的流程定义：只对有关的人可见（IApprovalAccessPolicy），无权时与不存在一样返回 404
        group.MapGet("/instances/{id:guid}", async (Guid id, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            await ResponseAsync(await q.GetInstanceForViewerAsync(id, await http.GetApprovalViewerAsync(ct), ct) ?? throw NotVisible(), http, ct));

        group.MapGet("/instances", async (string businessType, string businessKey, HttpContext http, ApprovalQueryService q,
                CancellationToken ct) =>
            await ResponsesAsync(await q.GetByBusinessForViewerAsync(businessType, businessKey, await http.GetApprovalViewerAsync(ct), ct), http, ct));

        // 实例绑定版本的流程定义公开视图（画流程图、展示表单），不含动作参数、通知与时限配置
        group.MapGet("/instances/{id:guid}/definition", async (Guid id, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            ApprovalDefinitionResponse.From(await q.GetPublicDefinitionAsync(id, await http.GetApprovalViewerAsync(ct), ct) ?? throw NotVisible()));

        // 当前用户在该流程中可处理的任务；前端据此显示操作按钮。表单审批时同时返回该任务所在节点可在同意时修改的字段
        group.MapGet("/instances/{id:guid}/my-task", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            var task = await e.FindHandleableTaskAsync(id, http.GetApprovalUserId(), ct);
            var forms = task is null ? null : http.RequestServices.GetService<ApprovalFormService>();
            return new ApprovalMyTaskResponse(task is null ? null : ApprovalTaskResponse.From(task), forms is null ? [] : await forms.GetEditableFieldsAsync(task!, ct))
            { Capabilities = task is null ? new Dictionary<string, bool>() : await http.RequestServices.GetRequiredService<ApprovalAuthorizationService>().CapabilitiesAsync(new() { TaskId = task.Id }, ct) };
        });

        group.MapPost("/instances/{id:guid}/withdraw", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.WithdrawAsync(id, http.GetApprovalUserId(), body.Comment, ct);
            return TypedResults.NoContent();
        });
    }

    private static async Task<ApprovalInstanceResponse[]> ResponsesAsync(IReadOnlyList<InstanceDetail> details, HttpContext http, CancellationToken ct)
    {
        var results = new List<ApprovalInstanceResponse>();
        foreach (var detail in details) results.Add(await ResponseAsync(detail, http, ct));
        return results.ToArray();
    }
    private static async Task<ApprovalInstanceResponse> ResponseAsync(InstanceDetail detail, HttpContext http, CancellationToken ct)
    {
        var authorization = http.RequestServices.GetRequiredService<ApprovalAuthorizationService>();
        var tasks = new List<ApprovalTaskResponse>();
        foreach (var task in detail.Tasks)
            tasks.Add(ApprovalTaskResponse.From(task) with { Capabilities = new Dictionary<string, bool>
            { [Approval.Core.Abstractions.ApprovalPermissions.TaskReassign] = await authorization.CanAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReassign, new() { TaskId = task.Id }, ct) } });
        return ApprovalInstanceResponse.From(detail) with { Tasks = tasks,
            Capabilities = await authorization.CapabilitiesAsync(new() { InstanceId = detail.Id }, ct) };
    }
    private static ApprovalException NotVisible() => new("审批不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
}

/// <summary>任务操作：同意、驳回 / 退回、转交、加签、重新提交。</summary>
internal static class TaskEndpoints
{
    public static void Map(RouteGroupBuilder group, bool formsEnabled)
    {
        group.MapGet("/tasks/{id:guid}/can-handle", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            new CanHandleResponse(await e.CanHandleAsync(id, http.GetApprovalUserId(), ct)));

        // 同意；表单审批可同时修改任务所在节点的可编辑字段（formData 只含这些字段）
        group.MapPost("/tasks/{id:guid}/approve", async (Guid id, ApproveRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            var userId = http.GetApprovalUserId();
            if (body.FormData is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } formData)
            {
                if (!formsEnabled) throw new ApprovalException("未启用表单审批，同意时不能修改表单数据");
                await http.RequestServices.GetRequiredService<ApprovalFormService>().ApproveAsync(id, userId, formData, body.Comment, ct);
            }
            else
            {
                await e.ApproveAsync(id, userId, body.Comment, ct);
            }
            return TypedResults.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/reject", async (Guid id, RejectRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            var userId = http.GetApprovalUserId();
            if (string.IsNullOrWhiteSpace(body.TargetNodeId))
                await e.RejectAsync(id, userId, body.Comment ?? "", ct);
            else
                await e.ReturnAsync(id, userId, body.Comment ?? "", body.TargetNodeId, ct);
            return TypedResults.NoContent();
        });

        group.MapGet("/tasks/{id:guid}/return-targets", (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            e.GetReturnTargetsAsync(id, http.GetApprovalUserId(), ct));

        group.MapPost("/tasks/{id:guid}/add-sign", async (Guid id, AddSignRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.AddSignAsync(id, http.GetApprovalUserId(), body.UserIds ?? [], body.Mode, body.Comment, ct);
            return TypedResults.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/transfer", async (Guid id, TransferRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.TransferAsync(id, http.GetApprovalUserId(), body.ToUserId, body.Comment, ct);
            return TypedResults.NoContent();
        });

        // 退回发起人后重新提交；本接口只提交意见，业务数据由业务单据决定。启用表单审批时由 FormEndpoints 提供（可带表单数据）
        if (formsEnabled) return;
        group.MapPost("/tasks/{id:guid}/resubmit", async (Guid id, ResubmitRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            if (body.FormData is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
                throw new ApprovalException("未启用表单审批，重新提交不能修改表单数据");
            await e.ResubmitAsync(id, http.GetApprovalUserId(), body.Comment, ct: ct);
            return TypedResults.NoContent();
        });
    }
}
