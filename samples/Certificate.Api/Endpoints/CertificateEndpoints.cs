using Approval.AspNetCore;
using Approval.Core.Engine;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Endpoints;

public sealed record RevokeRequest(string Reason);

public sealed record FieldChange(string Field, object? Before, object? After);

/// <summary>证书业务 API：所有写操作都只生成申请单，审批通过后才生效。</summary>
public static class CertificateEndpoints
{
    public static void MapCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/certificates").WithTags("Certificates");

        group.MapGet("/", (AppDbContext db, CancellationToken ct) =>
            db.Certificates.AsNoTracking().OrderBy(c => c.CertNo).ToListAsync(ct));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Certificates.FindAsync([id], ct) is { } cert ? Results.Ok(cert) : Results.NotFound());

        // 证书的全部申请记录（新增、变更、注销）
        group.MapGet("/{id:guid}/requests", (Guid id, AppDbContext db, CancellationToken ct) =>
            db.CertificateRequests.AsNoTracking()
                .Where(r => r.CertificateId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct));

        group.MapPost("/", async (CertificateData data, HttpContext http, CertificateService s, CancellationToken ct) =>
        {
            var request = await s.SubmitCreateAsync(data, http.GetApprovalUserId(), ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });

        group.MapPut("/{id:guid}", async (Guid id, CertificateData data, HttpContext http, CertificateService s, CancellationToken ct) =>
        {
            var request = await s.SubmitUpdateAsync(id, data, http.GetApprovalUserId(), ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });

        group.MapPost("/{id:guid}/revoke", async (Guid id, RevokeRequest body, HttpContext http, CertificateService s, CancellationToken ct) =>
        {
            var request = await s.SubmitRevokeAsync(id, body.Reason, http.GetApprovalUserId(), ct);
            return Results.Accepted($"/api/certificate-requests/{request.Id}", new { requestId = request.Id });
        });

        // 申请详情：变更前后对比 + 审批轨迹，审核人据此判断改了什么。
        // 审批记录按审批的查看规则过滤（无权时为 null）；申请单本身属于业务数据，其权限由业务自行决定（演示中不限制）
        app.MapGet("/api/certificate-requests/{id:guid}", async (Guid id, HttpContext http, AppDbContext db, ApprovalQueryService q,
            CancellationToken ct) =>
        {
            var request = await db.CertificateRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (request is null) return Results.NotFound();

            var current = request.CertificateId is { } certId
                ? await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == certId, ct)
                : null;
            var approvals = await q.GetByBusinessForViewerAsync(CertificateService.BusinessType, id.ToString(),
                await http.GetApprovalViewerAsync(ct), ct);

            return Results.Ok(new
            {
                request,
                changes = Diff(current is null ? null : CertificateData.From(current), request.Snapshot),
                approval = approvals.FirstOrDefault()
            });
        }).WithTags("Certificates");
    }

    private static List<FieldChange> Diff(CertificateData? before, CertificateData after) =>
        typeof(CertificateData).GetProperties()
            .Select(p => new FieldChange(p.Name, before is null ? null : p.GetValue(before), p.GetValue(after)))
            .Where(c => !Equals(c.Before, c.After))
            .ToList();
}
