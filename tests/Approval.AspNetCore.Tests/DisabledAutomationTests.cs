using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class DisabledAutomationTests
{
    [Theory]
    [InlineData("Http", "http://localhost/redirect")]
    [InlineData("Script", "while (true) {}")]
    public async Task Removed_automation_has_no_transport_or_sandbox_and_is_rejected_by_http(string type, string value)
    {
        await using var host = await ApiHost.StartAsync();
        await host.RunAsync(sp =>
        {
            Assert.Null(typeof(Approval.Core.Engine.ApprovalEngine).Assembly.GetType("Approval.Core.Actions.IApprovalActionHandler"));
            return Task.FromResult(true);
        });
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            startNodeId = "start",
            nodes = new Dictionary<string, object>
            {
                ["start"] = new { type = "Start", next = "action" },
                ["action"] = new { type = "Action", next = "end", actions = new[] { new { type, parameters = new { url = value, script = value } } } },
                ["end"] = new { type = "End" }
            }
        });
        var error = await (await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/removed", "admin", rawJson: json)).ProblemAsync(400);
        Assert.Equal("InvalidDefinition", error.GetProperty("code").GetString());
        Assert.Contains("JSON", error.GetProperty("title").GetString());
    }
}
