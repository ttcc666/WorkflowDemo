using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Forms;
using Approval.Forms.Runtime;
using System.Text.Json;

namespace Approval.Core.Tests;

public sealed class RuntimeQueryOptimizationTests
{
    private sealed class Directory(bool includeSecond) : IApprovalUserDirectory
    {
        public List<string[]> Calls { get; } = [];
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            Calls.Add(ids.ToArray());
            return Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Where(id => id != "two" || includeSecond)
                .Select(id => new ApprovalUser(id, id, [])).ToArray());
        }
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>([]);
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserFields_ShareOneDirectoryReadAndKeepErrorsOnTheOriginalField(bool includeSecond)
    {
        var directory = new Directory(includeSecond);
        await using var host = new TestHost(build: b => { b.AddApprovalForms(); b.Services.AddSingleton<IApprovalUserDirectory>(directory); });
        await host.PublishAsync("users", """
            {"startNodeId":"start","settings":{"startable":true},"form":{"fields":[
              {"key":"first","label":"First","type":"User"},{"key":"second","label":"Second","type":"Users"}]},
              "nodes":{"start":{"type":"Start","next":"review"},"review":{"type":"Approval",
                "approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
            """);
        using var data = JsonDocument.Parse("{\"first\":\"one\",\"second\":[\"one\",\"two\"]}");
        Task<ApprovalInstance> Start() => host.RunAsync(sp => sp.GetRequiredService<ApprovalFormService>().StartAsync("users", "alice", data.RootElement));
        if (includeSecond)
        {
            var instance = await Start();
            using var saved = JsonDocument.Parse(instance.FormDataJson);
            Assert.Equal("one", saved.RootElement.GetProperty("first").GetString());
            Assert.Equal(new[] { "one", "two" }, saved.RootElement.GetProperty("second").EnumerateArray().Select(v => v.GetString()));
        }
        else
        {
            var error = await Assert.ThrowsAsync<ApprovalValidationException>(Start);
            Assert.Equal(new[] { "second" }, error.Errors.Keys);
            Assert.Equal("Second中的用户不存在：two", Assert.Single(error.Errors["second"]));
        }
        var validationRead = Assert.Single(directory.Calls, ids => ids.Contains("one"));
        Assert.Equal(new[] { "one", "two" }, validationRead);
    }

    [Fact]
    public async Task Backlog_StreamsOrdinalAssigneesAndPreservesCountsDeadlinesAndOldestArrival()
    {
        await using var host = new TestHost();
        var timed = Flows.SingleNode("Any", "User", "bob").Replace("\"mode\": \"Any\"", "\"mode\": \"Any\", \"timeout\": { \"minutes\": 60 }");
        await host.PublishAsync("lower", timed);
        await host.PublishAsync("upper", Flows.SingleNode("Any", "User", "BOB"));
        var oldest = host.Clock.GetUtcNow().UtcDateTime;
        await host.StartAsync("lower", "one", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(15));
        await host.StartAsync("lower", "two", "alice");
        await host.StartAsync("upper", "three", "alice");
        host.Clock.Advance(TimeSpan.FromMinutes(75));
        var stats = await host.RunAsync(sp => sp.GetRequiredService<ApprovalStatisticsService>()
            .GetStatisticsAsync(new(oldest.AddDays(-1), oldest.AddDays(1))));
        Assert.Equal(3, stats.Summary.Running);
        Assert.Equal(2, stats.Backlog.Count);
        Assert.Equal(new[] { "bob", "BOB" }, stats.Backlog.Select(b => b.AssigneeValue));
        var lower = stats.Backlog[0];
        Assert.Equal(2, lower.Count); Assert.Equal(2, lower.Overdue); Assert.Equal(oldest, lower.OldestCreatedAt);
        var upper = stats.Backlog[1];
        Assert.Equal(1, upper.Count); Assert.Equal(0, upper.Overdue); Assert.Equal(oldest.AddMinutes(15), upper.OldestCreatedAt);
    }
}
