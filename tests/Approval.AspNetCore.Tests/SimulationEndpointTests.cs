using System.Net;
using System.Text.Json;
using Approval.Core.Domain;
using Approval.Core.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class SimulationEndpointTests
{
    private const string Flow = """{"schemaVersion":1,"startNodeId":"s","nodes":{"s":{"type":"Start","next":"cc"},"cc":{"type":"Cc","approvers":[{"type":"Initiator","value":""}],"next":"e"},"e":{"type":"End"}}}""";

    [Fact]
    public async Task SimulationBudgetExceededReturnsExplicitErrorsWithoutWrites()
    {
        await using var host = await ApiHost.StartAsync();
        var flow = FlowDefinition.Parse(Flow); flow.Nodes.Remove("cc"); flow.Nodes["s"].Next = "c0";
        for (var index = 0; index < 11; index++) flow.Nodes[$"c{index}"] = new()
        {
            Id = $"c{index}", Type = NodeType.Cc, Next = index == 10 ? "e" : $"c{index + 1}",
            Approvers = [new("User", string.Join(',', Enumerable.Repeat("bob", 200)))]
        };
        using var response = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/simulate", "admin",
            new { definitionJson = flow.ToJson(), formData = new { } }, withOperationId: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("isValid").GetBoolean()); Assert.False(body.GetProperty("isComplete").GetBoolean());
        Assert.Contains("解析对象累计超过 2000", Assert.Single(body.GetProperty("errors").EnumerateArray()).GetString());
        var failed = body.GetProperty("steps").EnumerateArray().Last();
        Assert.Equal("c10", failed.GetProperty("nodeId").GetString()); Assert.Equal("Failed", failed.GetProperty("status").GetString());
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApiDbContext>();
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalTask>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().CountAsync());
            return true;
        });
    }

    [Fact]
    public async Task SimulationNeedsAuthorization_ButNoKey_AndCannotImpersonateApplicant()
    {
        await using var host = await ApiHost.StartAsync();
        var request = new { definitionJson = Flow, formData = new { amount = 1 }, initiatorId = "bob" };
        using var anonymous = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/simulate", null, request, withOperationId: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var denied = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/simulate", "alice", request, withOperationId: false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var allowed = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/simulate", "admin", request, withOperationId: false);
        Assert.True(allowed.IsSuccessStatusCode, await allowed.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("isComplete").GetBoolean());
        var copy = body.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("nodeId").GetString() == "cc");
        Assert.Equal("admin", copy.GetProperty("resolvedAssignees")[0].GetProperty("value").GetString());
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalInstance>().CountAsync()));
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalOperation>().CountAsync()));
    }

    [Theory]
    [InlineData("not-json", "{}")]
    [InlineData(Flow, "[]")]
    public async Task InvalidSimulationInputReturnsExplicitErrors(string definition, string form)
    {
        await using var host = await ApiHost.StartAsync();
        using var response = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/simulate", "admin",
            new { definitionJson = definition, formData = JsonSerializer.Deserialize<JsonElement>(form) }, withOperationId: false);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("isValid").GetBoolean()); Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
    }
}
