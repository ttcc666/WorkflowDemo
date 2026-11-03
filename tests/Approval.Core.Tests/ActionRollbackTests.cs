using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class CallbackFaults
{
    public string Mode = "ok";
    public bool Starting;
    public bool Completing;
}
public sealed class WriteRecordCallback(TestDbContext db, CallbackFaults faults) : IApprovalLifecycleHandler, IApprovalEventHandler, IApprovalBusinessHandler
{
    public string BusinessType => RecordingBusinessHandler.Type;
    private async Task Write(Guid? instanceId, CancellationToken ct)
    {
        db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "R1" });
        if (faults.Mode == "save-then-fail") { await db.SaveChangesAsync(ct); throw new InvalidOperationException("保存后失败"); }
        if (faults.Mode == "touch-then-fail") { db.Set<ApprovalInstance>().Local.Single(i => i.Id == instanceId).Title = "被回调修改"; throw new InvalidOperationException("修改实例后失败"); }
        if (faults.Mode == "fail") throw new InvalidOperationException("回调失败");
    }
    public async Task<ApprovalStartPreparation> OnStartingAsync(ApprovalInstanceContext instance, CancellationToken ct)
    {
        if (faults.Starting) await Write(instance.Id, ct);
        return new(instance.Title, instance.FormDataJson);
    }
    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct) => !faults.Starting && !faults.Completing && approvalEvent.EventType == "Submit" ? Write(null, ct) : Task.CompletedTask;
    public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) => faults.Completing ? Write(instance.Id, ct) : Task.CompletedTask;
    public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
}
public sealed class ActionRollbackTests : IAsyncLifetime
{
    private readonly CallbackFaults _faults = new();
    private TestHost _host = null!;
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"提交","next":"review"},
        "review":{"type":"Approval","name":"审核","approvers":[{"type":"Role","value":"Reviewer"}],"next":"end"},"end":{"type":"End","name":"结束"}}}
        """;
    public Task InitializeAsync()
    {
        _host = new TestHost(build: b => {
            b.Services.AddSingleton(_faults);
            b.AddLifecycleHandler<WriteRecordCallback>().AddEventHandler<WriteRecordCallback>().AddBusinessHandler<WriteRecordCallback>();
        });
        _host.Identity.Add("alice"); _host.Identity.Add("bob", "Reviewer");
        return Task.CompletedTask;
    }
    public async Task DisposeAsync() => await _host.DisposeAsync();
    private Task<List<string>> Records() => _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().Select(r => r.Name).ToListAsync());
    private async Task<ApprovalOutboxMessage> Submit() => Assert.Single(await _host.OutboxAsync(), m => m.Description == "Submit");
    [Theory]
    [InlineData("fail")]
    [InlineData("save-then-fail")]
    public async Task Event_failure_rolls_back_partial_writes_and_counts_each_attempt(string mode)
    {
        _faults.Mode = mode; await _host.PublishAsync("f", Flow); await _host.StartAsync("f", "B1", "alice");
        await _host.DispatchOutboxAsync();
        Assert.Empty(await Records()); var message = await Submit();
        Assert.Equal(OutboxStatus.Pending, message.Status); Assert.Equal(1, message.Attempts); Assert.Contains("失败", message.LastError);
        _host.Clock.Advance(TimeSpan.FromHours(1)); await _host.DispatchOutboxAsync();
        Assert.Empty(await Records()); Assert.Equal(2, (await Submit()).Attempts);
    }
    [Fact]
    public async Task Event_success_commits_host_write_and_delivery_result_together()
    {
        await _host.PublishAsync("f", Flow); await _host.StartAsync("f", "B1", "alice"); await _host.DispatchOutboxAsync();
        Assert.Equal(["R1"], await Records()); Assert.Equal(OutboxStatus.Succeeded, (await Submit()).Status);
    }
    [Fact]
    public async Task Event_unique_constraint_failure_is_recorded_as_failed_attempt()
    {
        await _host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "R1" }); await db.SaveChangesAsync(); });
        await _host.PublishAsync("f", Flow); await _host.StartAsync("f", "B1", "alice"); await _host.DispatchOutboxAsync();
        Assert.Equal(["R1"], await Records()); var message = await Submit();
        Assert.Equal(OutboxStatus.Pending, message.Status); Assert.Equal(1, message.Attempts); Assert.Contains("DbUpdateException", message.LastError);
    }
    [Fact]
    public async Task Starting_failure_rolls_back_entire_operation_without_continue_on_error()
    {
        _faults.Starting = true; _faults.Mode = "touch-then-fail"; await _host.PublishAsync("f", Flow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.StartAsync("f", "B1", "alice"));
        Assert.Empty(await Records());
        await _host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync()); Assert.Empty(await db.Set<ApprovalBusinessSlot>().ToListAsync()); Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync()); });
    }
    [Fact]
    public async Task Completion_failure_restores_pending_task_title_and_running_instance()
    {
        _faults.Completing = true; _faults.Mode = "touch-then-fail"; await _host.PublishAsync("f", Flow);
        var instance = await _host.StartAsync("f", "B1", "alice");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.ApproveMyTaskAsync("bob"));
        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Running, saved.Status); Assert.Equal("review", saved.CurrentNodeId); Assert.Equal("证书 B1", saved.Title);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status); Assert.Empty(await Records());
        _faults.Mode = "ok"; await _host.ApproveMyTaskAsync("bob");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status); Assert.Equal(["R1"], await Records());
    }
    [Fact]
    public async Task Starting_callback_cannot_save_early_or_leak_writes()
    {
        _faults.Starting = true; _faults.Mode = "save-then-fail"; await _host.PublishAsync("f", Flow);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _host.StartAsync("f", "B1", "alice"));
        Assert.Contains("SaveChanges", error.Message); Assert.Empty(await Records());
        await _host.RunAsync(async sp => Assert.Empty(await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().ToListAsync()));
    }
    [Fact]
    public async Task Starting_success_commits_host_write_with_approval()
    {
        _faults.Starting = true; await _host.PublishAsync("f", Flow); var instance = await _host.StartAsync("f", "B1", "alice");
        Assert.Equal(["R1"], await Records()); Assert.Equal("review", (await _host.GetInstanceAsync(instance.Id)).CurrentNodeId);
    }
}
