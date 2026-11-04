using Approval.Core.Domain;

namespace Approval.Core.Tests;

/// <summary>列表分页与筛选：待办、我发起的、已办、抄送我的、管理员监控。</summary>
public sealed class PagingTests : IAsyncLifetime
{
    /// <summary>提交 → 抄送（用户 c1 + 角色 Watcher）→ 审核（角色 Reviewer，或签，60 分钟时限）。</summary>
    private const string Flow = """
    {
      "schemaVersion":1,
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "cc" },
        "cc":     { "name": "抄送", "type": "Cc", "approvers": [{ "type": "User", "value": "c1" }, { "type": "Role", "value": "Watcher" }], "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end",
                    "timeout": { "minutes": 60 } },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private readonly TestHost _host = new();

    public async Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("bob");
        _host.Identity.Add("r1", "Reviewer");
        _host.Identity.Add("w1", "Watcher");
        await _host.PublishAsync("f", Flow);
        await _host.PublishAsync("g", Flow);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> QueryAsync<T>(Func<ApprovalQueryService, Task<T>> query) =>
        _host.RunAsync(sp => query(sp.GetRequiredService<ApprovalQueryService>()));

    /// <summary>依次发起（每次间隔 1 分钟）：f/B1、f/B2、g/B3 由 alice 发起，f/B4 由 bob 发起。</summary>
    private async Task<List<ApprovalInstance>> SeedAsync()
    {
        var result = new List<ApprovalInstance>();
        foreach (var (key, business, user) in new[] { ("f", "B1", "alice"), ("f", "B2", "alice"), ("g", "B3", "alice"), ("f", "B4", "bob") })
        {
            result.Add(await _host.StartAsync(key, business, user));
            _host.Clock.Advance(TimeSpan.FromMinutes(1));
        }
        return result;
    }

    [Fact]
    public async Task Pages_are_ordered_by_newest_and_out_of_range_page_is_empty()
    {
        await SeedAsync();

        var first = await QueryAsync(q => q.GetStartedByAsync("alice", page: new PageRequest(1, 2)));
        var second = await QueryAsync(q => q.GetStartedByAsync("alice", page: new PageRequest(2, 2)));
        var beyond = await QueryAsync(q => q.GetStartedByAsync("alice", page: new PageRequest(5, 2)));

        Assert.Equal(3, first.Total);
        Assert.Equal(["证书 B3", "证书 B2"], first.Items.Select(i => i.Title));
        Assert.Equal(["证书 B1"], second.Items.Select(i => i.Title));
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
    }

    [Fact]
    public void Page_request_is_clamped()
    {
        Assert.Equal((1, PageRequest.DefaultPageSize), (new PageRequest().Page, new PageRequest().PageSize));
        Assert.Equal((1, PageRequest.MaxPageSize), (new PageRequest(0, 1000).Page, new PageRequest(0, 1000).PageSize));
        Assert.Equal(1, new PageRequest(-3, 0).PageSize);
    }

    [Fact]
    public async Task Instance_filters_keyword_status_flow_and_time_range()
    {
        var seeded = await SeedAsync();
        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().WithdrawAsync(seeded[0].Id, "alice"));

        async Task<string[]> Titles(InstanceQuery query) =>
            (await QueryAsync(q => q.GetStartedByAsync("alice", query))).Items.Select(i => i.Title ?? "").ToArray();

        Assert.Equal(["证书 B2"], await Titles(new InstanceQuery(Keyword: "B2")));
        Assert.Equal(["证书 B1"], await Titles(new InstanceQuery(Status: InstanceStatus.Withdrawn)));
        Assert.Equal(["证书 B3"], await Titles(new InstanceQuery(DefinitionKey: "g")));
        // [From, To)：只包含第二个（发起于 00:01）
        var start = seeded[0].CreatedAt;
        Assert.Equal(["证书 B2"], await Titles(new InstanceQuery(From: start.AddMinutes(1), To: start.AddMinutes(2))));
    }

    [Fact]
    public async Task Todo_includes_role_tasks_supports_filters_and_count()
    {
        await SeedAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(58));   // 现在 01:02：截止 01:00、01:01 的前两条已超时

        var all = await QueryAsync(q => q.GetTodoAsync("r1"));
        var byKeyword = await QueryAsync(q => q.GetTodoAsync("r1", new TodoQuery(Keyword: "bob")));
        var byFlow = await QueryAsync(q => q.GetTodoAsync("r1", new TodoQuery(DefinitionKey: "g")));
        var overdue = await QueryAsync(q => q.GetTodoAsync("r1", new TodoQuery(OverdueOnly: true)));

        Assert.Equal(4, all.Total);
        Assert.Equal(4, await QueryAsync(q => q.GetTodoCountAsync("r1")));
        Assert.Equal(["证书 B1", "证书 B2", "证书 B3", "证书 B4"], all.Items.Select(t => t.Title));
        Assert.Equal(["证书 B4"], byKeyword.Items.Select(t => t.Title));
        Assert.Equal(["证书 B3"], byFlow.Items.Select(t => t.Title));
        Assert.Equal(["证书 B1", "证书 B2"], overdue.Items.Select(t => t.Title));
        Assert.Equal(0, await QueryAsync(q => q.GetTodoCountAsync("alice")));
    }

    [Fact]
    public async Task Carbon_copied_lists_user_and_role_copies_once()
    {
        var seeded = await SeedAsync();
        // 退回后重新提交会再次抄送，列表中仍只出现一次
        var task = (await _host.TodoAsync("r1")).First(t => t.InstanceId == seeded[0].Id);
        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(task.TaskId, "r1", "补充", "start"));
        var resubmit = Assert.Single(await _host.TodoAsync("alice"));
        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.TaskId, "alice"));

        var c1 = await QueryAsync(q => q.GetCarbonCopiedAsync("c1"));
        var w1 = await QueryAsync(q => q.GetCarbonCopiedAsync("w1", new InstanceQuery(InitiatorId: "bob")));

        Assert.Equal(4, c1.Total);
        Assert.Equal(["证书 B4"], w1.Items.Select(i => i.Title));
        Assert.Empty((await QueryAsync(q => q.GetCarbonCopiedAsync("r1"))).Items);
    }

    [Fact]
    public async Task Done_and_admin_search()
    {
        await SeedAsync();
        await _host.RunAsync(async sp =>
        {
            var engine = sp.GetRequiredService<ApprovalEngine>();
            foreach (var todo in (await sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("r1", new TodoQuery(Keyword: "alice"))).Items)
                await engine.ApproveAsync(todo.TaskId, "r1");
        });

        var done = await QueryAsync(q => q.GetDoneAsync("r1", new InstanceQuery(Status: InstanceStatus.Approved)));
        var search = await QueryAsync(q => q.SearchInstancesAsync(new InstanceQuery(InitiatorId: "bob")));
        var running = await QueryAsync(q => q.SearchInstancesAsync(new InstanceQuery(Status: InstanceStatus.Running)));

        Assert.Equal(3, done.Total);
        Assert.Equal(["证书 B4"], search.Items.Select(i => i.Title));
        Assert.Equal(["证书 B4"], running.Items.Select(i => i.Title));
        Assert.Equal(4, (await QueryAsync(q => q.SearchInstancesAsync())).Total);
    }
}
