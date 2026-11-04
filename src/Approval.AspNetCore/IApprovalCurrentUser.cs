using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Approval.AspNetCore;

/// <summary>从请求中识别当前用户。返回 null 表示未登录，接口返回 401。</summary>
public interface IApprovalCurrentUser
{
    string? GetUserId(HttpContext http);
}

/// <summary>默认实现：读取已认证用户的 <see cref="ClaimTypes.NameIdentifier"/> 或 <c>sub</c> 声明。</summary>
public sealed class ClaimsApprovalCurrentUser : IApprovalCurrentUser
{
    public string? GetUserId(HttpContext http)
    {
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true) return null;
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
