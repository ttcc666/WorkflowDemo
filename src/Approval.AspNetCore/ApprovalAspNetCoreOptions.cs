namespace Approval.AspNetCore;

public sealed class ApprovalAspNetCoreOptions
{
    /// <summary>已认证身份中的当前企业 Claim；多个企业须先由宿主验证并选择唯一当前企业。</summary>
    public string TenantClaimType { get; set; } = "tenant_id";
    public Dictionary<string, string> PermissionPolicies { get; } = new(StringComparer.Ordinal);
    /// <summary>
    /// 管理操作（发布流程、投递记录）所需的授权策略名。设置后优先使用 ASP.NET Core 授权，
    /// 宿主需自行 <c>AddAuthorization(o =&gt; o.AddPolicy(...))</c>。
    /// </summary>
    public string? AdminPolicy { get; set; }

    /// <summary>未设置 <see cref="AdminPolicy"/> 时，经 <c>IApprovalIdentityProvider.IsInRoleAsync</c> 判断的管理员角色。</summary>
    public string AdminRole { get; set; } = "Admin";

    /// <summary>
    /// 除管理员外可读取完整流程定义（含动作参数、通知配置）的角色，如流程设计员；默认为空（仅管理员）。
    /// 发布流程仍需管理员。实例的查看者通过 <c>instances/{id}/definition</c> 读取去掉敏感配置的公开视图。
    /// </summary>
    public List<string> DefinitionReaders { get; } = [];

    /// <summary>是否映射投递记录（Outbox）接口。</summary>
    public bool EnableOutboxEndpoints { get; set; } = true;

    /// <summary>
    /// 是否在 <c>HttpJsonOptions</c> 中添加 <c>JsonStringEnumConverter</c>（已存在时跳过）。
    /// 前端组件库依赖字符串枚举；宿主若已有其他约定可关闭后自行配置。
    /// </summary>
    public bool UseStringEnums { get; set; } = true;
}
