using Approval.AspNetCore;
using Approval.Core;
using Approval.Core.Engine;
using Certificate.Api.Certificates;

namespace Certificate.Api.Endpoints;

public sealed record CertificateSubmission(string CertNo, string Name, string Holder, string CertType,
    DateOnly IssueDate, DateOnly ExpireDate, Guid? SourceRequestId = null)
{
    public CertificateData Data => new(CertNo, Name, Holder, CertType, IssueDate, ExpireDate);
}
public sealed record RevokeRequest(string? Reason, Guid? SourceRequestId = null);

public static class CertificateEndpoints
{
    public static void MapCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Certificates");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.GetApprovalUserId();
            return await next(context);
        });
        var group = api.MapGroup("/certificates");
        group.MapGet("/", async (int? page, int? pageSize, string? keyword, HttpContext http, CertificateQueryService query, CancellationToken ct) =>
            await query.ListAsync(await http.GetApprovalViewerAsync(ct), new PageRequest(page, pageSize), keyword, ct));
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, CertificateQueryService query, CancellationToken ct) =>
            await query.GetAsync(id, await http.GetApprovalViewerAsync(ct), ct));
        group.MapGet("/{id:guid}/requests", async (Guid id, int? page, int? pageSize, HttpContext http, CertificateQueryService query, CancellationToken ct) =>
            await query.HistoryAsync(id, await http.GetApprovalViewerAsync(ct), new PageRequest(page, pageSize), ct));

        group.MapPost("/", async (CertificateSubmission body, HttpContext http, CertificateService service, CancellationToken ct) =>
        {
            var request = await service.SubmitCreateAsync(body.Data, await http.GetApprovalViewerAsync(ct),
                SubmissionKey(http), body.SourceRequestId, ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });
        group.MapPut("/{id:guid}", async (Guid id, CertificateSubmission body, HttpContext http, CertificateService service, CancellationToken ct) =>
        {
            var request = await service.SubmitUpdateAsync(id, body.Data, await http.GetApprovalViewerAsync(ct),
                SubmissionKey(http), body.SourceRequestId, ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });
        group.MapPost("/{id:guid}/revoke", async (Guid id, RevokeRequest body, HttpContext http, CertificateService service, CancellationToken ct) =>
        {
            var request = await service.SubmitRevokeAsync(id, body.Reason, await http.GetApprovalViewerAsync(ct),
                SubmissionKey(http), body.SourceRequestId, ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });
        api.MapGet("/certificate-requests/{id:guid}", async (Guid id, HttpContext http, CertificateQueryService query, CancellationToken ct) =>
            await query.RequestAsync(id, await http.GetApprovalViewerAsync(ct), ct));
    }

    private static string SubmissionKey(HttpContext http)
    {
        if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var id) || id == Guid.Empty)
            throw new ApprovalValidationException(new Dictionary<string, string[]> { ["idempotencyKey"] = ["请提供有效的 Idempotency-Key 提交标识"] });
        return id.ToString("N");
    }
}
