using Approval.AspNetCore;
using Approval.AspNetCore.Contracts;
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

/// <summary>已受理的注册证变更申请。</summary>
public sealed record CertificateSubmittedResponse(Guid RequestId);

/// <summary>注册证申请与经过权限过滤的审批快照。</summary>
public sealed record CertificateRequestDetailResponse(CertificateRequestView Request, IReadOnlyList<FieldChange> Changes,
    ApprovalInstanceResponse? Approval)
{
    public static CertificateRequestDetailResponse From(CertificateRequestDetail value) =>
        new(value.Request, value.Changes, value.Approval is null ? null : ApprovalInstanceResponse.From(value.Approval));
}

public static class CertificateEndpoints
{
    public static void MapCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Certificates");
        foreach (var status in new[] { 400, 401, 403, 404, 409 })
            api.WithMetadata(new ProducesResponseTypeMetadata(status, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), ["application/problem+json"]));
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
            return TypedResults.Accepted($"/api/certificate-requests/{request.Id}", new CertificateSubmittedResponse(request.Id));
        });
        group.MapPut("/{id:guid}", async (Guid id, CertificateSubmission body, HttpContext http, CertificateService service, CancellationToken ct) =>
        {
            var request = await service.SubmitUpdateAsync(id, body.Data, await http.GetApprovalViewerAsync(ct),
                SubmissionKey(http), body.SourceRequestId, ct);
            return TypedResults.Accepted($"/api/certificate-requests/{request.Id}", new CertificateSubmittedResponse(request.Id));
        });
        group.MapPost("/{id:guid}/revoke", async (Guid id, RevokeRequest body, HttpContext http, CertificateService service, CancellationToken ct) =>
        {
            var request = await service.SubmitRevokeAsync(id, body.Reason, await http.GetApprovalViewerAsync(ct),
                SubmissionKey(http), body.SourceRequestId, ct);
            return TypedResults.Accepted($"/api/certificate-requests/{request.Id}", new CertificateSubmittedResponse(request.Id));
        });
        api.MapGet("/certificate-requests/{id:guid}", async (Guid id, HttpContext http, CertificateQueryService query, CancellationToken ct) =>
            CertificateRequestDetailResponse.From(await query.RequestAsync(id, await http.GetApprovalViewerAsync(ct), ct)));
    }

    private static string SubmissionKey(HttpContext http)
    {
        if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"], out var id) || id == Guid.Empty)
            throw new ApprovalValidationException(new Dictionary<string, string[]> { ["idempotencyKey"] = ["请提供有效的 Idempotency-Key 提交标识"] });
        return id.ToString("N");
    }
}
