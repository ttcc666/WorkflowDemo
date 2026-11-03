using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

/// <summary>审批结束回调：在引擎提交事务前执行，与审批状态一起提交或一起回滚。</summary>
public sealed class CertificateApprovalHandler(AppDbContext db, TimeProvider clock, CertificateDataValidator validator) : IApprovalBusinessHandler
{
    public string BusinessType => CertificateService.BusinessType;

    public async Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct)
    {
        var request = await LoadRequestAsync(instance, ct);
        if (request.Operation != CertOperation.Revoke) validator.Validate(request.Snapshot);

        var cert = request.Operation == CertOperation.Create
            ? db.Certificates.Add(new CertificateEntity
            {
                Id = Guid.NewGuid(),
                CertNo = request.Snapshot.CertNo,
                NormalizedCertNo = request.NormalizedCertNo,
                RegisteredBy = request.ApplicantId,
                Name = request.Snapshot.Name,
                Holder = request.Snapshot.Holder,
                CertType = request.Snapshot.CertType
            }).Entity
            : await db.Certificates.FindAsync([request.CertificateId], ct)
              ?? throw new InvalidOperationException($"申请单 {request.Id} 关联的证书不存在");

        if (request.Operation != CertOperation.Create && cert.PendingRequestId != request.Id)
            throw new ApprovalException("注册证锁不属于当前申请，请联系管理员核查") { Code = ApprovalErrorCode.Conflict };
        if (request.Operation == CertOperation.Create)
        {
            var reservation = await ReservationAsync(request, ct);
            reservation.CertificateId = cert.Id;
            reservation.ActiveRequestId = null;
        }

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
        cert.ConcurrencyStamp++;
        request.CertificateId = cert.Id;
        Complete(request, RequestStatus.Approved);
    }

    public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => CloseAsync(instance, RequestStatus.Rejected, ct);

    public Task OnWithdrawnAsync(ApprovalInstanceContext instance, CancellationToken ct) => CloseAsync(instance, RequestStatus.Withdrawn, ct);

    public Task OnTerminatedAsync(ApprovalInstanceContext instance, CancellationToken ct) => CloseAsync(instance, RequestStatus.Terminated, ct);

    /// <summary>驳回或撤回：申请单关闭、证书解锁，正式证书数据保持不变。</summary>
    private async Task CloseAsync(ApprovalInstanceContext instance, RequestStatus status, CancellationToken ct)
    {
        var request = await LoadRequestAsync(instance, ct);
        if (request.Operation == CertOperation.Create) db.CertificateNumberReservations.Remove(await ReservationAsync(request, ct));
        if (request.CertificateId is { } certId && await db.Certificates.FindAsync([certId], ct) is { } cert)
        {
            if (cert.PendingRequestId != request.Id)
                throw new ApprovalException("注册证锁不属于当前申请，请联系管理员核查") { Code = ApprovalErrorCode.Conflict };
            cert.PendingRequestId = null;
            cert.ConcurrencyStamp++;
        }
        Complete(request, status);
    }

    private async Task<CertificateRequest> LoadRequestAsync(ApprovalInstanceContext instance, CancellationToken ct)
    {
        var request = await db.CertificateRequests.FindAsync([Guid.Parse(instance.BusinessKey)], ct)
            ?? throw new InvalidOperationException($"证书申请单 {instance.BusinessKey} 不存在");
        if (request.ApprovalInstanceId != instance.Id || request.Status != RequestStatus.Reviewing)
            throw new ApprovalException("申请与审批实例不一致或已结束") { Code = ApprovalErrorCode.Conflict };
        return request;
    }

    private async Task<CertificateNumberReservation> ReservationAsync(CertificateRequest request, CancellationToken ct)
    {
        var reservation = await db.CertificateNumberReservations.FindAsync([request.NormalizedCertNo], ct);
        if (reservation is null || reservation.ActiveRequestId != request.Id || reservation.CertificateId is not null)
            throw new ApprovalException("证号占用与申请不一致") { Code = ApprovalErrorCode.Conflict };
        return reservation;
    }

    private void Complete(CertificateRequest request, RequestStatus status)
    {
        request.Status = status;
        request.ConcurrencyStamp++;
        request.CompletedAt = clock.GetUtcNow().UtcDateTime;
    }
}
