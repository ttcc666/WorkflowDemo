using Approval.Core;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Certificate.Api.Infrastructure;

namespace Certificate.Api.Endpoints;

public sealed record CommentRequest(string? Comment);

public sealed record TransferRequest(string ToUserId, string? Comment);

/// <summary>通用审批 API：与具体业务无关，所有接入框架的业务共用。</summary>
public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approval").WithTags("Approval");

        // ───── 工作台 ─────
        group.MapGet("/todo", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetTodoAsync(CurrentUser.Get(http), ct));

        group.MapGet("/done", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetDoneAsync(CurrentUser.Get(http), ct));

        group.MapGet("/started", (HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
            q.GetStartedByAsync(CurrentUser.Get(http), ct));

        // ───── 实例 ─────
        group.MapGet("/instances/{id:guid}", async (Guid id, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
        {
            CurrentUser.Get(http);
            return await q.GetInstanceAsync(id, ct) is { } instance ? Results.Ok(instance) : Results.NotFound();
        });

        group.MapGet("/instances", (string businessType, string businessKey, HttpContext http, ApprovalQueryService q, CancellationToken ct) =>
        {
            CurrentUser.Get(http);
            return q.GetByBusinessAsync(businessType, businessKey, ct);
        });

        // 当前用户在该流程中可处理的任务；前端据此显示操作按钮
        group.MapGet("/instances/{id:guid}/my-task", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            new { task = await e.FindHandleableTaskAsync(id, CurrentUser.Get(http), ct) });

        group.MapPost("/instances/{id:guid}/withdraw", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.WithdrawAsync(id, CurrentUser.Get(http), body.Comment, ct);
            return Results.NoContent();
        });

        // ───── 任务 ─────
        group.MapGet("/tasks/{id:guid}/can-handle", async (Guid id, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
            new { canHandle = await e.CanHandleAsync(id, CurrentUser.Get(http), ct) });

        group.MapPost("/tasks/{id:guid}/approve", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.ApproveAsync(id, CurrentUser.Get(http), body.Comment, ct);
            return Results.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/reject", async (Guid id, CommentRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.RejectAsync(id, CurrentUser.Get(http), body.Comment ?? "", ct);
            return Results.NoContent();
        });

        group.MapPost("/tasks/{id:guid}/transfer", async (Guid id, TransferRequest body, HttpContext http, ApprovalEngine e, CancellationToken ct) =>
        {
            await e.TransferAsync(id, CurrentUser.Get(http), body.ToUserId, body.Comment, ct);
            return Results.NoContent();
        });

        // ───── 流程定义 ─────
        // 设计器元数据：审批人类型、通知接收人类型、动作（含参数描述）、渠道、模板变量
        group.MapGet("/meta", (ApprovalDefinitionService d) => d.Metadata);

        group.MapGet("/definitions", (ApprovalDefinitionService d, CancellationToken ct) => d.ListLatestAsync(ct));

        group.MapGet("/definitions/by-id/{id:guid}", async (Guid id, ApprovalDefinitionService d, CancellationToken ct) =>
            await d.GetByIdAsync(id, ct) is { } definition ? Results.Ok(definition) : Results.NotFound());

        group.MapGet("/definitions/{key}", async (string key, ApprovalDefinitionService d, CancellationToken ct) =>
            await d.GetLatestAsync(key, ct) is { } definition ? Results.Ok(definition) : Results.NotFound());

        group.MapGet("/definitions/{key}/versions", (string key, ApprovalDefinitionService d, CancellationToken ct) =>
            d.ListVersionsAsync(key, ct));

        // 只校验不发布，供设计器实时提示
        group.MapPost("/definitions/validate", async (HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(new { errors = d.Validate(FlowDefinition.Parse(await ReadBodyAsync(http, ct))) });
            }
            catch (ApprovalException ex)
            {
                return Results.Ok(new { errors = new[] { ex.Message } });
            }
        });

        // 发布新版本：请求体为流程定义 JSON；仅 Admin 角色可操作
        group.MapPost("/definitions/{key}", async (string key, string? name, HttpContext http,
            ApprovalDefinitionService d, DemoIdentityProvider identity, CancellationToken ct) =>
        {
            var userId = CurrentUser.Get(http);
            if (!await identity.IsInRoleAsync(userId, "Admin", ct))
                throw new ApprovalException("仅管理员可发布流程") { Code = ApprovalErrorCode.Forbidden };

            var definition = await d.PublishAsync(key, string.IsNullOrWhiteSpace(name) ? key : name, await ReadBodyAsync(http, ct), ct);
            return Results.Ok(new { definition.Id, definition.Key, definition.Version });
        });

        // ───── 站内信 ─────
        group.MapGet("/messages", (bool? unreadOnly, int? take, HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            m.ListAsync(CurrentUser.Get(http), unreadOnly ?? false, take ?? 50, ct));

        group.MapGet("/messages/unread-count", async (HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            new { count = await m.UnreadCountAsync(CurrentUser.Get(http), ct) });

        group.MapPost("/messages/{id:guid}/read", async (Guid id, HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
        {
            await m.MarkReadAsync(CurrentUser.Get(http), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/messages/read-all", async (HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            new { count = await m.MarkAllReadAsync(CurrentUser.Get(http), ct) });

        // ───── 投递记录（Outbox）：仅 Admin ─────
        group.MapGet("/outbox", async (OutboxStatus? status, Guid? instanceId, HttpContext http, ApprovalOutboxService o,
            DemoIdentityProvider identity, CancellationToken ct) =>
        {
            await EnsureAdminAsync(http, identity, ct);
            return await o.ListAsync(new OutboxQuery(status, instanceId), ct);
        });

        group.MapPost("/outbox/{id:guid}/retry", async (Guid id, HttpContext http, ApprovalOutboxService o, ApprovalOutboxSignal signal,
            DemoIdentityProvider identity, CancellationToken ct) =>
        {
            await EnsureAdminAsync(http, identity, ct);
            await o.RetryAsync(id, ct);
            signal.Notify();
            return Results.NoContent();
        });
    }

    private static async Task EnsureAdminAsync(HttpContext http, DemoIdentityProvider identity, CancellationToken ct)
    {
        if (!await identity.IsInRoleAsync(CurrentUser.Get(http), "Admin", ct))
            throw new ApprovalException("仅管理员可查看投递记录") { Code = ApprovalErrorCode.Forbidden };
    }

    private static async Task<string> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }
}
