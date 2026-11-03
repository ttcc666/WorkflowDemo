using Approval.Forms.Schema;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

/// <summary>宿主自定义规则：审计员可查看全部审批。</summary>
public sealed class AuditorAccessPolicy(ApprovalDbContextAccessor accessor, IApprovalIdentityProvider identity)
    : DefaultApprovalAccessPolicy(accessor, identity)
{
    public override async Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstanceContext instance, CancellationToken ct) =>
        await Identity.IsInRoleAsync(viewer.UserId, "Auditor", ct) ? ApprovalAccess.Full : await base.GetAccessAsync(viewer, instance, ct);
}

/// <summary>数据权限：审批详情、按业务查询、流程定义公开视图只对有关的人可见。</summary>
public sealed class AccessControlTests : IAsyncLifetime
{
    /// <summary>提交 → 抄送（用户 c1 + 角色 Watcher）→ 审核（角色 Reviewer，或签）→ 终审（用户 boss）。</summary>
    private const string Flow = """
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "cc" },
        "cc":     { "name": "抄送", "type": "Cc", "approvers": [{ "type": "User", "value": "c1" }, { "type": "Role", "value": "Watcher" }], "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "final" },
        "final":  { "name": "终审", "type": "Approval", "mode": "Any", "approvers": [{ "type": "User", "value": "boss" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private readonly TestHost _host = new(build: b => b.AddAccessPolicy<AuditorAccessPolicy>());

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("r2", "Reviewer");
        _host.Identity.Add("w1", "Watcher");
        _host.Identity.Add("audit", "Auditor");
        return _host.PublishAsync("f", Flow);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<InstanceDetail?> ViewAsync(Guid id, string user, bool admin = false) =>
        _host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetInstanceForViewerAsync(id, new ApprovalViewer(user, admin)));

    private async Task<ApprovalAccessLevel> LevelAsync(Guid id, string user, bool admin = false) =>
        (await ViewAsync(id, user, admin))?.Access ?? ApprovalAccessLevel.None;

    private Task<List<ApprovalCarbonCopy>> CarbonCopiesAsync(Guid id) =>
        _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalCarbonCopy>()
            .Where(c => c.InstanceId == id).OrderBy(c => c.Round).ThenBy(c => c.AssigneeValue).ToListAsync());

    private Task RunEngineAsync(Func<ApprovalEngine, Task> action) => _host.RunAsync(sp => action(sp.GetRequiredService<ApprovalEngine>()));

    [Fact]
    public async Task Initiator_admin_and_participants_can_view_unrelated_users_cannot()
    {
        var instance = await _host.StartAsync("f", "B1", "alice");

        Assert.Equal(ApprovalAccessLevel.Full, await LevelAsync(instance.Id, "alice"));
        Assert.Equal(ApprovalAccessLevel.Full, await LevelAsync(instance.Id, "someone", admin: true));
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "r1"));   // 角色候选人
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "c1"));   // 抄送用户
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "w1"));   // 抄送角色成员
        Assert.Null(await ViewAsync(instance.Id, "boss"));                                    // 尚未到达终审节点
        Assert.Null(await ViewAsync(instance.Id, "outsider"));
        Assert.Null(await ViewAsync(Guid.NewGuid(), "alice"));
    }

    [Fact]
    public async Task Handlers_keep_access_after_transfer_and_add_sign_grants_access()
    {
        var instance = await _host.StartAsync("f", "B1", "alice");
        await _host.ApproveMyTaskAsync("r1");
        // 或签：r1 处理后，同角色的 r2 仍是该任务的候选人
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "r2"));

        var final = Assert.Single(await _host.TodoAsync("boss"));
        await RunEngineAsync(e => e.TransferAsync(final.TaskId, "boss", "bob", "出差"));
        var transferred = Assert.Single(await _host.TodoAsync("bob"));
        await RunEngineAsync(e => e.AddSignAsync(transferred.TaskId, "bob", ["sam"], AddSignMode.Before));

        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "boss"));   // 转交后原处理人
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "bob"));
        Assert.Equal(ApprovalAccessLevel.Participant, await LevelAsync(instance.Id, "sam"));    // 加签人
        var detail = (await ViewAsync(instance.Id, "sam"))!;
        Assert.Equal(instance.Id, detail.Id);
        Assert.NotEmpty(detail.Tasks);
        Assert.NotEmpty(detail.Logs);
    }

    [Fact]
    public async Task Carbon_copies_are_recorded_per_recipient_and_round()
    {
        var instance = await _host.StartAsync("f", "B1", "alice");
        var review = Assert.Single(await _host.TodoAsync("r1"));
        await RunEngineAsync(e => e.ReturnAsync(review.TaskId, "r1", "请补充", "start"));
        var resubmit = Assert.Single(await _host.TodoAsync("alice"));
        await RunEngineAsync(e => e.ResubmitAsync(resubmit.TaskId, "alice", "已补充"));

        var copies = await CarbonCopiesAsync(instance.Id);
        Assert.Equal([(0, AssigneeKind.Role, "Watcher"), (0, AssigneeKind.User, "c1"), (1, AssigneeKind.Role, "Watcher"), (1, AssigneeKind.User, "c1")],
            copies.Select(c => (c.Round, c.AssigneeKind, c.AssigneeValue)).OrderBy(c => c.Round).ThenBy(c => c.AssigneeValue, StringComparer.Ordinal));
        Assert.All(copies, c => Assert.Equal("cc", c.NodeId));
    }

    [Fact]
    public async Task Custom_policy_can_grant_access()
    {
        var instance = await _host.StartAsync("f", "B1", "alice");

        Assert.Equal(ApprovalAccessLevel.Full, await LevelAsync(instance.Id, "audit"));
    }

    [Fact]
    public async Task Business_query_returns_only_visible_instances()
    {
        var first = await _host.StartAsync("f", "B1", "alice");
        await RunEngineAsync(e => e.WithdrawAsync(first.Id, "alice"));
        await _host.StartAsync("f", "B1", "alice");

        Task<List<InstanceDetail>> Query(string user) => _host.RunAsync(sp =>
            sp.GetRequiredService<ApprovalQueryService>().GetByBusinessForViewerAsync(RecordingBusinessHandler.Type, "B1", new ApprovalViewer(user, false)));

        Assert.Equal(2, (await Query("alice")).Count);
        Assert.Equal(2, (await Query("r1")).Count);
        Assert.Empty(await Query("outsider"));
    }

    [Fact]
    public async Task Public_definition_requires_access_and_hides_sensitive_configuration()
    {
        var instance = await _host.StartAsync("f", "B1", "alice");

        Task<ApprovalDefinitionSnapshot?> Definition(string user) => _host.RunAsync(sp =>
            sp.GetRequiredService<ApprovalQueryService>().GetPublicDefinitionAsync(instance.Id, new ApprovalViewer(user, false)));

        Assert.Null(await Definition("outsider"));
        var view = FlowDefinition.Parse((await Definition("r1"))!.DefinitionJson);
        Assert.Equal(["cc", "end", "final", "review", "start"], view.Nodes.Keys.Order());
    }

    [Fact]
    public void Public_view_removes_notifications_timeout_and_initiator_roles()
    {
        var flow = FlowDefinition.Parse("""
        {
          "startNodeId": "start",
          "settings": { "startable": true, "initiatorRoles": ["HR"], "titleTemplate": "{申请人}" },
          "form": { "fields": [{ "key": "amount", "label": "金额", "type": "Money" }] },
          "notifications": [{ "events": ["Completed"], "recipients": [{ "type": "Initiator", "value": "" }], "channels": ["InApp"] }],
          "nodes": {
            "start": { "name": "提交", "type": "Start", "next": "review" },
            "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end",
                        "timeout": { "minutes": 60 }, "position": { "x": 10, "y": 20 },
                        "notifications": [{ "events": ["Enter"], "recipients": [{ "type": "Assignees", "value": "" }], "channels": ["Webhook"], "content":"secret-token" }] },
            "end":   { "name": "结束", "type": "End" }
          }
        }
        """);

        var json = flow.ToPublicView().ToJson();
        var view = FlowDefinition.Parse(json);

        Assert.DoesNotContain("secret-token", json);
        Assert.Empty(view.Notifications);
        Assert.Empty(view.Nodes["review"].Notifications);
        Assert.Null(view.Nodes["review"].Timeout);
        Assert.Empty(view.Settings!.InitiatorRoles);
        Assert.Equal(new NodePosition(10, 20), view.Nodes["review"].Position);
        Assert.Equal("amount", Assert.Single(view.GetForm()!.Fields).Key);
        Assert.Single(flow.Nodes["review"].Notifications);
        Assert.Contains("secret-token", JsonSerializer.Serialize(flow.Nodes["review"].Notifications));
        // 原定义不受影响
    }
}
