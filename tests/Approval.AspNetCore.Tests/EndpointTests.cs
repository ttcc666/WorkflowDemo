using System.Text.Json;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class EndpointTests
{
    private const string Flow = """
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any",
                    "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    [Fact]
    public async Task 未识别当前用户返回401()
    {
        await using var host = await ApiHost.StartAsync();

        var problem = await (await host.SendAsync(HttpMethod.Get, "/api/approval/todo", user: null)).ProblemAsync(401);

        Assert.Equal("Unauthorized", problem.GetProperty("code").GetString());
        // 不读取用户的接口（如元数据）同样要求登录
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/meta", user: null)).ProblemAsync(401);
    }

    [Fact]
    public async Task Me返回角色与管理员标记()
    {
        await using var host = await ApiHost.StartAsync();

        var admin = await host.GetJsonAsync("/api/approval/me", "admin");
        var bob = await host.GetJsonAsync("/api/approval/me", "bob");

        Assert.Equal("admin", admin.GetProperty("userId").GetString());
        Assert.Equal("ADMIN", admin.GetProperty("name").GetString());
        Assert.True(admin.GetProperty("isAdmin").GetBoolean());
        Assert.False(bob.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(["Reviewer"], bob.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task 非管理员发布流程返回403()
    {
        await using var host = await ApiHost.StartAsync();

        var problem = await (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/doc", "bob", rawJson: Flow)).ProblemAsync(403);

        Assert.Equal("Forbidden", problem.GetProperty("code").GetString());
        await host.PublishAsync("doc", Flow);
    }

    [Fact]
    public async Task 待办返回流程名称且枚举为字符串()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("doc", Flow, name: "文档审批");
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, "D-1", "alice") { Title = "文档 D-1" }));

        var todo = Assert.Single((await host.GetJsonAsync("/api/approval/todo", "bob")).EnumerateArray());
        var started = Assert.Single((await host.GetJsonAsync("/api/approval/started", "alice")).EnumerateArray());

        Assert.Equal("文档审批", todo.GetProperty("definitionName").GetString());
        Assert.Equal("Role", todo.GetProperty("assigneeKind").GetString());
        Assert.Equal("Approval", todo.GetProperty("kind").GetString());
        Assert.Equal("文档审批", started.GetProperty("definitionName").GetString());
        Assert.Equal("Running", started.GetProperty("status").GetString());
    }

    [Fact]
    public async Task 审批异常转换为ProblemDetails()
    {
        await using var host = await ApiHost.StartAsync();

        var notFound = await (await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{Guid.NewGuid()}/approve", "bob", new { }))
            .ProblemAsync(404);

        Assert.Equal("NotFound", notFound.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(notFound.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task 字符串枚举可用于请求体()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("doc", Flow);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, "D-2", "alice")));
        var taskId = (await host.GetJsonAsync("/api/approval/todo", "bob")).EnumerateArray().Single().GetProperty("taskId").GetGuid();

        var invalid = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{taskId}/add-sign", "bob",
            new { userIds = new[] { "hana" }, mode = "Bogus" });
        await host.PostJsonAsync($"/api/approval/tasks/{taskId}/add-sign", "bob",
            new { userIds = new[] { "hana" }, mode = "Parallel", comment = "请协助" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
        var signTask = Assert.Single((await host.GetJsonAsync("/api/approval/todo", "hana")).EnumerateArray());
        Assert.Equal("AddSignParallel", signTask.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task 用户目录()
    {
        await using var host = await ApiHost.StartAsync();

        var users = (await host.GetJsonAsync("/api/approval/directory/users?keyword=a", "alice")).EnumerateArray()
            .Select(u => u.GetProperty("id").GetString()).ToList();
        var byIds = (await host.GetJsonAsync("/api/approval/directory/users?ids=bob,hana", "alice")).EnumerateArray()
            .Select(u => u.GetProperty("name").GetString()).ToList();
        var roles = (await host.GetJsonAsync("/api/approval/directory/roles", "alice")).EnumerateArray().Select(r => r.GetString());

        Assert.Equal(["alice", "admin", "hana"], users);
        Assert.Equal(["BOB", "HANA"], byIds);
        Assert.Equal(["Admin", "HR", "Reviewer"], roles);
    }

    [Fact]
    public async Task 未注册用户目录返回501()
    {
        await using var host = await ApiHost.StartAsync(withDirectory: false);

        var problem = await (await host.SendAsync(HttpMethod.Get, "/api/approval/directory/roles", "alice")).ProblemAsync(501);

        Assert.Equal("DirectoryNotConfigured", problem.GetProperty("code").GetString());
        // me 仍可用，名称退化为用户 ID
        Assert.Equal("alice", (await host.GetJsonAsync("/api/approval/me", "alice")).GetProperty("name").GetString());
    }
}
