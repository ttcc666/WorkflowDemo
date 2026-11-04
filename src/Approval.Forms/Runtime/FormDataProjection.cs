using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Forms.Schema;

namespace Approval.Forms.Runtime;

internal sealed class FormDataProjection : IApprovalDataProjection
{
    public ApprovalDataView Project(FlowDefinition definition, ApprovalDataProjectionContext context, ApprovalAccess access)
    {
        var hidden = access.Level == ApprovalAccessLevel.Participant && access.VisibleViaNodes.Count > 0
            ? FieldVisibility.HiddenFields(definition, access.VisibleViaNodes) : FieldVisibility.None;
        return new(FieldVisibility.RemoveFields(context.Instance.FormDataJson, hidden), FieldVisibility.FilterLogs(context.Logs, hidden),
            hidden.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
