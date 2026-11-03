using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

internal static class MaintenanceEndpoints
{
    public sealed record CreateMaintenanceJobRequest(MaintenanceTarget Target, MaintenanceMode Mode, string PolicyHash);
    public sealed record RetentionHoldRequest(MaintenanceTarget Target, Guid ResourceId, bool Held, string Reason);
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/diagnostics", (ApprovalDiagnosticsService service, CancellationToken ct) => service.CheckAsync(ct));
        group.MapGet("/maintenance/preview", (MaintenanceTarget target, MaintenanceMode mode, ApprovalLifecycleService service, CancellationToken ct) => service.PreviewAsync(target, mode, ct));
        group.MapGet("/maintenance/jobs", (ApprovalLifecycleService service, CancellationToken ct) => service.ListJobsAsync(ct));
        group.MapGet("/maintenance/jobs/{id:guid}/batches", (Guid id, int? page, ApprovalLifecycleService service, CancellationToken ct) => service.ListBatchesAsync(id, new PageRequest(page), ct));
        group.MapPost("/maintenance/jobs", (CreateMaintenanceJobRequest request, ApprovalLifecycleService service, CancellationToken ct) => service.CreateJobAsync(request.Target, request.Mode, request.PolicyHash, ct));
        group.MapPost("/maintenance/jobs/{id:guid}/run", (Guid id, ApprovalLifecycleService service, CancellationToken ct) => service.RunJobAsync(id, ct: ct));
        group.MapPost("/maintenance/holds", (RetentionHoldRequest request, ApprovalLifecycleService service, CancellationToken ct) => service.SetHoldAsync(request.Target, request.ResourceId, request.Held, request.Reason, ct));
        group.MapGet("/archives", (int? page, int? pageSize, ApprovalLifecycleService service, CancellationToken ct) => service.ListArchivesAsync(new PageRequest(page, pageSize), ct));
        group.MapGet("/archives/{id:guid}/export", async (Guid id, ApprovalLifecycleService service, CancellationToken ct) =>
            Results.Text(await service.ExportArchiveAsync(id, ct), "application/json")).Produces<string>(contentType: "application/json");
        group.MapGet("/archives/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, ApprovalLifecycleService service, CancellationToken ct) =>
            Results.Stream(await service.OpenArchiveFileAsync(id, fileId, ct), "application/octet-stream")).Produces<byte[]>(StatusCodes.Status200OK, contentType: "application/octet-stream");
    }
}
