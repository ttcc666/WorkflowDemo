using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class CarbonCopyEndpointTests
{
    [Fact]
    public async Task Carbon_copy_endpoints_page_read_and_restrict_owner_using_server_identity()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("cc-center", """
            {"schemaVersion":1,"startNodeId":"s","nodes":{"s":{"type":"Start","next":"cc"},
              "cc":{"type":"Cc","approvers":[{"type":"Role","value":"HR"}],"next":"a"},
              "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"e"},"e":{"type":"End"}}}
            """);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("cc-center", DocHandler.Type, "CC-1", "alice")));
        var page = await host.GetJsonAsync("/api/approval/carbon-copies?unreadOnly=true&pageSize=1", "hana");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        await (await host.SendAsync(HttpMethod.Post, $"/api/approval/carbon-copies/{id}/read", "bob", new { userId = "hana" })).ProblemAsync(404);
        var read = await host.SendAsync(HttpMethod.Post, $"/api/approval/carbon-copies/{id}/read", "hana");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/carbon-copies?unreadOnly=true", "hana")).GetProperty("total").GetInt32());
        var history = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}/carbon-copies", "alice");
        Assert.Equal("HR", Assert.Single(history.EnumerateArray()).GetProperty("assigneeValue").GetString());
        Assert.Equal(0, (await host.GetJsonAsync("/api/approval/todo/count", "hana")).GetProperty("count").GetInt32());
    }
}
