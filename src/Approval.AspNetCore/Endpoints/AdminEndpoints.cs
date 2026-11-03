using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

/// <summary>管理接口（/admin/*，全部仅管理员）：流程监控、终止、改派、离职交接、统计。</summary>
internal static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        // 全部流程：分页 + 状态、流程、发起人、关键字、发起时间筛选
        admin.MapGet("/instances", ([AsParameters] InstanceListRequest request, ApprovalQueryService q, CancellationToken ct) =>
            q.SearchInstancesAsync(request.ToQuery(), request.ToPage(), ct));

        admin.MapPost("/instances/{id:guid}/terminate", async (Guid id, ReasonRequest body, HttpContext http, ApprovalEngine e,
            CancellationToken ct) =>
        {
            await e.TerminateAsync(id, http.GetApprovalUserId(), body.Reason ?? "", ct);
            return TypedResults.NoContent();
        });

        admin.MapPost("/tasks/{id:guid}/reassign", async (Guid id, ReassignRequest body, HttpContext http, ApprovalEngine e,
            CancellationToken ct) =>
        {
            await e.ReassignAsync(id, http.GetApprovalUserId(), body.ToUserId, body.Reason, ct);
            return TypedResults.NoContent();
        });

        // 离职交接：先预览某人的未完成待办，再批量改派
        admin.MapGet("/users/{userId}/tasks", (string userId, int? page, int? pageSize, ApprovalQueryService q, CancellationToken ct) =>
            q.GetOpenUserTasksAsync(userId, new PageRequest(page, pageSize), ct));

        admin.MapPost("/users/{userId}/reassign", (string userId, ReassignRequest body, HttpContext http, ApprovalEngine e,
            CancellationToken ct) =>
            e.ReassignUserAsync(userId, body.ToUserId, http.GetApprovalUserId(), body.Reason, ct));

        // 统计：默认最近 30 天
        admin.MapGet("/statistics", (DateTimeOffset? from, DateTimeOffset? to, string? definitionKey, ApprovalStatisticsService s,
            TimeProvider clock, CancellationToken ct) =>
        {
            var end = to?.UtcDateTime ?? clock.GetUtcNow().UtcDateTime;
            var start = from?.UtcDateTime ?? end.AddDays(-30);
            return s.GetStatisticsAsync(new StatisticsQuery(start, end, definitionKey), ct);
        });
    }
}
