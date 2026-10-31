using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore;

/// <summary>审批接口的请求上下文辅助方法，宿主自有接口（如业务提交）也可使用。</summary>
public static class ApprovalHttpContextExtensions
{
    private const string UserIdKey = "Approval.UserId";
    private const string IsAdminKey = "Approval.IsAdmin";

    /// <summary>当前用户 ID；未登录时抛出 <see cref="ApprovalErrorCode.Unauthorized"/>。</summary>
    public static string GetApprovalUserId(this HttpContext http)
    {
        if (http.Items.TryGetValue(UserIdKey, out var cached) && cached is string id) return id;

        var userId = http.RequestServices.GetRequiredService<IApprovalCurrentUser>().GetUserId(http);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ApprovalException("未登录或无法识别当前用户") { Code = ApprovalErrorCode.Unauthorized };

        http.Items[UserIdKey] = userId;
        return userId;
    }

    /// <summary>是否管理员：配置了 <see cref="ApprovalAspNetCoreOptions.AdminPolicy"/> 时按授权策略，否则按角色。同一请求内缓存结果。</summary>
    public static async Task<bool> IsApprovalAdminAsync(this HttpContext http, CancellationToken ct = default)
    {
        if (http.Items.TryGetValue(IsAdminKey, out var cached) && cached is bool value) return value;

        var services = http.RequestServices;
        var options = services.GetRequiredService<ApprovalAspNetCoreOptions>();
        var isAdmin = !string.IsNullOrEmpty(options.AdminPolicy)
            ? (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, options.AdminPolicy)).Succeeded
            : await services.GetRequiredService<IApprovalIdentityProvider>().IsInRoleAsync(http.GetApprovalUserId(), options.AdminRole, ct);

        http.Items[IsAdminKey] = isAdmin;
        return isAdmin;
    }

    public static async Task EnsureApprovalAdminAsync(this HttpContext http, string message, CancellationToken ct = default)
    {
        if (!await http.IsApprovalAdminAsync(ct))
            throw new ApprovalException(message) { Code = ApprovalErrorCode.Forbidden };
    }

    /// <summary>当前查看者（用户 + 是否管理员），用于带权限的查询，如 <c>ApprovalQueryService.GetInstanceForViewerAsync</c>。</summary>
    public static async Task<ApprovalViewer> GetApprovalViewerAsync(this HttpContext http, CancellationToken ct = default) =>
        new(http.GetApprovalUserId(), await http.IsApprovalAdminAsync(ct));

    /// <summary>能否读取完整流程定义：管理员，或属于 <see cref="ApprovalAspNetCoreOptions.DefinitionReaders"/> 中的角色。</summary>
    public static async Task<bool> CanReadApprovalDefinitionsAsync(this HttpContext http, CancellationToken ct = default)
    {
        if (await http.IsApprovalAdminAsync(ct)) return true;

        var services = http.RequestServices;
        var identity = services.GetRequiredService<IApprovalIdentityProvider>();
        foreach (var role in services.GetRequiredService<ApprovalAspNetCoreOptions>().DefinitionReaders)
        {
            if (await identity.IsInRoleAsync(http.GetApprovalUserId(), role, ct)) return true;
        }
        return false;
    }

    internal static async Task<string> ReadBodyAsStringAsync(this HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }
}
