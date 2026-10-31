using System.Text.Json;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

/// <summary>数据权限：详情 / 按业务查询 / 实例的流程定义只对有关的人可见（无权返回 404），完整流程定义仅管理员可读。</summary>
public sealed class AccessEndpointTests
{
    /// <summary>提交 → 脚本动作（参数含“密钥”）→ 审核（角色 Reviewer）。</summary>
    private const string Flow = """
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "act" },
        "act":    { "name": "预检", "type": "Action", "next": "review",
                    "actions": [{ "type": "Script", "name": "预检脚本", "parameters": { "script": "var token = 'secret-token'; form.checked = true;" } }] },
        "review": { "name": "审核", "type": "Approval", "mode": "Any",
                    "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static async Task<Guid> StartAsync(ApiHost host, string businessKey = "D-1")
    {
        await host.PublishAsync("doc", Flow, name: "文档审批");
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, businessKey, "alice") { Title = $"文档 {businessKey}" }));
        return instance.Id;
    }

    [Fact]
    public async Task 无关用户查看详情返回404_参与人与管理员可见()
    {
        await using var host = await ApiHost.StartAsync();
        var id = await StartAsync(host);

        var problem = await (await host.SendAsync(HttpMethod.Get, $"/api/approval/instances/{id}", "hana")).ProblemAsync(404);
        var bob = await host.GetJsonAsync($"/api/approval/instances/{id}", "bob");
        var admin = await host.GetJsonAsync($"/api/approval/instances/{id}", "admin");
        var alice = await host.GetJsonAsync($"/api/approval/instances/{id}", "alice");

        Assert.Equal("NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("Participant", bob.GetProperty("access").GetString());
        Assert.Equal("Full", admin.GetProperty("access").GetString());
        Assert.Equal("Full", alice.GetProperty("access").GetString());
        Assert.Equal("文档 D-1", bob.GetProperty("title").GetString());
        Assert.False(bob.TryGetProperty("concurrencyStamp", out _));
        // 不存在的实例与无权查看返回相同的结果
        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/instances/{Guid.NewGuid()}", "alice")).ProblemAsync(404);
    }

    [Fact]
    public async Task 按业务查询只返回可见的审批()
    {
        await using var host = await ApiHost.StartAsync();
        await StartAsync(host);
        const string url = $"/api/approval/instances?businessType={DocHandler.Type}&businessKey=D-1";

        Assert.Empty((await host.GetJsonAsync(url, "hana")).EnumerateArray());
        Assert.Single((await host.GetJsonAsync(url, "bob")).EnumerateArray());
    }

    [Fact]
    public async Task 实例的流程定义为公开视图且需要查看权限()
    {
        await using var host = await ApiHost.StartAsync();
        var id = await StartAsync(host);

        var definition = await host.GetJsonAsync($"/api/approval/instances/{id}/definition", "bob");
        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/instances/{id}/definition", "hana")).ProblemAsync(404);

        var json = definition.GetProperty("definitionJson").GetString()!;
        Assert.Equal("文档审批", definition.GetProperty("name").GetString());
        Assert.DoesNotContain("secret-token", json);
        var nodes = JsonDocument.Parse(json).RootElement.GetProperty("nodes");
        Assert.Equal("预检脚本", nodes.GetProperty("act").GetProperty("actions")[0].GetProperty("name").GetString());
        Assert.Equal(4, nodes.EnumerateObject().Count());
    }

    [Fact]
    public async Task 非管理员读取完整流程定义返回403()
    {
        await using var host = await ApiHost.StartAsync();
        var id = await StartAsync(host);
        var definitionId = (await host.GetJsonAsync($"/api/approval/instances/{id}", "alice")).GetProperty("definitionId").GetGuid();

        foreach (var url in new[] { "/api/approval/definitions", "/api/approval/definitions/doc", "/api/approval/definitions/doc/versions",
                     $"/api/approval/definitions/by-id/{definitionId}" })
        {
            var problem = await (await host.SendAsync(HttpMethod.Get, url, "bob")).ProblemAsync(403);
            Assert.Equal("Forbidden", problem.GetProperty("code").GetString());
            await host.GetJsonAsync(url, "admin");
        }
        Assert.False((await host.GetJsonAsync("/api/approval/me", "bob")).GetProperty("canViewDefinitions").GetBoolean());
        Assert.True((await host.GetJsonAsync("/api/approval/me", "admin")).GetProperty("canViewDefinitions").GetBoolean());
    }

    [Fact]
    public async Task DefinitionReaders角色可读取完整定义但不能发布()
    {
        await using var host = await ApiHost.StartAsync(api: o => o.DefinitionReaders.Add("Designer"));
        host.Users.Roles["dora"] = ["Designer"];
        await StartAsync(host);

        var latest = await host.GetJsonAsync("/api/approval/definitions/doc", "dora");

        Assert.Contains("secret-token", latest.GetProperty("definitionJson").GetString());
        Assert.True((await host.GetJsonAsync("/api/approval/me", "dora")).GetProperty("canViewDefinitions").GetBoolean());
        await (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/doc", "dora", rawJson: Flow)).ProblemAsync(403);
    }
}
