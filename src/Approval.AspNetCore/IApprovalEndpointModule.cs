using System.Text.Json;
using Approval.Core.Domain;
using Microsoft.AspNetCore.Routing;
namespace Approval.AspNetCore;
public interface IApprovalEndpointModule
{
    string BusinessType { get; }
    void Map(RouteGroupBuilder group);
}
public interface IApprovalTaskDataAdapter
{
    Task<IReadOnlyList<string>> GetEditableFieldsAsync(ApprovalTask task, CancellationToken ct);
    Task ResubmitAsync(Guid taskId, string userId, JsonElement? data, string? comment, CancellationToken ct);
    Task ApproveAsync(Guid taskId, string userId, JsonElement data, string? comment, CancellationToken ct);
}
