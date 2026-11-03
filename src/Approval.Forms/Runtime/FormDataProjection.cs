using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Forms.Schema;
namespace Approval.Forms.Runtime;
internal sealed class FormDataProjection : IApprovalDataProjection
{
    public ApprovalDataView Project(FlowDefinition definition, ApprovalInstance instance, ApprovalAccess access)
    {
        var hidden = access.Level == ApprovalAccessLevel.Participant && access.VisibleViaNodes.Count > 0
            ? FieldVisibility.HiddenFields(definition, access.VisibleViaNodes) : FieldVisibility.None;
        return new(FieldVisibility.RemoveFields(instance.FormDataJson, hidden), FieldVisibility.FilterLogs(instance.Logs, hidden),
            hidden.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
