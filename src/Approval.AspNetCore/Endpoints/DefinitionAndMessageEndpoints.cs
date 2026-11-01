using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

/// <summary>流程定义：设计器元数据、列表、版本、校验、发布。完整定义含动作参数与通知配置，仅管理员（及 DefinitionReaders）可读。</summary>
internal static class DefinitionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // 设计器元数据：审批人类型、通知接收人类型、动作（含参数描述）、渠道、模板变量（不含敏感配置，所有登录用户可读）
        group.MapGet("/meta", (ApprovalDefinitionService d) => d.Metadata);

        // 流程标识与名称（列表的流程筛选），所有登录用户可读
        group.MapGet("/flows", (ApprovalDefinitionService d, CancellationToken ct) => d.ListFlowsAsync(ct));

        group.MapGet("/definitions", async (HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            await EnsureReaderAsync(http, ct);
            return await d.ListLatestAsync(ct);
        });

        group.MapGet("/definitions/by-id/{id:guid}", async (Guid id, HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            await EnsureReaderAsync(http, ct);
            return await d.GetByIdAsync(id, ct) is { } definition ? Results.Ok(definition) : Results.NotFound();
        });

        group.MapGet("/definitions/{key}", async (string key, HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            await EnsureReaderAsync(http, ct);
            return await d.GetLatestAsync(key, ct) is { } definition ? Results.Ok(definition) : Results.NotFound();
        });

        group.MapGet("/definitions/{key}/versions", async (string key, HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            await EnsureReaderAsync(http, ct);
            return await d.ListVersionsAsync(key, ct);
        });

        // 只校验不发布，供设计器实时提示；解析失败也作为校验错误返回。warnings 为不阻止发布的提示
        group.MapPost("/definitions/validate", async (HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            try
            {
                var flow = FlowDefinition.Parse(await http.ReadBodyAsStringAsync(ct));
                return Results.Ok(new { errors = d.Validate(flow), warnings = d.Warnings(flow) });
            }
            catch (ApprovalException ex)
            {
                return Results.Ok(new { errors = new[] { ex.Message }, warnings = Array.Empty<string>() });
            }
        });

        // 发布新版本：请求体为流程定义 JSON；仅管理员
        group.MapPost("/definitions/{key}", async (string key, string? name, HttpContext http, ApprovalDefinitionService d, CancellationToken ct) =>
        {
            await http.EnsureApprovalAdminAsync("仅管理员可发布流程", ct);
            var definition = await d.PublishAsync(key, string.IsNullOrWhiteSpace(name) ? key : name, await http.ReadBodyAsStringAsync(ct), ct);
            return Results.Ok(new { definition.Id, definition.Key, definition.Version });
        });
    }

    private static async Task EnsureReaderAsync(HttpContext http, CancellationToken ct)
    {
        if (!await http.CanReadApprovalDefinitionsAsync(ct))
            throw new ApprovalException("仅管理员可查看流程定义") { Code = ApprovalErrorCode.Forbidden };
    }
}

/// <summary>站内信与投递记录。</summary>
internal static class MessageEndpoints
{
    public static void Map(RouteGroupBuilder group, ApprovalAspNetCoreOptions options)
    {
        group.MapGet("/messages", (bool? unreadOnly, int? take, HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            m.ListAsync(http.GetApprovalUserId(), unreadOnly ?? false, Math.Clamp(take ?? 50, 1, 200), ct));

        group.MapGet("/messages/unread-count", async (HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            new { count = await m.UnreadCountAsync(http.GetApprovalUserId(), ct) });

        group.MapPost("/messages/{id:guid}/read", async (Guid id, HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
        {
            await m.MarkReadAsync(http.GetApprovalUserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/messages/read-all", async (HttpContext http, ApprovalMessageService m, CancellationToken ct) =>
            new { count = await m.MarkAllReadAsync(http.GetApprovalUserId(), ct) });

        if (!options.EnableOutboxEndpoints) return;

        // 投递记录（Outbox）：仅管理员
        group.MapGet("/outbox", async (OutboxStatus? status, Guid? instanceId, int? page, int? pageSize, HttpContext http,
            ApprovalOutboxService o, CancellationToken ct) =>
        {
            await http.EnsureApprovalAdminAsync("仅管理员可查看投递记录", ct);
            return await o.ListAsync(new OutboxQuery(status, instanceId), new PageRequest(page, pageSize), ct);
        });

        group.MapPost("/outbox/{id:guid}/retry", async (Guid id, HttpContext http, ApprovalOutboxService o, ApprovalOutboxSignal signal,
            CancellationToken ct) =>
        {
            await http.EnsureApprovalAdminAsync("仅管理员可重试投递", ct);
            await o.RetryAsync(id, ct);
            signal.Notify();
            return Results.NoContent();
        });
    }
}

/// <summary>当前用户与用户目录。</summary>
internal static class DirectoryEndpoints
{
    private const int MaxTake = 200;

    public static void Map(RouteGroupBuilder group, string? formBusinessType)
    {
        // 前端据此显示菜单与按钮：isAdmin（发布流程、投递记录）、canViewDefinitions（流程定义与设计器）、formsEnabled（发起申请）、
        // formBusinessType（该业务类型的申请被退回后可修改表单重新提交）
        group.MapGet("/me", async (HttpContext http, IApprovalIdentityProvider identity, CancellationToken ct) =>
        {
            var userId = http.GetApprovalUserId();
            var directory = http.RequestServices.GetService(typeof(IApprovalUserDirectory)) as IApprovalUserDirectory;
            var user = directory is null ? null : (await directory.GetUsersAsync([userId], ct)).FirstOrDefault();
            return new
            {
                userId,
                name = user?.Name ?? userId,
                roles = await identity.GetRolesAsync(userId, ct),
                isAdmin = await http.IsApprovalAdminAsync(ct),
                canViewDefinitions = await http.CanReadApprovalDefinitionsAsync(ct),
                formsEnabled = formBusinessType is not null,
                formBusinessType
            };
        });

        // ids=a,b 时按 ID 批量获取（回显已选用户），否则按关键字搜索
        group.MapGet("/directory/users", async (string? keyword, string? ids, int? take, HttpContext http, CancellationToken ct) =>
        {
            if (Directory(http) is not { } directory) return NotConfigured();
            if (!string.IsNullOrWhiteSpace(ids))
            {
                var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxTake).ToList();
                return Results.Ok(await directory.GetUsersAsync(list, ct));
            }
            return Results.Ok(await directory.SearchUsersAsync(keyword?.Trim(), Math.Clamp(take ?? 50, 1, MaxTake), ct));
        });

        group.MapGet("/directory/roles", async (HttpContext http, CancellationToken ct) =>
            Directory(http) is { } directory ? Results.Ok(await directory.ListRolesAsync(ct)) : NotConfigured());
    }

    private static IApprovalUserDirectory? Directory(HttpContext http) =>
        http.RequestServices.GetService(typeof(IApprovalHttpUserDirectory)) as IApprovalUserDirectory
        ?? http.RequestServices.GetService(typeof(IApprovalUserDirectory)) as IApprovalUserDirectory;

    private static IResult NotConfigured() =>
        Results.Problem(title: "宿主未注册用户目录（IApprovalUserDirectory）", statusCode: StatusCodes.Status501NotImplemented,
            extensions: new Dictionary<string, object?> { ["code"] = "DirectoryNotConfigured" });
}
