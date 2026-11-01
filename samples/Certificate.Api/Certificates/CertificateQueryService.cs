using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public sealed record CertificateView(Guid Id, string CertNo, string Name, string Holder, string CertType, DateOnly IssueDate,
    DateOnly ExpireDate, CertificateStatus Status, string EffectiveStatus, Guid? PendingRequestId, bool CanManage);
public sealed record CertificateRequestView(Guid Id, Guid? CertificateId, CertOperation Operation, CertificateData Snapshot,
    CertificateData? BeforeSnapshot, Guid? SourceRequestId, string? Reason, RequestStatus Status, string ApplicantId,
    DateTime CreatedAt, DateTime? CompletedAt, bool HasHistoricalBaseline, bool CanReapply);
public sealed record FieldChange(string Field, object? Before, object? After);
public sealed record CertificateRequestDetail(CertificateRequestView Request, IReadOnlyList<FieldChange> Changes, InstanceDetail? Approval);

public sealed class CertificateQueryService(CertificateAccessPolicy access, CertificateDataValidator validator, ApprovalQueryService approvals)
{
    public async Task<PagedResult<CertificateView>> ListAsync(ApprovalViewer viewer, PageRequest page, string? keyword, CancellationToken ct)
    {
        var query = await access.VisibleCertificatesAsync(viewer, ct);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var text = keyword.Trim();
            query = query.Where(c => c.CertNo.Contains(text) || c.Name.Contains(text) || c.Holder.Contains(text));
        }
        var result = await query.OrderBy(c => c.CertNo).ThenBy(c => c.Id).ToPagedAsync(page, ct);
        return new(result.Items.Select(c => View(c, viewer)).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<CertificateView> GetAsync(Guid id, ApprovalViewer viewer, CancellationToken ct) =>
        View(await (await access.VisibleCertificatesAsync(viewer, ct)).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw NotFound(), viewer);

    public async Task<PagedResult<CertificateRequestView>> HistoryAsync(Guid certificateId, ApprovalViewer viewer, PageRequest page,
        CancellationToken ct)
    {
        await GetAsync(certificateId, viewer, ct);
        var result = await (await access.VisibleRequestsAsync(viewer, ct)).Where(r => r.CertificateId == certificateId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToPagedAsync(page, ct);
        return new(result.Items.Select(r => View(r, viewer)).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<CertificateRequestDetail> RequestAsync(Guid id, ApprovalViewer viewer, CancellationToken ct)
    {
        var request = await (await access.VisibleRequestsAsync(viewer, ct)).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw NotFound();
        var view = View(request, viewer);
        var changes = view.HasHistoricalBaseline
            ? typeof(CertificateData).GetProperties()
                .Select(p => new FieldChange(p.Name, request.BeforeSnapshot is null ? null : p.GetValue(request.BeforeSnapshot), p.GetValue(request.Snapshot)))
                .Where(p => !Equals(p.Before, p.After)).ToList() : [];
        var approval = request.ApprovalInstanceId is { } instanceId
            ? await approvals.GetInstanceForViewerAsync(instanceId, viewer, ct) : null;
        return new(view, changes, approval);
    }

    private CertificateView View(CertificateEntity c, ApprovalViewer viewer) => new(c.Id, c.CertNo, c.Name, c.Holder, c.CertType,
        c.IssueDate, c.ExpireDate, c.Status,
        c.Status == CertificateStatus.Revoked ? "Revoked" : c.ExpireDate < validator.Today ? "Expired" : c.IssueDate > validator.Today ? "NotYetValid" : "Valid",
        c.PendingRequestId, CertificateAccessPolicy.CanManage(c, viewer));

    private static CertificateRequestView View(CertificateRequest r, ApprovalViewer viewer) => new(r.Id, r.CertificateId, r.Operation,
        r.Snapshot, r.BeforeSnapshot, r.SourceRequestId, r.Reason, r.Status, r.ApplicantId, r.CreatedAt, r.CompletedAt,
        r.Operation == CertOperation.Create || r.BeforeSnapshot is not null,
        r.ApplicantId == viewer.UserId && r.Status is RequestStatus.Rejected or RequestStatus.Withdrawn or RequestStatus.Terminated);

    public static ApprovalException NotFound() => new("注册证或申请不存在，或无权查看") { Code = ApprovalErrorCode.NotFound };
}
