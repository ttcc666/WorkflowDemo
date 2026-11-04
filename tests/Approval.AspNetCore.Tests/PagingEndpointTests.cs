using System.Text.Json;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

/// <summary>列表接口的分页参数、筛选参数绑定与返回结构。</summary>
public sealed class PagingEndpointTests
{
    private const string Flow = """
    {
      "startNodeId": "start",
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "cc" },
        "cc":     { "name": "抄送人事", "type": "Cc", "approvers": [{ "type": "Role", "value": "HR" }], "next": "review" },
        "review": { "name": "审核", "type": "Approval", "mode": "Any", "approvers": [{ "type": "Role", "value": "Reviewer" }], "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync();
        await host.PublishAsync("doc", Flow, name: "文档审批");
        foreach (var key in new[] { "D-1", "D-2", "D-3" })
            await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
                .StartAsync(new StartApprovalRequest("doc", DocHandler.Type, key, "alice") { Title = $"文档 {key}" }));
        return host;
    }

    private static string[] Titles(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    [Fact]
    public async Task 待办分页与数量()
    {
        await using var host = await StartAsync();

        var page = await host.GetJsonAsync("/api/approval/todo?page=2&pageSize=2", "bob");
        var count = await host.GetJsonAsync("/api/approval/todo/count", "bob");

        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(["文档 D-3"], Titles(page));
        Assert.Equal(3, count.GetProperty("count").GetInt32());
        Assert.Equal(["文档 D-2"], Titles(await host.GetJsonAsync("/api/approval/todo?keyword=D-2", "bob")));
    }

    [Fact]
    public async Task 我发起的按状态关键字时间筛选()
    {
        await using var host = await StartAsync();
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));

        Assert.Equal(["文档 D-2"], Titles(await host.GetJsonAsync("/api/approval/started?status=Running&keyword=D-2", "alice")));
        Assert.Equal(3, (await host.GetJsonAsync($"/api/approval/started?from={from}&to={to}", "alice")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await host.GetJsonAsync($"/api/approval/started?to={from}", "alice")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/started?status=Approved", "alice")).GetProperty("total").GetInt32());
        // pageSize 超过上限按 100 处理
        Assert.Equal(100, (await host.GetJsonAsync("/api/approval/started?pageSize=1000", "alice")).GetProperty("pageSize").GetInt32());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/api/approval/started?status=Bogus", "alice")).StatusCode);
    }

    [Fact]
    public async Task 抄送我的_流程列表_投递记录分页()
    {
        await using var host = await StartAsync();

        var cc = await host.GetJsonAsync("/api/approval/cc?pageSize=2", "hana");
        var flows = (await host.GetJsonAsync("/api/approval/flows", "alice")).EnumerateArray().ToList();
        var outbox = await host.GetJsonAsync("/api/approval/outbox?page=1&pageSize=5", "admin");

        Assert.Equal(3, cc.GetProperty("total").GetInt32());
        Assert.Equal(2, cc.GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/cc", "bob")).GetProperty("total").GetInt32());
        Assert.Equal("doc", Assert.Single(flows).GetProperty("key").GetString());
        Assert.Equal("文档审批", flows[0].GetProperty("name").GetString());
        Assert.Equal(5, outbox.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Array, outbox.GetProperty("items").ValueKind);
    }
}
