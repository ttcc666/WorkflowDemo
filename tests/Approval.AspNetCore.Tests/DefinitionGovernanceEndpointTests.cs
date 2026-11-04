using Approval.Forms;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class DefinitionGovernanceEndpointTests
{
    private const string Flow = """
        {"startNodeId":"start","settings":{"startable":true},"nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private static async Task<JsonElement> Command(ApiHost host, HttpMethod method, string suffix, string key, object body)
    {
        using var request = new HttpRequestMessage(method, "/api/approval/definitions/doc/" + suffix);
        request.Headers.Add(HeaderCurrentUser.Header, "admin"); request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(body); var response = await host.Client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task DraftPublish_OriginalKeysReplayAndChangedRevisionReturns409_WithAtomicAudit()
    {
        await using var host = await ApiHost.StartAsync(workflow: o => o.RequireOperationId = true);
        var body = new { name = "Doc", definitionJson = Flow, expectedRevision = 0 };
        var saved = await Command(host, HttpMethod.Put, "draft", "draft-1", body);
        Assert.Equal(1, saved.GetProperty("revision").GetInt64());
        Assert.Equal(saved, await Command(host, HttpMethod.Put, "draft", "draft-1", body), JsonElementComparer.Instance);
        var publish = new { expectedRevision = 1, changeNote = "first publish" };
        var result = await Command(host, HttpMethod.Post, "publish-draft", "publish-1", publish);
        Assert.Equal(1, result.GetProperty("version").GetInt32());
        Assert.Equal(result.GetProperty("id").GetGuid(), (await Command(host, HttpMethod.Post, "publish-draft", "publish-1", publish)).GetProperty("id").GetGuid());
        var conflict = await host.SendAsync(HttpMethod.Put, "/api/approval/definitions/doc/draft", "admin", body);
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode); // strict missing-key gate
        using var stale = new HttpRequestMessage(HttpMethod.Put, "/api/approval/definitions/doc/draft");
        stale.Headers.Add(HeaderCurrentUser.Header, "admin"); stale.Headers.Add("Idempotency-Key", "stale"); stale.Content = JsonContent.Create(body);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.SendAsync(stale)).StatusCode);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApiDbContext>();
            Assert.Single(await db.Set<ApprovalDefinition>().ToListAsync());
            var audits = await db.Set<ApprovalDefinitionAudit>().OrderBy(a => a.Revision).ToListAsync();
            Assert.Equal(2, audits.Count); Assert.Equal("admin", audits[1].ActorId); Assert.Equal("first publish", audits[1].ChangeNote);
            Assert.Equal(2, await db.Set<ApprovalOperation>().CountAsync()); return true;
        });
    }
    [Fact]
    public async Task Disable_HidesStartableButPreservesHistory_AndOrdinaryUserCannotGovern()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddApprovalForms()); await host.PublishAsync("doc", Flow);
        var state = await host.GetJsonAsync("/api/approval/definitions/doc/governance", "admin");
        var disabled = await Command(host, HttpMethod.Post, "enabled", "disable", new { enabled = false, expectedRevision = state.GetProperty("revision").GetInt64(), changeNote = "retired" });
        Assert.False(disabled.GetProperty("isEnabled").GetBoolean()); Assert.Equal(JsonValueKind.Null, disabled.GetProperty("draftJson").ValueKind);
        Assert.Empty((await host.GetJsonAsync("/api/approval/forms/startable", "alice")).EnumerateArray());
        Assert.Single((await host.GetJsonAsync("/api/approval/definitions/doc/versions", "admin")).EnumerateArray());
        var denied = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/doc/enabled", "alice", new { enabled = true, expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var audit = await host.GetJsonAsync("/api/approval/definitions/doc/audit", "admin");
        Assert.Equal(2, audit.GetProperty("total").GetInt32());
    }
    private sealed class JsonElementComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementComparer Instance = new();
        public bool Equals(JsonElement x, JsonElement y) => x.GetRawText() == y.GetRawText();
        public int GetHashCode(JsonElement obj) => obj.GetRawText().GetHashCode();
    }
}
