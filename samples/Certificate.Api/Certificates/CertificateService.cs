using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Engine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

/// <summary>每次调用是一份申请的完整提交；数据库约束负责并发防重，结果重放不重新推进流程。</summary>
public sealed class CertificateService(AppDbContext db, ApprovalEngine engine, TimeProvider clock,
    CertificateDataValidator validator, CertificateAccessPolicy access, CertificateWorkflowPolicy workflows, ILogger<CertificateService> logger)
{
    public const string BusinessType = "Certificate";

    public async Task<CertificateRequest> SubmitCreateAsync(CertificateData data, ApprovalViewer viewer, string key,
        Guid? sourceRequestId, CancellationToken ct)
    {
        data = validator.Validate(data, checkExpiry: false);
        var hash = Hash(CertOperation.Create, null, data, null, sourceRequestId);
        if (await ReplayAsync(viewer.UserId, CertOperation.Create, key, hash, ct) is { } replay) return replay;
        data = validator.Validate(data);
        await ValidateSourceAsync(sourceRequestId, CertOperation.Create, null, viewer.UserId, ct);
        var number = CertificateDataValidator.NormalizeNumber(data.CertNo);
        if (await db.CertificateNumberReservations.AnyAsync(r => r.NormalizedCertNo == number, ct)) throw NumberConflict();

        return await SubmitAsync(null, CertOperation.Create, data, null, viewer, key, hash, sourceRequestId, ct);
    }

    public async Task<CertificateRequest> SubmitUpdateAsync(Guid certificateId, CertificateData data, ApprovalViewer viewer,
        string key, Guid? sourceRequestId, CancellationToken ct)
    {
        data = validator.Validate(data, checkExpiry: false);
        var hash = Hash(CertOperation.Update, certificateId, data, null, sourceRequestId);
        if (await ReplayAsync(viewer.UserId, CertOperation.Update, key, hash, ct) is { } replay) return replay;
        data = validator.Validate(data);
        var cert = await LoadEditableAsync(certificateId, viewer, ct);
        if (CertificateDataValidator.NormalizeNumber(data.CertNo) != cert.NormalizedCertNo)
            throw new ApprovalValidationException(new Dictionary<string, string[]> { ["certNo"] = ["已登记的证号不允许修改"] });
        await ValidateSourceAsync(sourceRequestId, CertOperation.Update, certificateId, viewer.UserId, ct);
        return await SubmitAsync(cert, CertOperation.Update, data, null, viewer, key, hash, sourceRequestId, ct);
    }

    public async Task<CertificateRequest> SubmitRevokeAsync(Guid certificateId, string? reason, ApprovalViewer viewer,
        string key, Guid? sourceRequestId, CancellationToken ct)
    {
        reason = CertificateDataValidator.ValidateReason(reason);
        var hash = Hash(CertOperation.Revoke, certificateId, null, reason, sourceRequestId);
        if (await ReplayAsync(viewer.UserId, CertOperation.Revoke, key, hash, ct) is { } replay) return replay;
        var cert = await LoadEditableAsync(certificateId, viewer, ct);
        await ValidateSourceAsync(sourceRequestId, CertOperation.Revoke, certificateId, viewer.UserId, ct);
        return await SubmitAsync(cert, CertOperation.Revoke, CertificateData.From(cert), reason, viewer, key, hash, sourceRequestId, ct);
    }

    private async Task<CertificateEntity> LoadEditableAsync(Guid id, ApprovalViewer viewer, CancellationToken ct)
    {
        if (!await (await access.VisibleCertificatesAsync(viewer, ct)).AnyAsync(c => c.Id == id, ct)) throw CertificateQueryService.NotFound();
        var cert = await db.Certificates.FindAsync([id], ct) ?? throw CertificateQueryService.NotFound();
        CertificateAccessPolicy.EnsureCanManage(cert, viewer);
        if (cert.Status != CertificateStatus.Active) throw new ApprovalException("已注销的注册证不能变更或再次注销");
        if (cert.PendingRequestId is not null) throw new ApprovalException("该注册证已有审核中的申请") { Code = ApprovalErrorCode.Conflict };
        return cert;
    }

    private async Task ValidateSourceAsync(Guid? sourceId, CertOperation operation, Guid? certificateId, string userId, CancellationToken ct)
    {
        if (sourceId is not { } id) return;
        var source = await db.CertificateRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.ApplicantId == userId, ct)
            ?? throw CertificateQueryService.NotFound();
        if (source.Operation != operation || source.CertificateId != certificateId
            || source.Status is not (RequestStatus.Rejected or RequestStatus.Withdrawn or RequestStatus.Terminated))
            throw new ApprovalException("该申请不能作为重新申请的来源") { Code = ApprovalErrorCode.Conflict };
    }

    private async Task<CertificateRequest?> ReplayAsync(string userId, CertOperation operation, string key, string hash, CancellationToken ct)
    {
        var previous = await db.CertificateRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ApplicantId == userId && r.Operation == operation && r.IdempotencyKey == key, ct);
        if (previous is not null && previous.RequestHash != hash)
            throw new ApprovalException("同一提交标识不能用于不同的申请内容") { Code = ApprovalErrorCode.Conflict };
        return previous;
    }

    private async Task<CertificateRequest> SubmitAsync(CertificateEntity? cert, CertOperation operation, CertificateData data,
        string? reason, ApprovalViewer viewer, string key, string hash, Guid? sourceId, CancellationToken ct)
    {
        var definitionId = await workflows.EnsureReadyAsync(operation, data, viewer.UserId, ct);
        var number = CertificateDataValidator.NormalizeNumber(data.CertNo);
        var request = new CertificateRequest
        {
            Id = Guid.NewGuid(),
            ApprovalInstanceId = Guid.NewGuid(),
            CertificateId = cert?.Id,
            Operation = operation,
            Snapshot = data,
            BeforeSnapshot = cert is null ? null : CertificateData.From(cert),
            NormalizedCertNo = number,
            SourceRequestId = sourceId,
            IdempotencyKey = key,
            RequestHash = hash,
            Reason = reason,
            Status = RequestStatus.Reviewing,
            ApplicantId = viewer.UserId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.CertificateRequests.Add(request);
        if (cert is null)
        {
            db.CertificateNumberReservations.Add(new CertificateNumberReservation
            {
                NormalizedCertNo = number,
                ActiveRequestId = request.Id,
                CreatedAt = request.CreatedAt
            });
        }
        else
        {
            cert.PendingRequestId = request.Id;
            cert.ConcurrencyStamp++;
        }

        try
        {
            await engine.StartAsync(new StartApprovalRequest(CertificateWorkflowPolicy.Key(operation), BusinessType, request.Id.ToString(), viewer.UserId)
            {
                InstanceId = request.ApprovalInstanceId,
                DefinitionId = definitionId,
                Title = $"{OperationName(operation)}注册证 {data.CertNo} {data.Name}",
                Comment = reason,
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
            logger.LogInformation("注册证申请已提交：{Operation} RequestId={RequestId} InstanceId={InstanceId}", operation, request.Id, request.ApprovalInstanceId);
            return request;
        }
        catch (Exception ex) when (IsUniqueConflict(ex) || ex is ApprovalException { Code: ApprovalErrorCode.Conflict })
        {
            // 本业务入口按独立请求作用域使用。失败的图不再参与后续保存，只读取已提交的赢家。
            db.ChangeTracker.Clear();
            if (await ReplayAsync(viewer.UserId, operation, key, hash, ct) is { } previous) return previous;
            if (operation == CertOperation.Create && await db.CertificateNumberReservations.AnyAsync(r => r.NormalizedCertNo == number, ct))
                throw NumberConflict();
            throw;
        }
    }

    private static bool IsUniqueConflict(Exception ex) =>
        ex is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 1555 or 2067 } };

    private static string Hash(CertOperation operation, Guid? certificateId, CertificateData? data, string? reason, Guid? sourceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { operation, certificateId, data, reason, sourceId }, JsonSerializerOptions.Web))));

    private static ApprovalException NumberConflict() => new("该注册证编号已登记或正在审核中") { Code = ApprovalErrorCode.Conflict };
    public static string OperationName(CertOperation operation) => operation switch
    {
        CertOperation.Create => "新增",
        CertOperation.Update => "变更",
        CertOperation.Revoke => "注销",
        _ => operation.ToString()
    };
}
