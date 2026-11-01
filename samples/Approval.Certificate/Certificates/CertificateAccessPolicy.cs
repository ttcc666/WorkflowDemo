using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public sealed class CertificateAccessPolicy(ApprovalDbContextAccessor accessor, IApprovalIdentityProvider identity)
    : DefaultApprovalAccessPolicy(accessor, identity)
{
    public async Task<IQueryable<CertificateRequest>> VisibleRequestsAsync(ApprovalViewer viewer, CancellationToken ct)
    {
        var requests = Db.Set<CertificateRequest>().AsNoTracking();
        if (viewer.IsAdmin) return requests;
        var instances = await VisibleInstancesAsync(viewer, ct);
        return requests.Where(r => r.ApplicantId == viewer.UserId
            || (r.ApprovalInstanceId != null && instances.Any(i => i.Id == r.ApprovalInstanceId)));
    }

    public async Task<IQueryable<CertificateEntity>> VisibleCertificatesAsync(ApprovalViewer viewer, CancellationToken ct)
    {
        var certificates = Db.Set<CertificateEntity>().AsNoTracking();
        if (viewer.IsAdmin) return certificates;
        var requests = await VisibleRequestsAsync(viewer, ct);
        return certificates.Where(c => c.RegisteredBy == viewer.UserId || requests.Any(r => r.CertificateId == c.Id));
    }

    public static bool CanManage(CertificateEntity certificate, ApprovalViewer viewer) =>
        viewer.IsAdmin || certificate.RegisteredBy == viewer.UserId;

    public static void EnsureCanManage(CertificateEntity certificate, ApprovalViewer viewer)
    {
        if (!CanManage(certificate, viewer)) throw new ApprovalException("仅登记人或管理员可以申请变更或注销") { Code = ApprovalErrorCode.Forbidden };
    }
}
