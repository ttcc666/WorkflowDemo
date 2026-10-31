namespace Approval.AspNetCore;

public sealed class ApprovalAspNetCoreOptions
{
    /// <summary>
    /// 管理操作（发布流程、投递记录）所需的授权策略名。设置后优先使用 ASP.NET Core 授权，
    /// 宿主需自行 <c>AddAuthorization(o =&gt; o.AddPolicy(...))</c>。
    /// </summary>
    public string? AdminPolicy { get; set; }

    /// <summary>未设置 <see cref="AdminPolicy"/> 时，经 <c>IApprovalIdentityProvider.IsInRoleAsync</c> 判断的管理员角色。</summary>
    public string AdminRole { get; set; } = "Admin";

    /// <summary>是否映射投递记录（Outbox）接口。</summary>
    public bool EnableOutboxEndpoints { get; set; } = true;

    /// <summary>
    /// 是否在 <c>HttpJsonOptions</c> 中添加 <c>JsonStringEnumConverter</c>（已存在时跳过）。
    /// 前端组件库依赖字符串枚举；宿主若已有其他约定可关闭后自行配置。
    /// </summary>
    public bool UseStringEnums { get; set; } = true;
}
