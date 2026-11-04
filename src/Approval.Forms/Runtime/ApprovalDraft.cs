using Approval.Core.Domain;

namespace Approval.Forms.Runtime;

public class ApprovalDraft : IApprovalEntity
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string DefinitionKey { get; set; }
    public string FormDataJson { get; set; } = "{}";
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
