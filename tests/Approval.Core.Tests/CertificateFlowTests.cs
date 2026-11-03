using Approval.Core.Abstractions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>证书多角色审核主场景。</summary>
public sealed class CertificateFlowTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public async Task InitializeAsync()
    {
        _host.Identity.Add("alice");                         // 申请人
        _host.Identity.Add("dave", "DeptReviewer");
        _host.Identity.Add("dan", "DeptReviewer");
        _host.Identity.Add("quinn", "QualityManager");
        _host.Identity.Add("sam", "SafetyOfficer");
        _host.Identity.Add("leo", "Leader");
        await _host.PublishAsync("certificate.update", Flows.Certificate);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Normal_certificate_passes_roles_in_order_and_skips_safety()
    {
        var instance = await _host.StartAsync("certificate.update", "CERT-001", "alice", new { certType = "Normal" });

        // 角色任务：部门审核员角色内的任何人都能看到待办
        Assert.Single(await _host.TodoAsync("dave"));
        Assert.Single(await _host.TodoAsync("dan"));
        Assert.Empty(await _host.TodoAsync("quinn"));

        await _host.ApproveMyTaskAsync("dan", "部门审核通过");
        Assert.Empty(await _host.TodoAsync("dave"));         // 或签：一人处理后其他人的待办消失

        await _host.ApproveMyTaskAsync("quinn");
        Assert.Empty(await _host.TodoAsync("sam"));          // 普通证书跳过安全审核
        await _host.ApproveMyTaskAsync("leo");

        var done = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, done.Status);
        Assert.Equal([("Approved", "CERT-001")], _host.Handler.Calls);

        var dept = Assert.Single(done.Tasks, t => t.NodeId == "dept");
        Assert.Equal(AssigneeKind.Role, dept.AssigneeKind);
        Assert.Equal("dan", dept.HandledBy);                 // 记录角色内的实际处理人

        Assert.Equal(
            [ApprovalAction.Submit, ApprovalAction.Approve, ApprovalAction.Approve, ApprovalAction.Approve, ApprovalAction.Cc, ApprovalAction.Complete],
            done.Logs.Where(l => l.Action != ApprovalAction.NodeEntered).Select(l => l.Action));
    }

    [Fact]
    public async Task Special_certificate_requires_safety_review()
    {
        await _host.StartAsync("certificate.update", "CERT-002", "alice", new { certType = "Special" });

        await _host.ApproveMyTaskAsync("dave");
        await _host.ApproveMyTaskAsync("quinn");
        Assert.Empty(await _host.TodoAsync("leo"));
        await _host.ApproveMyTaskAsync("sam");
        await _host.ApproveMyTaskAsync("leo");

        Assert.Equal([("Approved", "CERT-002")], _host.Handler.Calls);
    }

    [Fact]
    public async Task User_outside_role_cannot_approve()
    {
        await _host.StartAsync("certificate.update", "CERT-003", "alice", new { certType = "Normal" });
        var todo = Assert.Single(await _host.TodoAsync("dave"));

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(todo.TaskId, "quinn"));
        Assert.Equal(ApprovalErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task Reject_ends_flow_and_allows_resubmission()
    {
        var first = await _host.StartAsync("certificate.update", "CERT-004", "alice", new { certType = "Normal" });
        await _host.ApproveMyTaskAsync("dave");

        var todo = Assert.Single(await _host.TodoAsync("quinn"));
        await _host.RunAsync(sp => sp.GetRequiredService<Engine.ApprovalEngine>().RejectAsync(todo.TaskId, "quinn", "有效期填写错误"));

        var rejected = await _host.GetInstanceAsync(first.Id);
        Assert.Equal(InstanceStatus.Rejected, rejected.Status);
        Assert.Equal([("Rejected", "CERT-004")], _host.Handler.Calls);
        Assert.DoesNotContain(rejected.Tasks, t => t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting);

        // 修改后重新提交，同一业务单据产生新的流程实例
        var second = await _host.StartAsync("certificate.update", "CERT-004", "alice", new { certType = "Normal" });
        Assert.NotEqual(first.Id, second.Id);
        var history = await _host.RunAsync(sp =>
            sp.GetRequiredService<Engine.ApprovalQueryService>().GetByBusinessAsync(RecordingBusinessHandler.Type, "CERT-004"));
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task Same_business_cannot_have_two_running_flows()
    {
        await _host.StartAsync("certificate.update", "CERT-005", "alice", new { certType = "Normal" });

        var ex = await Assert.ThrowsAsync<ApprovalException>(() =>
            _host.StartAsync("certificate.update", "CERT-005", "alice", new { certType = "Normal" }));
        Assert.Equal(ApprovalErrorCode.Conflict, ex.Code);
    }

    [Fact]
    public async Task Initiator_cannot_approve_own_request_even_with_role()
    {
        _host.Identity.Add("alice", "DeptReviewer");
        await _host.StartAsync("certificate.update", "CERT-006", "alice", new { certType = "Normal" });

        Assert.Empty(await _host.TodoAsync("alice"));
        var task = Assert.Single(await _host.TodoAsync("dave"));
        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.ApproveAsync(task.TaskId, "alice"));
        Assert.Equal(ApprovalErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task Notifications_are_sent_for_new_tasks_cc_and_completion()
    {
        await _host.StartAsync("certificate.update", "CERT-007", "alice", new { certType = "Normal" });
        var created = Assert.Single(_host.Notifier.Notifications);
        Assert.Equal(NotificationKind.TaskCreated, created.Kind);
        Assert.Equal(new Assignee(AssigneeKind.Role, "DeptReviewer"), Assert.Single(created.Recipients));

        await _host.ApproveMyTaskAsync("dave");
        await _host.ApproveMyTaskAsync("quinn");
        await _host.ApproveMyTaskAsync("leo");

        Assert.Contains(_host.Notifier.Notifications, n => n.Kind == NotificationKind.Cc);
        var completed = _host.Notifier.Notifications[^1];
        Assert.Equal(NotificationKind.Completed, completed.Kind);
        Assert.Equal(InstanceStatus.Approved, completed.Status);
    }
}
