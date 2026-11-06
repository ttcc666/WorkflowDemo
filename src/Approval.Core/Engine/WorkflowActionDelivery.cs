using System.Text.Json;
using Approval.Core.Abstractions;

namespace Approval.Core.Engine;

internal sealed record WorkflowActionDelivery(int Version, string Type, ApprovalActionContext Context, JsonElement Config, string? DestinationStamp)
{
    internal const string Target = "WorkflowAction";
    internal const int MaxPayloadBytes = 512 * 1024;
}

// Only fixed engine codes and HTTP status numbers may cross the action delivery error boundary.
internal sealed class WorkflowActionDeliveryException(string code, int? httpStatus = null)
    : Exception(httpStatus is { } status ? $"{code} ({status})" : code);
