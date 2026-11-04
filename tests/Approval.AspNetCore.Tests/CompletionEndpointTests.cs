using Approval.Core.Abstractions;
using Approval.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class CompletionEndpointTests
{
    private const string Flow = """
        {"schemaVersion":1,"settings":{"category":"人事"},"startNodeId":"s","nodes":{"s":{"type":"Start","next":"cc"},
        "cc":{"type":"Cc","approvers":[{"type":"Role","value":"HR"}],"next":"a"},
        "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"e"},"e":{"type":"End"}}}
        """;
    private sealed class PublicDirectory : IApprovalHttpUserDirectory
    {
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>([]);
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApprovalUser>>([]);
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
    [Fact]
    public async Task Categories_are_authorized_and_governance_filters_on_server()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("hr", Flow); await host.PublishAsync("finance", Flow.Replace("人事", "财务")); await host.PublishAsync("none", Flow.Replace("人事", ""));
        Assert.Equal("人事", (await host.GetJsonAsync("/api/approval/definitions/categories", "admin"))[0].GetString());
        var filtered = await host.GetJsonAsync("/api/approval/definitions/governance?category=" + Uri.EscapeDataString("财务"), "admin");
        Assert.Equal("finance", Assert.Single(filtered.EnumerateArray()).GetProperty("key").GetString());
        var empty = await host.GetJsonAsync("/api/approval/definitions/governance?uncategorized=true", "admin"); Assert.Equal("none", Assert.Single(empty.EnumerateArray()).GetProperty("key").GetString());
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/definitions/categories", "bob")).ProblemAsync(403);
        await (await host.SendAsync(HttpMethod.Get, "/api/approval/definitions/governance?category=HR&uncategorized=true", "admin")).ProblemAsync(400);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recipients_require_visibility_and_prefer_public_directory(bool restrictedDirectory)
    {
        await using var host = await ApiHost.StartAsync(build: b => { if (restrictedDirectory) b.Services.AddSingleton<IApprovalHttpUserDirectory, PublicDirectory>(); });
        await host.PublishAsync("hr", Flow);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("hr", DocHandler.Type, "one", "alice")));
        var history = await host.GetJsonAsync($"/api/approval/instances/{instance.Id}/carbon-copies", "alice"); var id = history[0].GetProperty("id").GetGuid();
        var people = await host.GetJsonAsync($"/api/approval/carbon-copies/{id}/recipients?pageSize=1", "bob");
        Assert.True(people.GetProperty("isCurrentRoleMembership").GetBoolean()); Assert.Equal(1, people.GetProperty("total").GetInt32());
        var person = Assert.Single(people.GetProperty("items").EnumerateArray()); Assert.Equal("hana", person.GetProperty("userId").GetString()); Assert.Equal(!restrictedDirectory, person.GetProperty("isKnown").GetBoolean());
        if (restrictedDirectory) Assert.Equal(System.Text.Json.JsonValueKind.Null, person.GetProperty("name").ValueKind);
        else Assert.Equal("HANA", person.GetProperty("name").GetString());
        host.Users.Roles["unrelated"] = [];
        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/carbon-copies/{id}/recipients", "unrelated")).ProblemAsync(404);
        var unread = await host.GetJsonAsync("/api/approval/carbon-copies?unreadOnly=true", "hana"); Assert.Equal(1, unread.GetProperty("total").GetInt32());
    }
}
