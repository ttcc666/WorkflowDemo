using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Background;
using Approval.Core;
using Approval.ReferenceHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Approval.Tests.Infrastructure;

internal sealed class ReferenceHostScenario : IAsyncDisposable
{
    public const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true,"titleTemplate":"参考申请"},
        "form":{"fields":[{"key":"amount","label":"金额","type":"Number","required":true}]},
        "nodes":{"start":{"type":"Start","name":"提交","next":"review"},
        "review":{"type":"Approval","name":"初审","approvers":[{"type":"Role","value":"Reviewer"}],"next":"final"},
        "final":{"type":"Approval","name":"终审","approvers":[{"type":"Role","value":"FinalReviewer"}],"next":"end"},
        "end":{"type":"End","name":"完成"}}}
        """;
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public static Dictionary<string, string?> Configuration(string provider, string connection)
    {
        var config = new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "Demo", ["Database:Provider"] = provider,
            ["ConnectionStrings:Approval"] = connection, ["Approval:EnableBackgroundServices"] = "false",
            ["Approval:FileRoot"] = Path.Combine(Path.GetTempPath(), "approval-reference-files-" + Guid.NewGuid().ToString("N"))
        };
        var index = 0;
        foreach (var tenant in new[] { "alpha" })
        foreach (var user in new[] { "admin", "alice", "bob", "carol" })
        {
            config[$"Authentication:DemoIdentities:{index}:UserId"] = user;
            config[$"Authentication:DemoIdentities:{index}:Token"] = $"reference-test-{tenant}-{user}-" + new string('x', 32);
            index++;
        }
        return config;
    }
    public static void ConfigureBuilder(WebApplicationBuilder builder, Dictionary<string, string?> config)
    {
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ReferenceHostConfiguration.json"));
        builder.Configuration.AddInMemoryCollection(config);
    }
    private ReferenceHostScenario(WebApplication app) { _app = app; Client = app.GetTestClient(); }
    public static async Task<ReferenceHostScenario> StartAsync(Dictionary<string, string?> config)
    {
        var app = ReferenceApplication.Build(["--environment", "Development"], b => ConfigureBuilder(b, config));
        try
        {
            await ReferenceDatabase.MigrateAsync(app.Services);
            await app.StartAsync();
            return new(app);
        }
        catch { await app.DisposeAsync(); throw; }
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, string tenant = "alpha", string user = "alice",
        string? key = null, object? body = null, string? raw = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", $"reference-test-{tenant}-{user}-" + new string('x', 32));
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (raw is not null) request.Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    public async Task<JsonElement> Get(string path, string tenant = "alpha", string user = "alice")
    {
        using var response = await Send(HttpMethod.Get, path, tenant, user);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public async Task Publish(string tenant, string key = "shared", string flow = Flow, string operation = "publish-v1")
    {
        using var response = await Send(HttpMethod.Post, $"/api/approval/definitions/{key}?name=Reference", tenant, "admin", operation, raw: flow);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
    public async Task<Guid> Start(string tenant, string operation = "same-start-key")
    {
        using var response = await Send(HttpMethod.Post, "/api/approval/forms/shared/start", tenant, key: operation,
            body: new { formData = new { amount = 12 }, comment = "same-content" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
    }
    private async Task Grant(string tenant, string user, string permission, bool allowed)
    {
        using var response = await Send(HttpMethod.Put, $"/demo/grants/{user}/{permission}", tenant, "admin", body: new { allowed });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    public async Task SingleHostWorkflow()
    {
        await Publish("alpha"); var id = await Start("alpha");
        Assert.Equal(id, await Start("alpha"));
        var original = (await Get($"/api/approval/instances/{id}")).GetProperty("definitionId").GetGuid();
        await Publish("alpha", flow: Flow.Replace("\"key\":\"amount\"", "\"key\":\"newRequired\""), operation: "publish-v2");
        Assert.Equal(id, await Start("alpha"));
        Assert.Equal(original, (await Get($"/api/approval/instances/{id}")).GetProperty("definitionId").GetGuid());
        using var conflicting = await Send(HttpMethod.Post, "/api/approval/forms/shared/start", key: "same-start-key", body: new { formData = new { amount = 13 }, comment = "same-content" });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        await Grant("alpha", "alice", ApprovalPermissions.InstanceRead, false);
        using var denied = await Send(HttpMethod.Post, "/api/approval/forms/shared/start", key: "same-start-key", body: new { formData = new { amount = 12 }, comment = "same-content" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Grant("alpha", "alice", ApprovalPermissions.InstanceRead, true);
        var task = Assert.Single((await Get("/api/approval/todo", user: "bob")).GetProperty("items").EnumerateArray()).GetProperty("taskId").GetGuid();
        await Grant("alpha", "bob", ApprovalPermissions.TaskApprove, false);
        using var noApprove = await Send(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", user: "bob", key: "review", body: new { comment = "ok" });
        Assert.Equal(HttpStatusCode.Forbidden, noApprove.StatusCode);
        await Grant("alpha", "bob", ApprovalPermissions.TaskApprove, true);
        using var approved = await Send(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", user: "bob", key: "review", body: new { comment = "ok" });
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        using var replay = await Send(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", user: "bob", key: "review", body: new { comment = "ok" });
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        var finalTask = Assert.Single((await Get("/api/approval/todo", user: "carol")).GetProperty("items").EnumerateArray()).GetProperty("taskId").GetGuid();
        using var final = await Send(HttpMethod.Post, $"/api/approval/tasks/{finalTask}/approve", user: "carol", key: "final", body: new { comment = "done" });
        Assert.Equal(HttpStatusCode.NoContent, final.StatusCode);
        Assert.Equal("Approved", (await Get($"/api/approval/instances/{id}")).GetProperty("status").GetString());
        await using var scope = await _app.Services.GetRequiredService<IApprovalScopeFactory>().CreateAsync();
        var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
        Assert.Equal(1, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalBusinessSlot>().CountAsync());
        Assert.Equal(2, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Approve));
        Assert.False(db.Database.HasPendingModelChanges());
    }
    public async Task TwentyRequestsAcross(ReferenceHostScenario second)
    {
        await Publish("alpha");
        var ids = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            var host = i % 2 == 0 ? this : second;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                using var result = await host.Send(HttpMethod.Post, "/api/approval/forms/shared/start", key: "parallel",
                    body: new { formData = new { amount = 12 }, comment = "same-content" });
                if (result.StatusCode == HttpStatusCode.OK)
                    return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
                Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
            }
            throw new InvalidOperationException("Concurrent command did not converge within the bounded retry limit.");
        }));
        var winner = Assert.Single(ids.Distinct());
        await using var scope = await _app.Services.GetRequiredService<IApprovalScopeFactory>().CreateAsync();
        var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
        Assert.Equal(winner, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
        Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync());
        Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync());
        Assert.Equal(2, await db.Set<ApprovalOperation>().CountAsync()); // publication + one start
    }
    public async Task BackgroundIsolation()
    {
        await Publish("alpha"); var id = await Start("alpha");
        var runner = _app.Services.GetRequiredService<ApprovalBackgroundRunner>();
        Assert.Equal(0, await runner.RunAsync("probe", (_, _) => Task.FromException<int>(new InvalidOperationException("controlled failure"))));
        var count = await runner.RunAsync("probe", async (sp, ct) =>
        {
            var actor = sp.GetRequiredService<IApprovalActorContext>(); Assert.True(actor.IsSystem);
            Assert.True(await sp.GetRequiredService<IApprovalPermissionProvider>().AuthorizeAsync(new(new(actor.ActorId, true), ApprovalPermissions.TaskTimeout, new()), ct));
            Assert.Equal(id, (await sp.GetRequiredService<ReferenceDbContext>().Set<ApprovalInstance>().SingleAsync(ct)).Id);
            return 1;
        });
        Assert.Equal(1, count);
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await _app.DisposeAsync(); }
}
