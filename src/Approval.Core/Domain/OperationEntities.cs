namespace Approval.Core.Domain;

/// <summary>与业务变化同时提交；不保存请求正文。没有自动过期，清理必须保留防重标记。</summary>
public sealed class ApprovalOperation : IApprovalEntity
{
    public Guid Id { get; set; }
    public required string LookupHash { get; set; }
    public required string ActorId { get; set; }
    public required string Kind { get; set; }
    public required string Scope { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public int HashVersion { get; set; } = 1;
    public int ResultVersion { get; set; } = 2;
    public string ResultJson { get; set; } = string.Empty;
    public Guid? InstanceId { get; set; }
    public Guid? DefinitionId { get; set; }
    public DateTime CommittedAt { get; set; }
    public bool IsTombstone { get; set; }
}

/// <summary>数据库唯一约束保证不同请求也不能同时占用同一业务；摘要使用原始、区分大小写的业务标识。</summary>
public sealed class ApprovalBusinessSlot : IApprovalEntity
{
    public Guid Id { get; set; }
    public required string LookupHash { get; set; }
    public required string BusinessType { get; set; }
    public required string BusinessKey { get; set; }
    public Guid InstanceId { get; set; }
}
