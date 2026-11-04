namespace Approval.AspNetCore;

public sealed class ApprovalAspNetCoreOptions
{
    public Dictionary<string, string> PermissionPolicies { get; } = new(StringComparer.Ordinal);

    /// <summary>是否映射投递记录（Outbox）接口。</summary>
    public bool EnableOutboxEndpoints { get; set; } = true;

    /// <summary>
    /// 是否在 <c>HttpJsonOptions</c> 中添加 <c>JsonStringEnumConverter</c>（已存在时跳过）。
    /// 前端组件库依赖字符串枚举；宿主若已有其他约定可关闭后自行配置。
    /// </summary>
    public bool UseStringEnums { get; set; } = true;
}
