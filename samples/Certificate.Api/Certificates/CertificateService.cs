using Approval.Core;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

/// <summary>证书业务：所有变更都先生成申请单并发起审批，审批通过后由 <see cref="CertificateApprovalHandler"/> 生效。</summary>
public sealed class CertificateService(AppDbContext db, ApprovalEngine engine, TimeProvider clock)
{
    public const string BusinessType = "Certificate";

    public async Task<CertificateRequest> SubmitCreateAsync(CertificateData data, string userId, CancellationToken ct)
    {
        var duplicated = await db.Certificates.AnyAsync(c => c.CertNo == data.CertNo, ct)
                         || await db.CertificateRequests.AnyAsync(r =>
                             r.Operation == CertOperation.Create && r.Status == RequestStatus.Reviewing && r.Snapshot.CertNo == data.CertNo, ct);
        if (duplicated) throw new ApprovalException($"证书编号 {data.CertNo} 已存在或正在审核中");

        return await SubmitAsync(null, CertOperation.Create, data, null, userId, ct);
    }

    public async Task<CertificateRequest> SubmitUpdateAsync(Guid certificateId, CertificateData data, string userId, CancellationToken ct)
    {
        var cert = await LoadEditableAsync(certificateId, ct);
        if (data.CertNo != cert.CertNo) throw new ApprovalException("证书编号不允许修改");
        return await SubmitAsync(cert, CertOperation.Update, data, null, userId, ct);
    }

    public async Task<CertificateRequest> SubmitRevokeAsync(Guid certificateId, string reason, string userId, CancellationToken ct)
    {
        var cert = await LoadEditableAsync(certificateId, ct);
        return await SubmitAsync(cert, CertOperation.Revoke, CertificateData.From(cert), reason, userId, ct);
    }

    private async Task<CertificateEntity> LoadEditableAsync(Guid certificateId, CancellationToken ct)
    {
        var cert = await db.Certificates.FindAsync([certificateId], ct)
                   ?? throw new ApprovalException("证书不存在") { Code = ApprovalErrorCode.NotFound };
        if (cert.Status != CertificateStatus.Active) throw new ApprovalException("仅有效证书可以变更或注销");
        if (cert.PendingRequestId is not null)
            throw new ApprovalException("该证书已有审核中的申请，请等待审核完成") { Code = ApprovalErrorCode.Conflict };
        return cert;
    }

    private async Task<CertificateRequest> SubmitAsync(
        CertificateEntity? cert, CertOperation operation, CertificateData data, string? reason, string userId, CancellationToken ct)
    {
        var request = new CertificateRequest
        {
            Id = Guid.NewGuid(),
            CertificateId = cert?.Id,
            Operation = operation,
            Snapshot = data,
            Reason = reason,
            Status = RequestStatus.Reviewing,
            ApplicantId = userId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.CertificateRequests.Add(request);

        if (cert is not null)
        {
            cert.PendingRequestId = request.Id;   // 编辑锁
            cert.ConcurrencyStamp++;              // 两人同时提交时，后提交者冲突失败
        }

        // 引擎在内部 SaveChanges，申请单、证书锁与审批实例在同一事务中提交
        await engine.StartAsync(new StartApprovalRequest($"certificate.{operation.ToString().ToLowerInvariant()}",
            BusinessType, request.Id.ToString(), userId)
        {
            Title = $"{OperationName(operation)}证书 {data.CertNo} {data.Name}",
            Comment = reason,
            // 条件分支、消息模板（如 {form.certNo}）、脚本可用的字段
            FormData = new
            {
                certType = data.CertType,
                operation = operation.ToString(),
                certNo = data.CertNo,
                name = data.Name,
                holder = data.Holder,
                issueDate = data.IssueDate,
                expireDate = data.ExpireDate
            }
        }, ct);

        return request;
    }

    public static string OperationName(CertOperation operation) => operation switch
    {
        CertOperation.Create => "新增",
        CertOperation.Update => "变更",
        CertOperation.Revoke => "注销",
        _ => operation.ToString()
    };
}
