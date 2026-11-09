using Approval.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Approval.MultiTenantHost;

public sealed class Expense
{
    public Guid Id { get; set; }
    public string BusinessKey { get; set; } = "";
    public string ApplicantId { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid? InstanceId { get; set; }
    public string Status { get; set; } = "Submitted";
}
public sealed class DeliveryReceipt
{
    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public string EventType { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}
public sealed class ExpenseLifecycle(TenantDbContext db) : IApprovalBusinessHandler
{
    public string BusinessType => "Expense";
    public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Update(instance.BusinessKey, "Approved", ct);
    public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Update(instance.BusinessKey, "Rejected", ct);
    public Task OnWithdrawnAsync(ApprovalInstanceContext instance, CancellationToken ct) => Update(instance.BusinessKey, "Withdrawn", ct);
    private async Task Update(string businessKey, string status, CancellationToken ct)
    {
        // A flow may finish during StartAsync, before its new business row is flushed.
        var expense = db.Expenses.Local.SingleOrDefault(e => e.BusinessKey == businessKey)
            ?? await db.Expenses.SingleAsync(e => e.BusinessKey == businessKey, ct);
        expense.Status = status; // 与审批状态一起提交；回调不自行 SaveChanges。
    }
}
public sealed class TenantEvents(TenantDbContext db, TimeProvider clock, ILogger<TenantEvents> logger) : IApprovalEventHandler
{
    public async Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        if (await db.Receipts.AnyAsync(r => r.Id == approvalEvent.Id, ct)) return;
        db.Receipts.Add(new() { Id = approvalEvent.Id, InstanceId = approvalEvent.InstanceId, EventType = approvalEvent.EventType,
            ReceivedAt = clock.GetUtcNow().UtcDateTime });
        logger.LogInformation("宿主租户 {TenantId} 接收审批事件 {EventId}: {EventType}", db.PartitionKey, approvalEvent.Id, approvalEvent.EventType);
        // Receipt 随 Outbox 确认一起提交。真实邮件/消息渠道仍须按 Event.Id 自行去重。
    }
}
