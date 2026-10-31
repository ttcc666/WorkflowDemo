using Approval.Core.Actions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

/// <summary>
/// 写宿主业务表的动作。mode：ok 成功；fail 写入后抛异常；save-then-fail 自行 SaveChanges 后抛异常；
/// touch-then-fail 写入并修改审批实例标题后抛异常。
/// </summary>
public sealed class WriteRecordAction(TestDbContext db) : IApprovalActionHandler
{
    public ApprovalActionDescriptor Descriptor { get; } = new("WriteRecord", "写业务记录",
    [
        new ActionParameter("name", "名称") { Required = true },
        new ActionParameter("mode", "模式") { DefaultValue = "ok" }
    ]);

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
    {
        db.Add(new TestRecord { Id = Guid.NewGuid(), Name = context.Get("name")! });
        switch (context.Get("mode"))
        {
            case "fail":
                throw new InvalidOperationException("动作失败");
            case "save-then-fail":
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException("保存后失败");
            case "touch-then-fail":
                context.Instance.Title = "被动作修改";
                throw new InvalidOperationException("修改实例后失败");
            default:
                return ApprovalActionResult.Empty;
        }
    }
}

/// <summary>动作失败时撤销其做了一半的数据修改（异步：Outbox 投递；同步：失败继续）。</summary>
public sealed class ActionRollbackTests : IAsyncLifetime
{
    private readonly TestHost _host = new(build: b => b.AddActionHandler<WriteRecordAction>());

    public Task InitializeAsync()
    {
        _host.Identity.Add("alice");
        _host.Identity.Add("bob", "Reviewer");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>提交 → 动作 →（可选）审核 → 结束。</summary>
    private static string Flow(string mode, string actionMode = "Async", bool continueOnError = false, bool reviewFirst = false, string name = "R1") =>
        $$"""
        {
          "startNodeId": "start",
          "nodes": {
            "start":  { "name": "提交", "type": "Start", "next": "{{(reviewFirst ? "review" : "act")}}" },
            "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "{{(reviewFirst ? "act" : "end")}}" },
            "act":    { "name": "写记录", "type": "Action", "next": "{{(reviewFirst ? "end" : "review")}}",
                        "actions": [{ "type": "WriteRecord", "mode": "{{actionMode}}", "continueOnError": {{(continueOnError ? "true" : "false")}},
                                      "parameters": { "name": "{{name}}", "mode": "{{mode}}" } }] },
            "end":    { "name": "结束", "type": "End" }
          }
        }
        """;

    private Task<List<string>> RecordsAsync() =>
        _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<TestRecord>().Select(r => r.Name).OrderBy(n => n).ToListAsync());

    // ───────────── 异步动作（Outbox） ─────────────

    [Theory]
    [InlineData("fail")]
    [InlineData("save-then-fail")]
    public async Task Async_failure_discards_partial_changes_and_records_the_error(string mode)
    {
        await _host.PublishAsync("f", Flow(mode));
        await _host.StartAsync("f", "B1", "alice");

        await _host.DispatchOutboxAsync();

        Assert.Empty(await RecordsAsync());
        var message = Assert.Single(await _host.OutboxAsync());
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("失败", message.LastError);

        // 重试仍失败：不会累积写入
        _host.Clock.Advance(TimeSpan.FromHours(1));
        await _host.DispatchOutboxAsync();
        Assert.Empty(await RecordsAsync());
        Assert.Equal(2, Assert.Single(await _host.OutboxAsync()).Attempts);
    }

    [Fact]
    public async Task Async_success_commits_changes_with_the_result()
    {
        await _host.PublishAsync("f", Flow("ok"));
        await _host.StartAsync("f", "B1", "alice");

        await _host.DispatchOutboxAsync();

        Assert.Equal(["R1"], await RecordsAsync());
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await _host.OutboxAsync()).Status);
    }

    [Fact]
    public async Task Async_changes_that_cannot_be_saved_count_as_a_failed_attempt()
    {
        await _host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "R1" });
            await db.SaveChangesAsync();
        });
        await _host.PublishAsync("f", Flow("ok"));
        await _host.StartAsync("f", "B1", "alice");

        await _host.DispatchOutboxAsync();

        // 唯一约束冲突：记录失败并安排重试，而不是停在租约中
        Assert.Equal(["R1"], await RecordsAsync());
        var message = Assert.Single(await _host.OutboxAsync());
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("DbUpdateException", message.LastError);
    }

    // ───────────── 同步动作：失败继续 ─────────────

    [Fact]
    public async Task Sync_continue_on_error_discards_changes_made_before_the_failure()
    {
        await _host.PublishAsync("f", Flow("touch-then-fail", "Sync", continueOnError: true));

        var instance = await _host.StartAsync("f", "B1", "alice");

        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal("review", saved.CurrentNodeId);
        Assert.Equal("证书 B1", saved.Title);
        Assert.Empty(await RecordsAsync());
        Assert.Contains(await _host.LogsAsync(instance.Id), l => l.Comment?.Contains("执行失败（已忽略）") == true);
    }

    [Fact]
    public async Task Sync_rollback_keeps_engine_changes_made_before_the_action()
    {
        // 审核通过后才执行动作：实例在动作前已被引擎修改（状态、当前节点），回滚只撤销动作的修改
        await _host.PublishAsync("f", Flow("touch-then-fail", "Sync", continueOnError: true, reviewFirst: true));
        var instance = await _host.StartAsync("f", "B1", "alice");

        await _host.ApproveMyTaskAsync("bob");

        var saved = await _host.GetInstanceAsync(instance.Id);
        Assert.Equal(InstanceStatus.Approved, saved.Status);
        Assert.Equal("证书 B1", saved.Title);
        Assert.Contains(saved.Tasks, t => t.Status == ApprovalTaskStatus.Approved && t.HandledBy == "bob");
        Assert.Empty(await RecordsAsync());
    }

    [Fact]
    public async Task Sync_success_saves_changes_with_the_approval()
    {
        await _host.PublishAsync("f", Flow("ok", "Sync"));

        await _host.StartAsync("f", "B1", "alice");

        Assert.Equal(["R1"], await RecordsAsync());
    }
}
