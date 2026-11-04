using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Persistence;
using Approval.Forms.Attachments;
using Microsoft.EntityFrameworkCore;
namespace Approval.Forms.Runtime;
internal sealed class FormResourceResolver(IApprovalHostContext accessor) : IApprovalResourceResolver
{
    public async Task<ApprovalResource> ResolveAsync(ApprovalResource resource, CancellationToken ct)
    {
        var db = accessor.Context;
        if (resource.FileId is { } fileId)
        {
            var file = await db.Set<ApprovalFile>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, ct)
                ?? throw new ApprovalException("资源不存在") { Code = ApprovalErrorCode.NotFound };
            resource = resource with { InstanceId = file.InstanceId, TargetUserId = file.UploadedBy };
        }
        if (resource.DraftId is { } draftId)
        {
            var draft = await db.Set<ApprovalDraft>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == draftId, ct)
                ?? throw new ApprovalException("资源不存在") { Code = ApprovalErrorCode.NotFound };
            resource = resource with { DefinitionKey = draft.DefinitionKey, TargetUserId = draft.UserId };
        }
        return resource;
    }
}
