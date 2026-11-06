using System.Net;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class OutboxPayloadEndpointTests
{
    [Fact]
    public async Task Lists_metadata_and_checks_both_permissions_and_instance_access_before_reading_payload()
    {
        var permissions = new Permissions();
        var scope = new Scope();
        await using var host = await ApiHost.StartAsync(workflow: o => o.Outbox.EnableDispatcher = true, build: builder =>
        {
            builder.AddActionHandler<AsyncAction>();
            builder.Services.AddSingleton<IApprovalEventHandler, EventSink>();
            builder.Services.AddSingleton<IApprovalPermissionProvider>(permissions);
            builder.Services.AddSingleton<IApprovalQueryScope>(scope);
        });
        await host.PublishAsync("outbox", """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"send"},
              "send":{"type":"Action","next":"review","actions":[
                {"id":"enqueue","type":"Fixture","mode":"Async","failurePolicy":"Retry","config":{"secret":"action-body"}}
              ]},"review":{"type":"Approval","next":"end","approvers":[{"type":"User","value":"bob"}]},"end":{"type":"End"}
            }}
            """);
        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
            .StartAsync(new("outbox", DocHandler.Type, "host-body", "alice")));
        var ids = await host.RunAsync(async sp =>
        {
            var connection = sp.GetRequiredService<ApiDbContext>().Database.GetDbConnection();
            await using var db = new StorageContext(new DbContextOptionsBuilder<StorageContext>().UseSqlite(connection).Options);
            var rows = await db.Set<ApprovalOutboxMessage>().Where(m => m.InstanceId == instance.Id).ToListAsync();
            var action = Assert.Single(rows, r => r.Target == "WorkflowAction");
            var approvalEvent = rows.First(r => r.Target == "Host");
            approvalEvent.Description = "old rendered title secret";
            approvalEvent.LastError = "old exception response secret";
            var system = new ApprovalOutboxMessage
            {
                Id = Guid.NewGuid(), Target = "SystemFixture", Status = OutboxStatus.Failed,
                Description = "system title secret", PayloadJson = "{\"secret\":\"system-body\"}"
            };
            var mismatch = new ApprovalOutboxMessage
            {
                Id = Guid.NewGuid(), Target = "Host", InstanceId = instance.Id, Status = OutboxStatus.Failed,
                PayloadJson = approvalEvent.PayloadJson
            };
            db.AddRange(system, mismatch);
            await db.SaveChangesAsync();
            return (Action: action.Id, Host: approvalEvent.Id, System: system.Id, Mismatch: mismatch.Id);
        });

        Task<HttpResponseMessage> Payload(Guid id, string actor = "alice") => host.SendAsync(HttpMethod.Get, $"/api/approval/outbox/{id}/payload", actor);
        permissions.AllowOutbox = false;
        using (var denied = await Payload(ids.Action)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await host.SendAsync(HttpMethod.Get, "/api/approval/outbox", "alice")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        permissions.AllowOutbox = true;
        permissions.AllowSensitive = false;
        var list = await host.GetJsonAsync("/api/approval/outbox?page=1&pageSize=30", "alice");
        var rows = list.GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetGuid());
        Assert.Equal("Fixture/enqueue", rows[ids.Action].GetProperty("description").GetString());
        Assert.Equal("ApprovalEvent", rows[ids.Host].GetProperty("description").GetString());
        Assert.Equal("DeliveryFailed", rows[ids.Host].GetProperty("lastError").GetString());
        Assert.Equal("Delivery", rows[ids.System].GetProperty("description").GetString());
        Assert.All(rows.Values, row => Assert.False(row.TryGetProperty("payloadJson", out _)));
        foreach (var secret in new[] { "host-body", "action-body", "system-body", "secret" })
            Assert.DoesNotContain(secret, list.GetRawText());
        var firstPage = await host.GetJsonAsync("/api/approval/outbox?page=1&pageSize=1", "alice");
        var secondPage = await host.GetJsonAsync("/api/approval/outbox?page=2&pageSize=1", "alice");
        Assert.Equal(rows.Count, firstPage.GetProperty("total").GetInt32());
        Assert.NotEqual(Assert.Single(firstPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid(),
            Assert.Single(secondPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using (var denied = await Payload(ids.Action)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        permissions.AllowSensitive = true;
        foreach (var (id, secret) in new[] { (ids.Action, "action-body"), (ids.Host, "host-body") })
        {
            using var response = await Payload(id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains(secret, json.RootElement.GetProperty("payloadJson").GetString());
        }
        using (var denied = await Payload(ids.Action, "bob")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var denied = await Payload(ids.Action, "hana")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        foreach (var id in new[] { ids.System, ids.Mismatch, Guid.NewGuid() })
        {
            using var denied = await Payload(id);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        scope.Hidden = instance.Id;
        using (var denied = await Payload(ids.Action)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var scoped = await host.GetJsonAsync("/api/approval/outbox", "alice");
        Assert.Equal(ids.System, Assert.Single(scoped.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    private sealed class StorageContext(DbContextOptions<StorageContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.ConfigureApproval();
    }

    private sealed class Permissions : IApprovalPermissionProvider
    {
        internal bool AllowOutbox = true;
        internal bool AllowSensitive = true;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(request.Permission switch
        {
            ApprovalPermissions.OutboxRead => AllowOutbox,
            ApprovalPermissions.InstanceReadSensitive => AllowSensitive,
            ApprovalPermissions.InstanceReadAll => false,
            _ => true
        });
    }

    private sealed class Scope : IApprovalQueryScope
    {
        internal Guid Hidden;
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.Id != Hidden));
    }

    private sealed class AsyncAction : IApprovalActionHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new("Fixture", "Fixture", "Test", [ActionExecutionMode.Async]);
        public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => [];
        public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class EventSink : IApprovalEventHandler
    {
        public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct) => Task.CompletedTask;
    }
}
