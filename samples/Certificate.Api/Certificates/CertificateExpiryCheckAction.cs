using Approval.Core.Actions;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

/// <summary>
/// 注册动作示例：读取申请单中的证书有效期，输出剩余天数与是否临期，供后续条件分支使用。
/// 动作以 Scoped 方式创建，可直接注入宿主的 DbContext；同步执行时与审批处于同一事务。
/// </summary>
public sealed class CertificateExpiryCheckAction(AppDbContext db, TimeProvider clock) : IApprovalActionHandler
{
    public ApprovalActionDescriptor Descriptor { get; } = new("CertificateExpiryCheck", "证书有效期检查",
    [
        new ActionParameter("warnDays", "临期阈值（天）", ActionParameterKind.Number)
        {
            DefaultValue = "30",
            Help = "剩余有效天数小于该值视为临期"
        }
    ])
    {
        Description = "输出 { daysToExpire, nearExpiry }，如输出变量为 expiry，条件可写 expiry.nearExpiry == true",
        SupportsAsync = false
    };

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
    {
        var requestId = Guid.Parse(context.Instance.BusinessKey);
        // 发起审批时申请单与实例在同一事务中尚未提交，先查本地跟踪的实体
        var request = db.CertificateRequests.Local.FirstOrDefault(r => r.Id == requestId)
                      ?? await db.CertificateRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new InvalidOperationException($"申请单 {requestId} 不存在");

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var days = request.Snapshot.ExpireDate.DayNumber - today.DayNumber;
        var warnDays = int.TryParse(context.Get("warnDays"), out var w) ? w : 30;

        return ApprovalActionResult.FromOutput(new { daysToExpire = days, nearExpiry = days < warnDays }, $"剩余 {days} 天");
    }
}
