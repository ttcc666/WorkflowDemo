namespace Approval.Core.Domain;

/// <summary>可编辑设计草稿与发布计数；发布 JSON 仍保存在不可变的 ApprovalDefinition 中。</summary>
public sealed class ApprovalDefinitionManagement : IApprovalEntity
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }

    /// <summary>有草稿时为草稿分类，否则为当前选用发布版本的分类。</summary>
    public string? Category { get; set; }

    public string? DraftJson { get; set; }
    public Guid? DraftSourceDefinitionId { get; set; }
    public long Revision { get; set; }
    public long DraftRevision { get; set; }
    public long? PublishedDraftRevision { get; set; }
    public int LastVersion { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid? ActiveDefinitionId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

public enum DefinitionAuditAction
{ DraftSaved, Published, Enabled, Disabled, VersionSelected }

/// <summary>同事务落库的治理审计，不含流程 JSON、动作参数或凭据。</summary>
public sealed class ApprovalDefinitionAudit : IApprovalEntity
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public long Revision { get; set; }
    public DefinitionAuditAction Action { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? DefinitionId { get; set; }
    public Guid? SourceDefinitionId { get; set; }
    public int? Version { get; set; }
    public string? ChangeNote { get; set; }
    public string? DefinitionHash { get; set; }
}
