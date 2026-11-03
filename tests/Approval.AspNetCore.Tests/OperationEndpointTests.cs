using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class OperationEndpointTests
{
    private const string Flow = """
        {"startNodeId":"start","settings":{"startable":true,"titleTemplate":"申请"},"form":{"fields":[{"key":"amount","label":"金额","type":"Number","required":true}]},
        "nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End","name":"End"}}}
        """;
    private static async Task<HttpResponseMessage> Send(ApiHost host, HttpMethod method, string url, string user, string? key, object body)
    {
        using var request = new HttpRequestMessage(method, url); request.Headers.Add(HeaderCurrentUser.Header, user);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(body); return await host.Client.SendAsync(request);
    }
    private static Task Seed(ApiHost host) => host.RunAsync(async sp => { await sp.GetRequiredService<Approval.Core.Abstractions.IApprovalUnitOfWork>().ExecuteAsync(token => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync("doc", "Doc", Flow, token)); return true; });

    [Fact]
    public async Task FormIntent_ReplaysBeforeLatestSchemaValidation_AndChangedBodyConflicts()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddFormApprovals()); await Seed(host);
        var body = new { formData = new { amount = 1 }, comment = "one" };
        using var first = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", "intent", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
        await host.RunAsync(async sp => { await sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync("doc", "New", Flow.Replace("\"key\":\"amount\"", "\"key\":\"newRequired\"")); return true; });
        using var replay = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", "intent", body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid());
        using var changed = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", "intent", new { formData = new { amount = 2 }, comment = "one" });
        await changed.ProblemAsync(409);
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<ApiDbContext>(); Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync()); return true; });
    }

    [Fact]
    public async Task ApprovalCommand_ReplaysNoContentWithoutAdvancingAgain()
    {
        await using var host = await ApiHost.StartAsync(); await Seed(host);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("doc", DocHandler.Type, "one", "alice")));
        var task = Assert.Single(instance.Tasks);
        foreach (var _ in new[] { 1, 2 })
        {
            using var response = await Send(host, HttpMethod.Post, $"/api/approval/tasks/{task.Id}/approve", "bob", "approve", new { comment = "ok" });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<ApiDbContext>(); Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync()); Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Approve)); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync()); return true; });
    }

    [Fact]
    public async Task StrictMode_RejectsMissingKey_ButValidationDoesNotCreateCommandRecord()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddFormApprovals(), workflow: o => o.RequireOperationId = true); await Seed(host);
        using var missing = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", null, new { formData = new { amount = 1 } });
        Assert.Equal("OperationIdRequired", (await missing.ProblemAsync(400)).GetProperty("code").GetString());
        using var invalid = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", "recoverable", new { formData = new { } }); await invalid.ProblemAsync(400);
        using var corrected = await Send(host, HttpMethod.Post, "/api/approval/forms/doc/start", "alice", "recoverable", new { formData = new { amount = 1 } });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalOperation>().CountAsync()));
    }

    [Fact]
    public async Task DraftCreation_UsesStableKeyAndReturnsOriginalDraft()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddFormApprovals()); await Seed(host);
        Guid? id = null;
        foreach (var _ in new[] { 1, 2 })
        {
            using var response = await Send(host, HttpMethod.Put, "/api/approval/forms/doc/draft", "alice", "draft", new { formData = new { amount = 1 } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var current = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draftId").GetGuid();
            if (id is { } first) Assert.Equal(first, current); id = current;
        }
        Assert.Equal(1, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalDraft>().CountAsync()));
        foreach (var _ in new[] { 1, 2 })
        {
            using var deleted = await Send(host, HttpMethod.Delete, $"/api/approval/forms/drafts/{id}", "alice", "delete", new { });
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalDraft>().CountAsync()));
    }
}
