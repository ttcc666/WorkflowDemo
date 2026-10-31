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

    /// <summary>是否管理员：配置了 <see cref="ApprovalAspNetCoreOptions.AdminPolicy"/> 时按授权策略，否则按角色。</summary>
    public static async Task<bool> IsApprovalAdminAsync(this HttpContext http, CancellationToken ct = default)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<ApprovalAspNetCoreOptions>();
        if (!string.IsNullOrEmpty(options.AdminPolicy))
        {
            var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, options.AdminPolicy);
            return result.Succeeded;
        }

        return await services.GetRequiredService<IApprovalIdentityProvider>().IsInRoleAsync(http.GetApprovalUserId(), options.AdminRole, ct);
    }

    public static async Task EnsureApprovalAdminAsync(this HttpContext http, string message, CancellationToken ct = default)
    {
        if (!await http.IsApprovalAdminAsync(ct))
            throw new ApprovalException(message) { Code = ApprovalErrorCode.Forbidden };
    }

    internal static async Task<string> ReadBodyAsStringAsync(this HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }
}
