using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class P4SafetyEndpointTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
        """;
    private sealed class Permissions : IApprovalPermissionProvider
    {
        public Func<ApprovalPermissionRequest, bool> Rule = _ => true;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(Rule(request));
    }
    private sealed class Scope : IApprovalQueryScope
    {
        public Guid Hidden;
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.Id != Hidden));
    }
    private sealed class Business : IApprovalBusinessHandler
    {
        public string BusinessType => DocHandler.Type;
        public int Calls;
        public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) { Calls++; return Task.CompletedTask; }
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) { Calls++; return Task.CompletedTask; }
    }
    private sealed record Fixture(ApiHost Host, Permissions Permissions, Scope Scope, Business Business, Guid InstanceId, Guid TaskId, Guid OutboxId);
    private static async Task<Fixture> Seed(CommitFault? fault = null)
    {
        var permissions = new Permissions(); var scope = new Scope(); var business = new Business();
        var host = await ApiHost.StartAsync(build: b =>
        {
            b.Services.AddSingleton<IApprovalPermissionProvider>(permissions);
            b.Services.AddSingleton<IApprovalQueryScope>(scope);
            b.Services.AddSingleton<IApprovalBusinessHandler>(business);
            if (fault is not null) b.Services.ConfigureDbContext<ApiDbContext>(o => o.AddInterceptors(fault, new VerificationFault(fault)));
        });
        try
        {
            await host.PublishAsync("p4", Flow);
            var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("p4", DocHandler.Type, "one", "alice")));
            var outbox = await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalOutboxMessage>().Select(m => m.Id).FirstAsync());
            return new(host, permissions, scope, business, instance.Id, Assert.Single(instance.Tasks).Id, outbox);
        }
        catch { await host.DisposeAsync(); throw; }
    }
    private static (string Url, object? Body) Request(Fixture f, string command, string reason = "handover") => command switch
    {
        "retry" => ($"/api/approval/outbox/{f.OutboxId}/retry", null),
        "reassign" => ($"/api/approval/admin/tasks/{f.TaskId}/reassign", new { toUserId = "hana", reason }),
        "bulk" => ("/api/approval/admin/users/bob/reassign", new { toUserId = "hana", reason }),
        _ => ($"/api/approval/admin/instances/{f.InstanceId}/terminate", new { reason })
    };
    private static async Task<HttpResponseMessage> Send(Fixture f, string command, string? key = "p4-stable", string reason = "handover", string user = "admin", bool multiple = false)
    {
        var (url, body) = Request(f, command, reason);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add(HeaderCurrentUser.Header, user);
        if (key is not null) Assert.True(request.Headers.TryAddWithoutValidation("Idempotency-Key", multiple ? new[] { key, "second-value" } : new[] { key }));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Host.Client.SendAsync(request);
    }
    private static Task<string> Snapshot(ApiHost host) => host.RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<ApiDbContext>();
        return JsonSerializer.Serialize(new
        {
            Instances = await db.Set<ApprovalInstance>().AsNoTracking().OrderBy(i => i.Id).ToListAsync(),
            Tasks = await db.Set<ApprovalTask>().AsNoTracking().OrderBy(t => t.Id).ToListAsync(),
            Logs = await db.Set<ApprovalLog>().AsNoTracking().OrderBy(l => l.Id).ToListAsync(),
            Outbox = await db.Set<ApprovalOutboxMessage>().AsNoTracking().OrderBy(m => m.Id).ToListAsync(),
            Slots = await db.Set<ApprovalBusinessSlot>().AsNoTracking().OrderBy(s => s.Id).ToListAsync(),
            Operations = await db.Set<ApprovalOperation>().AsNoTracking().OrderBy(o => o.Id).ToListAsync()
        });
    });

    [Theory]
    [InlineData("diagnostics-only")]
    [InlineData("outbox-read-only")]
    [InlineData("reassign-denied")]
    [InlineData("bulk-denied")]
    [InlineData("terminate-denied")]
    public async Task ManagementHttp_IndependentPermissionsRejectWithoutAnyWrites(string permissionCase)
    {
        var f = await Seed(); await using var host = f.Host;
        var before = await Snapshot(host);
        f.Permissions.Rule = r => permissionCase switch
        {
            "diagnostics-only" => r.Permission == ApprovalPermissions.DiagnosticsRead,
            "outbox-read-only" => r.Permission == ApprovalPermissions.OutboxRead,
            "reassign-denied" => r.Permission != ApprovalPermissions.TaskReassign,
            "bulk-denied" => r.Permission != ApprovalPermissions.TaskReassignBulk,
            _ => r.Permission != ApprovalPermissions.InstanceTerminate
        };
        if (permissionCase == "diagnostics-only") await host.GetJsonAsync("/api/approval/diagnostics", "admin");
        if (permissionCase == "outbox-read-only") Assert.NotEmpty((await host.GetJsonAsync("/api/approval/outbox", "admin")).GetProperty("items").EnumerateArray());
        var commands = permissionCase switch
        {
            "diagnostics-only" => new[] { "retry", "reassign", "bulk", "terminate" },
            "outbox-read-only" => new[] { "retry" }, "reassign-denied" => new[] { "reassign" },
            "bulk-denied" => new[] { "bulk" }, _ => new[] { "terminate" }
        };
        foreach (var command in commands)
        {
            using var response = await Send(f, command);
            Assert.Equal("Forbidden", (await response.ProblemAsync(403)).GetProperty("code").GetString());
            Assert.Equal(before, await Snapshot(host)); Assert.Equal(0, f.Business.Calls);
        }
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("reassign")]
    [InlineData("terminate")]
    public async Task ManagementHttp_QueryScopeExcludesRealResourceWithoutWrites(string command)
    {
        var f = await Seed(); await using var host = f.Host; var before = await Snapshot(host);
        f.Scope.Hidden = f.InstanceId;
        using var response = await Send(f, command);
        Assert.Equal("NotFound", (await response.ProblemAsync(404)).GetProperty("code").GetString());
        Assert.Equal(before, await Snapshot(host)); Assert.Equal(0, f.Business.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("too-long")]
    [InlineData("control")]
    [InlineData("multiple")]
    public async Task ManagementHttp_InvalidKeysRejectOrdinaryAndBulkWithoutWrites(string caseId)
    {
        var f = await Seed(); await using var host = f.Host; var before = await Snapshot(host);
        var key = caseId switch { "missing" => null, "empty" => "", "whitespace" => " ", "too-long" => new string('k', 201), "control" => "key\u0001", _ => "key" };
        foreach (var command in new[] { "retry", "reassign", "bulk", "terminate" })
        {
            using var response = await Send(f, command, key, multiple: caseId == "multiple");
            Assert.Equal("OperationIdRequired", (await response.ProblemAsync(400)).GetProperty("code").GetString());
            Assert.Equal(before, await Snapshot(host)); Assert.Equal(0, f.Business.Calls);
        }
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("reassign")]
    [InlineData("bulk")]
    [InlineData("terminate")]
    public async Task ManagementHttp_StableReplayAndRevocationPreserveCommittedResult(string command)
    {
        var f = await Seed(); await using var host = f.Host;
        using var first = await Send(f, command); Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var body = await first.Content.ReadAsStringAsync(); var committed = await Snapshot(host);
        using var replay = await Send(f, command); Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync()); Assert.Equal(committed, await Snapshot(host));
        if (command != "retry")
        {
            using var changed = await Send(f, command, reason: "different intent");
            Assert.Equal("Conflict", (await changed.ProblemAsync(409)).GetProperty("code").GetString());
            Assert.Equal(committed, await Snapshot(host));
        }
        f.Scope.Hidden = f.InstanceId;
        using var revoked = await Send(f, command);
        Assert.Equal("NotFound", (await revoked.ProblemAsync(404)).GetProperty("code").GetString());
        Assert.Equal(committed, await Snapshot(host)); Assert.Equal(command == "terminate" ? 1 : 0, f.Business.Calls);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("reassign")]
    [InlineData("bulk")]
    [InlineData("terminate")]
    public async Task ManagementHttp_UnknownCommitRecoversOriginalKeyWithoutDuplicateEffects(string command)
    {
        var fault = new CommitFault(); var f = await Seed(fault); await using var host = f.Host;
        Guid? remaining = null;
        if (command == "bulk")
        {
            remaining = (await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("p4", DocHandler.Type, "second", "alice")))).Id;
            fault.SkipCommits = 1; // Let the fixed plan commit, then lose the first instance's commit acknowledgement.
        }
        fault.Enabled = true;
        using var unknown = await Send(f, command);
        Assert.Equal("CommitOutcomeUnknown", (await unknown.ProblemAsync(503)).GetProperty("code").GetString());
        fault.BlockVerification = false;
        if (remaining is { } remainingId)
            await host.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<ApiDbContext>();
                Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.item"));
                Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "task.reassign-bulk.result"));
                Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Reassign));
                Assert.Equal("bob", (await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == remainingId)).AssigneeValue);
                return true;
            });
        using var recovered = await Send(f, command); Assert.True(recovered.IsSuccessStatusCode, await recovered.Content.ReadAsStringAsync());
        var committed = await Snapshot(host);
        using var replay = await Send(f, command); Assert.Equal(recovered.StatusCode, replay.StatusCode);
        Assert.Equal(committed, await Snapshot(host)); Assert.Equal(command == "terminate" ? 1 : 0, f.Business.Calls);
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApiDbContext>();
            Assert.Equal(command == "bulk" ? 5 : 2, await db.Set<ApprovalOperation>().CountAsync()); // includes HTTP publication
            Assert.Equal(command == "bulk" ? 2 : command == "reassign" ? 1 : 0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Reassign));
            Assert.Equal(command == "terminate" ? InstanceStatus.Terminated : InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == f.InstanceId)).Status);
            return true;
        });
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Enabled, BlockVerification;
        public int SkipCommits;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (!Enabled) return Task.CompletedTask;
            if (SkipCommits-- > 0) return Task.CompletedTask;
            Enabled = false; BlockVerification = true;
            throw new IOException("commit response lost");
        }
    }
    private sealed class VerificationFault(CommitFault fault) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (fault.BlockVerification && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal))
                throw new IOException("verification unavailable");
            return ValueTask.FromResult(result);
        }
    }
}
