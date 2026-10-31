using Approval.Core.Abstractions;
using Approval.Core.Domain;

namespace Certificate.Api.Certificates;

/// <summary>审批结束回调：在引擎提交事务前执行，与审批状态一起提交或一起回滚。</summary>
public sealed class CertificateApprovalHandler(AppDbContext db, TimeProvider clock) : IApprovalBusinessHandler
{
    public string BusinessType => CertificateService.BusinessType;

    public async Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct)
    {
        var request = await LoadRequestAsync(instance, ct);

        var cert = request.Operation == CertOperation.Create
            ? db.Certificates.Add(new CertificateEntity
            {
                Id = Guid.NewGuid(),
                CertNo = request.Snapshot.CertNo,
                Name = request.Snapshot.Name,
                Holder = request.Snapshot.Holder,
                CertType = request.Snapshot.CertType
            }).Entity
            : await db.Certificates.FindAsync([request.CertificateId], ct)
              ?? throw new InvalidOperationException($"申请单 {request.Id} 关联的证书不存在");

        switch (request.Operation)
        {
            case CertOperation.Create:
            case CertOperation.Update:
                request.Snapshot.ApplyTo(cert);   // 审批通过后才真正写入正式证书
                cert.Status = CertificateStatus.Active;
                break;
            case CertOperation.Revoke:
                cert.Status = CertificateStatus.Revoked;
                break;
        }

        cert.PendingRequestId = null;
        request.CertificateId = cert.Id;
        Complete(request, RequestStatus.Approved);
    }

    public Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct) => CloseAsync(instance, RequestStatus.Rejected, ct);

    public Task OnWithdrawnAsync(ApprovalInstance instance, CancellationToken ct) => CloseAsync(instance, RequestStatus.Withdrawn, ct);

    /// <summary>驳回或撤回：申请单关闭、证书解锁，正式证书数据保持不变。</summary>
    private async Task CloseAsync(ApprovalInstance instance, RequestStatus status, CancellationToken ct)
    {
        var request = await LoadRequestAsync(instance, ct);
        if (request.CertificateId is { } certId && await db.Certificates.FindAsync([certId], ct) is { } cert)
            cert.PendingRequestId = null;
        Complete(request, status);
    }

    private async Task<CertificateRequest> LoadRequestAsync(ApprovalInstance instance, CancellationToken ct) =>
        await db.CertificateRequests.FindAsync([Guid.Parse(instance.BusinessKey)], ct)
        ?? throw new InvalidOperationException($"证书申请单 {instance.BusinessKey} 不存在");

    private void Complete(CertificateRequest request, RequestStatus status)
    {
        request.Status = status;
        request.CompletedAt = clock.GetUtcNow().UtcDateTime;
    }
}
