namespace Approval.Core.Abstractions;

/// <summary>当前租户的展示与模板配置；不包含宿主 HTTP 白名单、脚本限制等安全选项。</summary>
public sealed record ApprovalTenantSettings(TimeZoneInfo DisplayTimeZone, string? InstanceUrlFormat, IReadOnlyDictionary<string, string> GlobalVariables);
public interface IApprovalTenantSettingsProvider { ApprovalTenantSettings GetSettings(); }
