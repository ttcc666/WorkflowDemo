using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Resolvers;

namespace Approval.Core.Tests;

/// <summary>
/// 组织架构审批人：部门负责人、第 N 级上级、逐级审批。
/// 组织：总经办（leo）→ 研发部（dave）→ 研发一组（dan）；alice、bob 在研发一组，上级 dan → dave → leo。
/// </summary>
public sealed class OrganizationApproverTests : IAsyncLifetime
{
    private readonly TestHost _host = new(build: b => b.AddOrganizationProvider<FakeOrganization>());

    public Task InitializeAsync()
    {
        var org = _host.Organization;
        org.AddDepartment("gm", null, "leo");
        org.AddDepartment("rd", "gm", "dave");
        org.AddDepartment("rd1", "rd", "dan");
        org.AddUser("alice", "rd1", "dan");
        org.AddUser("bob", "rd1", "dan");
        org.AddUser("dan", "rd1", "dave");
        org.AddUser("dave", "rd", "leo");
        org.AddUser("leo", "gm");
        org.AddUser("zoe", null);                 // 不属于任何部门、没有上级
        foreach (var user in new[] { "alice", "bob", "dan", "dave", "leo", "zoe" }) _host.Identity.Add(user);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private const string FormJson =
        "\"form\": { \"fields\": [{ \"key\": \"payee\", \"label\": \"收款人\", \"type\": \"User\" }, { \"key\": \"reviewers\", \"label\": \"审核人\", \"type\": \"Users\" }] },";

    /// <summary>单节点流程：审批人为一条规则；可选表单（收款人 payee、多人字段 reviewers）。</summary>
    private static string Flow(string type, string value, string mode = "Any", string emptyPolicy = "Error", bool withForm = false) => $$"""
    {
      "startNodeId": "start",
      {{(withForm ? FormJson : "")}}
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "{{mode}}", "emptyApproverPolicy": "{{emptyPolicy}}",
                    "approvers": [{ "type": "{{type}}", "value": "{{value}}" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private async Task<List<(string User, ApprovalTaskStatus Status, int Order)>> TasksAsync(Guid instanceId) =>
        (await _host.GetInstanceAsync(instanceId)).Tasks.OrderBy(t => t.Order).Select(t => (t.AssigneeValue, t.Status, t.Order)).ToList();

    [Theory]
    [InlineData("1", "dan")]
    [InlineData("2", "dave")]
    [InlineData("3", "leo")]
    public async Task Dept_leader_by_level(string level, string expected)
    {
        await _host.PublishAsync("f", Flow("DeptLeader", level));

        var instance = await _host.StartAsync("f", "B1", "alice");

        Assert.Equal(expected, Assert.Single(await TasksAsync(instance.Id)).User);
    }

    [Fact]
    public async Task Dept_leader_beyond_top_follows_empty_approver_policy()
    {
        await _host.PublishAsync("err", Flow("DeptLeader", "4"));
        await _host.PublishAsync("auto", Flow("DeptLeader", "4", emptyPolicy: "AutoApprove"));

        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("err", "B1", "alice"));
        var auto = await _host.StartAsync("auto", "B2", "alice");

        Assert.Contains("未找到审批人", ex.Message);
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(auto.Id)).Status);
        // 不属于任何部门同样为空
        await Assert.ThrowsAsync<ApprovalException>(() => _host.StartAsync("err", "B3", "zoe"));
    }

    [Theory]
    [InlineData("1", "dan")]
    [InlineData("2", "dave")]
    [InlineData("3", "leo")]
    public async Task Manager_by_level(string level, string expected)
    {
        await _host.PublishAsync("f", Flow("Manager", level));

        var instance = await _host.StartAsync("f", "B1", "alice");

        Assert.Equal(expected, Assert.Single(await TasksAsync(instance.Id)).User);
        await _host.PublishAsync("missing", Flow("Manager", "4", emptyPolicy: "AutoApprove"));
        Assert.Equal(InstanceStatus.Approved, (await _host.StartAsync("missing", "B2", "alice")).Status);
    }

    [Fact]
    public async Task Manager_chain_creates_sequential_tasks_in_order_and_stops_at_top()
    {
        await _host.PublishAsync("chain", Flow("ManagerChain", "5", mode: "Sequential"));

        var instance = await _host.StartAsync("chain", "B1", "alice");

        Assert.Equal(
            [("dan", ApprovalTaskStatus.Pending, 0), ("dave", ApprovalTaskStatus.Waiting, 1), ("leo", ApprovalTaskStatus.Waiting, 2)],
            await TasksAsync(instance.Id));
        await _host.ApproveMyTaskAsync("dan");
        await _host.ApproveMyTaskAsync("dave");
        await _host.ApproveMyTaskAsync("leo");
        Assert.Equal(InstanceStatus.Approved, (await _host.GetInstanceAsync(instance.Id)).Status);
    }

    [Fact]
    public async Task Manager_chain_based_on_form_field_skips_initiator_and_duplicates()
    {
        // 以收款人 bob 为基准：bob → dan → dave；发起人 dan 自己在链上被跳过
        await _host.PublishAsync("chain", Flow("ManagerChain", "2@payee", mode: "Sequential", withForm: true));
        _host.Organization.Managers["dave"] = "dan";          // 构造环：dave 的上级是 dan

        var instance = await _host.StartAsync("chain", "B1", "dan", new { payee = "bob" });

        Assert.Equal([("dave", ApprovalTaskStatus.Pending, 0)], await TasksAsync(instance.Id));
        var deep = await _host.RunAsync(sp => new ManagerChainApproverResolver(_host.Organization).ResolveAsync("10", new ApproverResolveContext
        {
            Instance = new ApprovalInstanceContext { DefinitionKey = "x", BusinessType = "x", BusinessKey = "x", InitiatorId = "alice" },
            Node = new ApprovalNodeContext("", "", NodeType.Start, ApproveMode.Any),
            FormData = new Dictionary<string, object?>()
        }, CancellationToken.None));
        Assert.Equal(["dan", "dave"], deep.Select(a => a.Value));   // 遇到环截止，同一人只出现一次
    }

    [Fact]
    public async Task Level_with_form_field_uses_first_user_of_multi_user_field()
    {
        await _host.PublishAsync("f", Flow("Manager", "1@reviewers", withForm: true));

        var instance = await _host.StartAsync("f", "B1", "alice", new { reviewers = new[] { "dave", "dan" } });

        Assert.Equal("leo", Assert.Single(await TasksAsync(instance.Id)).User);
        await _host.PublishAsync("auto", Flow("Manager", "1@payee", emptyPolicy: "AutoApprove", withForm: true));
        Assert.Equal(InstanceStatus.Approved, (await _host.StartAsync("auto", "B2", "alice", new { payee = (string?)null })).Status);
    }

    [Theory]
    [InlineData("DeptLeader", "0", "级数 '0' 无效")]
    [InlineData("Manager", "11", "级数 '11' 无效")]
    [InlineData("Manager", "abc", "级数 'abc' 无效")]
    [InlineData("DeptLeader", "1@missing", "表单字段 'missing'")]
    [InlineData("Role", "", "「角色」未设置")]
    public async Task Invalid_rule_values_fail_publishing(string type, string value, string expected)
    {
        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.PublishAsync("bad", Flow(type, value, withForm: true)));

        Assert.Equal(ApprovalErrorCode.InvalidDefinition, ex.Code);
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("Any")]
    [InlineData("All")]
    public async Task Manager_chain_requires_sequential_mode(string mode)
    {
        var ex = await Assert.ThrowsAsync<ApprovalException>(() => _host.PublishAsync("bad", Flow("ManagerChain", "2", mode: mode)));

        Assert.Contains("依次审批", ex.Message);
    }

    [Fact]
    public void Level_value_parsing()
    {
        Assert.True(ApproverLevel.TryParse(" 2@payee ", out var level));
        Assert.Equal(new ApproverLevel(2, "payee"), level);
        Assert.True(ApproverLevel.TryParse("10", out level));
        Assert.Equal(new ApproverLevel(10, null), level);
        Assert.False(ApproverLevel.TryParse("1@", out _));
        Assert.False(ApproverLevel.TryParse("1@9x", out _));
        Assert.False(ApproverLevel.TryParse(null, out _));
    }

    [Fact]
    public async Task Metadata_describes_approver_types_in_designer_order()
    {
        var meta = await _host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().Metadata));

        Assert.Equal(["Role", "User", "FormField", "Initiator", "DeptLeader", "Manager", "ManagerChain"], meta.ApproverTypes.Select(t => t.Type));
        var chain = meta.ApproverTypes.Single(t => t.Type == "ManagerChain");
        Assert.Equal((ApproverValueKind.Level, true, "逐级上级"), (chain.ValueKind, chain.Ordered, chain.DisplayName));
        Assert.Equal(ApproverValueKind.None, meta.ApproverTypes.Single(t => t.Type == "Initiator").ValueKind);
        Assert.Equal(["Assignees", "Approvers"], meta.RecipientTypes.Take(2).Select(t => t.Type));
    }

    [Fact]
    public async Task Organization_types_are_not_available_without_provider()
    {
        await using var host = new TestHost();

        var types = await host.RunAsync(sp => Task.FromResult(sp.GetRequiredService<ApprovalDefinitionService>().ApproverTypes));

        Assert.DoesNotContain(types, t => t.Type is "DeptLeader" or "Manager" or "ManagerChain");
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("f", Flow("DeptLeader", "1")));
    }
}
