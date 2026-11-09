using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.MultiTenantHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Approval.AspNetCore.Tests;

public sealed class MultiTenantHostTests
{
    [Theory]
    [InlineData("healthy")]
    [InlineData("empty")]
    [InlineData("enumeration")]
    [InlineData("entry")]
    [InlineData("database")]
    public async Task Readiness_AnonymousProbeRequiresHealthyHostScopesAndRestoresContext(string scenario)
    {
        ReadinessScopes probe = null!;
        var commands = new ReadinessCommands();
        await using var host = await Host.Start(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IApprovalHostScopeProvider>(sp =>
                probe = new ReadinessScopes(sp.GetRequiredService<TenantScopes>())));
            services.ConfigureDbContext<TenantDbContext>(o => o.AddInterceptors(commands));
        });
        probe.Scenario = scenario;
        commands.Enabled = true;
        commands.FailBravo = scenario == "database";
        using var response = await host.Client.GetAsync("/health/approval");
        Assert.Equal(scenario == "healthy" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "ready" }, body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(scenario == "healthy", body.GetProperty("ready").GetBoolean());
        var expected = scenario is "empty" or "enumeration" ? Array.Empty<string>()
            : scenario == "entry" ? ["alpha"] : new[] { "alpha", "bravo" };
        Assert.Equal(expected, commands.Partitions.Distinct());
        Assert.Equal(expected.Length, probe.Restored.Count);
        Assert.All(probe.Restored, partition => Assert.Null(partition));
        Assert.Throws<ApprovalException>(() => host.App.Services.GetRequiredService<TenantScopes>().Capture());
    }

    [Fact]
    public async Task ImmediateCompletion_CommitsBusinessAndApprovalOnceWithinOwnTenant()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha", ImmediateFlow); await host.Publish("bravo");
        var bravo = await host.Submit("bravo");
        var alpha = await host.Submit("alpha");
        Assert.Equal(alpha, await host.Submit("alpha"));
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var expense = await db.Expenses.SingleAsync();
                var instance = await db.Set<ApprovalInstance>().SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.InstanceId : bravo.InstanceId, instance.Id);
                Assert.Equal(instance.Id, expense.InstanceId);
                Assert.Equal(tenant == "alpha" ? "Approved" : "Submitted", expense.Status);
                Assert.Equal(tenant == "alpha" ? InstanceStatus.Approved : InstanceStatus.Running, instance.Status);
                Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "sample.expense.submit"));
                Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Submit));
                Assert.Equal(tenant == "alpha" ? 1 : 0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Complete));
                Assert.Equal(tenant == "alpha" ? 0 : 1, await db.Set<ApprovalTask>().CountAsync(t => t.Status == ApprovalTaskStatus.Pending));
                Assert.NotEmpty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            });
    }

    [Fact]
    public async Task ImmediateCompletion_CallbackFailureRollsBackBusinessApprovalAndReservation()
    {
        var failure = new CompletionFailure();
        await using var host = await Host.Start(services =>
            services.Replace(ServiceDescriptor.Scoped<IApprovalBusinessHandler>(sp =>
                new FailingExpenseLifecycle(sp.GetRequiredService<TenantDbContext>(), failure))));
        await host.Publish("alpha", ImmediateFlow);
        using var response = await host.Send("alpha", "alice", HttpMethod.Post, "/demo/expenses", "submit", new { businessKey = "EXP-001", amount = 12 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(failure.UpdatedTrackedExpense);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Empty(await db.Expenses.ToListAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalTask>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "sample.expense.submit"));
        });
        failure.Enabled = false;
        var result = await host.Submit("alpha");
        Assert.Equal(result, await host.Submit("alpha"));
        await host.InTenant("alpha", async sp => Assert.Equal("Approved", (await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync()).Status));
    }

    private const string ImmediateFlow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},"nodes":{
          "start":{"type":"Start","name":"提交","next":"end"},"end":{"type":"End","name":"完成"}}}
        """;

    private sealed class CompletionFailure { public bool Enabled = true; public bool UpdatedTrackedExpense; }
    private sealed class FailingExpenseLifecycle(TenantDbContext db, CompletionFailure failure) : IApprovalBusinessHandler
    {
        public string BusinessType => "Expense";
        public async Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct)
        {
            await new ExpenseLifecycle(db).OnApprovedAsync(instance, ct);
            failure.UpdatedTrackedExpense = db.Expenses.Local.Single().Status == "Approved";
            if (failure.Enabled) throw new InvalidOperationException("Failure after updating the tracked business row");
        }
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => throw new InvalidOperationException("Unexpected rejection");
    }

    private sealed class ReadinessCommands : DbCommandInterceptor
    {
        public bool Enabled, FailBravo;
        public ConcurrentQueue<string> Partitions { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Enabled && data.CommandSource == CommandSource.LinqQuery)
            {
                var partition = ((TenantDbContext)data.Context!).PartitionKey;
                Partitions.Enqueue(partition);
                if (FailBravo && partition == "bravo") throw new IOException("Private database diagnostic");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReadinessScopes(TenantScopes scopes) : IApprovalHostScopeProvider
    {
        public string? Scenario;
        public ConcurrentQueue<string?> Restored { get; } = new();
        public IApprovalHostScope Capture() => scopes.Capture();
        public async Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default)
        {
            if (Scenario == "empty") return [];
            if (Scenario == "enumeration") throw new IOException("Private directory diagnostic");
            var contexts = await scopes.GetBackgroundScopesAsync(ct);
            return Scenario is null ? contexts : contexts.Select(c => (IApprovalHostScope)new ProbeScope(this, c)).ToArray();
        }
        private sealed class ProbeScope(ReadinessScopes owner, IApprovalHostScope context) : IApprovalHostScope
        {
            public string? PartitionKey => context.PartitionKey;
            public IDisposable Enter()
            {
                if (owner.Scenario == "entry" && PartitionKey == "bravo") throw new IOException("Private tenant diagnostic");
                return new Restore(owner, context.Enter());
            }
        }
        private sealed class Restore(ReadinessScopes owner, IDisposable inner) : IDisposable
        {
            public void Dispose()
            {
                inner.Dispose();
                string? previous;
                try { previous = owner.Capture().PartitionKey; }
                catch (ApprovalException) { previous = null; }
                owner.Restored.Enqueue(previous);
            }
        }
    }

    [Fact]
    public async Task SharedDatabase_IndependentKeysReplayAndApprovalOnlyChangesOwnTenant()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var a = await host.Submit("alpha"); var b = await host.Submit("bravo");
        Assert.NotEqual(a.InstanceId, b.InstanceId); Assert.NotEqual(a.ExpenseId, b.ExpenseId);
        Assert.Equal("alpha", a.TenantId); Assert.Equal("bravo", b.TenantId);
        Assert.Equal(a, await host.Submit("alpha")); Assert.Equal(b, await host.Submit("bravo"));
        using var conflict = await host.Send("alpha", "alice", HttpMethod.Post, "/demo/expenses", "submit", new { businessKey = "EXP-001", amount = 99 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var aTask = await host.Task("alpha"); var bTask = await host.Task("bravo");
        using var approve = await host.Send("alpha", "bob", HttpMethod.Post, $"/api/approval/tasks/{aTask}/approve", "approve", new { comment = "同意" });
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        using var replay = await host.Send("alpha", "bob", HttpMethod.Post, $"/api/approval/tasks/{aTask}/approve", "approve", new { comment = "同意" });
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(bTask, await host.Task("bravo"));
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var instance = await db.Set<ApprovalInstance>().SingleAsync();
                Assert.Equal(tenant == "alpha" ? a.InstanceId : b.InstanceId, instance.Id);
                Assert.Equal(tenant == "alpha" ? InstanceStatus.Approved : InstanceStatus.Running, instance.Status);
                var expense = await db.Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? "Approved" : "Submitted", expense.Status);
                Assert.Equal(tenant == "alpha" ? a.ExpenseId : b.ExpenseId, expense.Id);
                Assert.Equal(instance.Id, expense.InstanceId);
                Assert.Equal("EXP-001", expense.BusinessKey); Assert.Equal("alice", expense.ApplicantId); Assert.Equal(12m, expense.Amount);
                Assert.Equal(1, await db.Set<ApprovalDefinition>().CountAsync());
                Assert.Equal(tenant == "alpha" ? 1 : 0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Approve));
                Assert.False(db.Database.HasPendingModelChanges());
                Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.Name.StartsWith("Approval.Forms.", StringComparison.Ordinal));
            });
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public async Task Lifecycle_RejectionAndWithdrawalOnlyUpdateOwnTenant(string decision)
    {
        await using var host = await Host.Start(); await host.Publish("alpha"); await host.Publish("bravo");
        var a = await host.Submit("alpha"); await host.Submit("bravo");
        var path = decision == "reject" ? $"/api/approval/tasks/{await host.Task("alpha")}/reject" : $"/api/approval/instances/{a.InstanceId}/withdraw";
        using var response = await host.Send("alpha", decision == "reject" ? "bob" : "alice", HttpMethod.Post, path, "decision", new { comment = "结束申请" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(decision == "reject" ? "Rejected" : "Withdrawn", (await db.Expenses.SingleAsync()).Status);
            Assert.Equal(decision == "reject" ? InstanceStatus.Rejected : InstanceStatus.Withdrawn, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
        });
        await host.InTenant("bravo", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal("Submitted", (await db.Expenses.SingleAsync()).Status);
            Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
        });
    }

    [Fact]
    public async Task DirectoryScopes_KeepUserPermissionsAndRolesInsideTenant()
    {
        var directory = new TenantDirectory(Host.Configuration());
        directory.Require("bravo").Users.Single(u => u.Id == "alice").Permissions = [];
        directory.Require("bravo").Users.Single(u => u.Id == "bob").Roles = ["OtherReviewer"];
        var scopes = new TenantScopes(directory);
        var request = new ApprovalPermissionRequest(new("alice", false), ApprovalPermissions.InstanceStart, new());
        using (scopes.ForTenant("alpha").Enter())
        {
            Assert.True(await new TenantPermissions(scopes, directory).AuthorizeAsync(request, default));
            Assert.Equal(new[] { "bob" }, await new TenantUsers(scopes, directory).GetUserIdsInRoleAsync("Reviewer", default));
        }
        using (scopes.ForTenant("bravo").Enter())
        {
            Assert.False(await new TenantPermissions(scopes, directory).AuthorizeAsync(request, default));
            Assert.Empty(await new TenantUsers(scopes, directory).GetUserIdsInRoleAsync("Reviewer", default));
        }
        Assert.Throws<ApprovalException>(() => scopes.Capture());
    }

    [Fact]
    public async Task HttpBoundary_RejectsCrossTenantResourcesAndMissingPermissions()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var a = await host.Submit("alpha"); var b = await host.Submit("bravo");
        var task = await host.Task("alpha");
        using var detail = await host.Send("bravo", "admin", HttpMethod.Get, $"/api/approval/instances/{a.InstanceId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using var crossApprove = await host.Send("bravo", "bob", HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "cross", new { comment = "越权" });
        Assert.Equal(HttpStatusCode.NotFound, crossApprove.StatusCode);
        using var denied = await host.Send("alpha", "alice", HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "denied", new { comment = "自批" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var spoof = await host.Send("alpha", "alice", HttpMethod.Get, "/demo/expenses", tenantHeader: "bravo");
        Assert.Equal(HttpStatusCode.OK, spoof.StatusCode);
        var expense = Assert.Single((await spoof.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(a.InstanceId, expense.GetProperty("instanceId").GetGuid());
        Assert.NotEqual(b.InstanceId, expense.GetProperty("instanceId").GetGuid());
        Assert.Equal(task, await host.Task("alpha"));
        await host.InTenant("alpha", async sp => Assert.Equal("Submitted", (await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync()).Status));
        using var anonymous = await host.Client.GetAsync("/demo/expenses");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Theory]
    [InlineData("missing-tenant")]
    [InlineData("ambiguous-tenant")]
    [InlineData("unknown-tenant")]
    [InlineData("nonmember")]
    [InlineData("inactive-tenant")]
    [InlineData("inactive-user")]
    [InlineData("missing-subject")]
    [InlineData("ambiguous-subject")]
    public void TrustedClaims_RejectInvalidTenantIdentity(string scenario)
    {
        var directory = new TenantDirectory(Host.Configuration());
        if (scenario == "inactive-tenant") directory.Require("alpha").IsActive = false;
        if (scenario == "inactive-user") directory.Require("alpha").Users.Single(u => u.Id == "alice").IsActive = false;
        var claims = new List<Claim> { new("sub", scenario == "nonmember" ? "outsider" : "alice") };
        if (scenario == "missing-subject") claims.Clear();
        if (scenario == "ambiguous-subject") claims.Add(new("sub", "bob"));
        if (scenario != "missing-tenant") claims.Add(new("tenant_id", scenario == "unknown-tenant" ? "unknown" : "alpha"));
        if (scenario == "ambiguous-tenant") claims.Add(new("tenant_id", "bravo"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "verified"));
        var error = Assert.Throws<ApprovalException>(() => new TenantScopes(directory).FromPrincipal(principal));
        Assert.Equal(ApprovalErrorCode.Forbidden, error.Code);
    }

    [Fact]
    public void Production_RejectsDemoAndInsecureJwtConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => TenantApplication.Build(["--environment", "Production"], b =>
        { b.Configuration.Sources.Clear(); b.Configuration.AddConfiguration(Host.Configuration()); }));
        Assert.Throws<InvalidOperationException>(() => TenantApplication.Build(["--environment", "Production"], b =>
        {
            b.Configuration.Sources.Clear(); b.Configuration.AddConfiguration(Host.Configuration());
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Mode"] = "Jwt", ["Authentication:Authority"] = "http://issuer.invalid", ["Authentication:Audience"] = "approval" });
        }));
    }

    [Fact]
    public async Task Persistence_RejectsScopeSwitchFilterRemovalForgedTenantAndCrossTenantForeignKey()
    {
        await using var host = await Host.Start(); await host.Publish("alpha");
        var a = await host.Submit("alpha");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        Assert.Equal(ApprovalErrorCode.Forbidden, Assert.Throws<ApprovalException>(() => scopes.Capture()).Code);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(a.InstanceId, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ApprovalInstance>().Select(i => new { i.Id }).IgnoreQueryFilters().ToListAsync());
            var compiled = EF.CompileAsyncQuery((TenantDbContext context) => context.Set<ApprovalInstance>().Count());
            using (scopes.ForTenant("bravo").Enter())
            {
                var mismatch = await Assert.ThrowsAsync<ApprovalException>(() => compiled(db));
                Assert.Equal(ApprovalErrorCode.Forbidden, mismatch.Code);
            }
            var definition = new ApprovalDefinition { Id = Guid.NewGuid(), Key = "illegal", Name = "illegal", Version = 1, DefinitionJson = Host.Flow };
            db.Add(definition);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
        await host.InTenant("bravo", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            var expense = new Expense { Id = Guid.NewGuid(), BusinessKey = "forged", ApplicantId = "alice", Amount = 10 };
            db.Expenses.Add(expense); db.Entry(expense).Property(TenantDbContext.TenantProperty).CurrentValue = "alpha";
            Assert.Equal(ApprovalErrorCode.Forbidden, (await Assert.ThrowsAsync<ApprovalException>(() => db.SaveChangesAsync())).Code);
        });
        await host.InTenant("bravo", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            db.Receipts.Add(new() { Id = Guid.NewGuid(), InstanceId = a.InstanceId, EventType = "forged", ReceivedAt = DateTime.UtcNow });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(787, Assert.IsType<SqliteException>(failure.InnerException).SqliteExtendedErrorCode);
        });
        await host.InTenant("bravo", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(0, await db.Expenses.CountAsync()); Assert.Equal(0, await db.Receipts.CountAsync());
        });
    }

    [Fact]
    public async Task FailedSubmission_RollsBackBusinessDataAndOperationReservation()
    {
        await using var host = await Host.Start();
        using var response = await host.Send("alpha", "alice", HttpMethod.Post, "/demo/expenses", "missing-definition", new { businessKey = "EXP-001", amount = 12 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(0, await db.Expenses.CountAsync()); Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync()); Assert.Equal(0, await db.Set<ApprovalOutboxMessage>().CountAsync());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedExpense_CrossTenantWriteRejectsWithoutPartialWrites(bool delete)
    {
        await using var host = await Host.Start(); await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            var own = await db.Expenses.SingleAsync();
            own.Status = "Changed";
            var foreign = new Expense { Id = bravo.ExpenseId };
            db.Attach(foreign);
            if (delete) db.Remove(foreign); else foreign.Status = "Approved";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        });
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var expense = await db.Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, expense.Id);
                Assert.Equal("Submitted", expense.Status);
                Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedExpense_OwnTenantWriteSucceeds(bool delete)
    {
        await using var host = await Host.Start(); await host.Publish("alpha");
        var submitted = await host.Submit("alpha");
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            var expense = new Expense { Id = submitted.ExpenseId };
            db.Attach(expense);
            if (delete) db.Remove(expense); else expense.Status = "Updated";
            Assert.Equal(1, await db.SaveChangesAsync());
        });
        await host.InTenant("alpha", async sp =>
        {
            var expenses = await sp.GetRequiredService<TenantDbContext>().Expenses.ToListAsync();
            if (delete) Assert.Empty(expenses);
            else { Assert.Equal(submitted.ExpenseId, Assert.Single(expenses).Id); Assert.Equal("Updated", expenses[0].Status); }
        });
    }

    [Theory]
    [InlineData("composed")]
    [InlineData("named")]
    [InlineData("compiled")]
    public async Task QueryFilters_RejectRemovalAcrossWholePartitionedQuery(string shape)
    {
        await using var host = await Host.Start(); await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); await host.Submit("bravo");
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (shape == "compiled")
                {
                    var query = EF.CompileAsyncQuery((TenantDbContext c) => c.Database.SqlQueryRaw<FilterProbe>("SELECT 1 AS Value")
                        .IgnoreQueryFilters().SelectMany(_ => c.Set<ApprovalInstance>()).Count());
                    await query(db);
                }
                else if (shape == "named")
                    await db.Set<ApprovalInstance>().IgnoreQueryFilters(["TenantId"]).CountAsync();
                else
                    await db.Database.SqlQueryRaw<FilterProbe>("SELECT 1 AS Value").IgnoreQueryFilters()
                        .SelectMany(_ => db.Set<ApprovalInstance>()).CountAsync();
            });
            Assert.Contains("审批分区 DbContext 禁止移除查询过滤器", error.Message);
            Assert.Equal(alpha.InstanceId, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Database.SqlQueryRaw<FilterProbe>("SELECT 1 AS Value").IgnoreQueryFilters().CountAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<FilterProbe>("SELECT 1 AS Value").CountAsync());
        });
    }

    public sealed class FilterProbe { public int Value { get; set; } }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("empty-key")]
    [InlineData("zero-amount")]
    [InlineData("negative-amount")]
    [InlineData("empty-business-key")]
    public async Task InvalidSubmission_RejectsWithoutPartialWrites(string invalid)
    {
        await using var host = await Host.Start();
        var key = invalid == "missing-key" ? null : invalid == "empty-key" ? "" : "invalid";
        var amount = invalid == "zero-amount" ? 0 : invalid == "negative-amount" ? -1 : 12;
        using var response = await host.Send("alpha", "alice", HttpMethod.Post, "/demo/expenses", key,
            new { businessKey = invalid == "empty-business-key" ? "" : "EXP-001", amount });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (invalid is "missing-key" or "empty-key")
            Assert.Equal("OperationIdRequired", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(0, await db.Expenses.CountAsync()); Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync());
        });
    }

    [Fact]
    public async Task Outbox_FailedTenantDoesNotBlockOtherTenantAndReceiptsDoNotDuplicate()
    {
        var failAlpha = false;
        await using var host = await Host.Start(services =>
            services.Replace(ServiceDescriptor.Scoped<IApprovalEventHandler>(sp =>
                failAlpha && sp.GetRequiredService<TenantScopes>().Current == "alpha"
                    ? throw new IOException("Alpha event handler unavailable") : ActivatorUtilities.CreateInstance<TenantEvents>(sp))));
        await host.Publish("alpha"); await host.Publish("bravo");
        var a = await host.Submit("alpha"); var b = await host.Submit("bravo");
        var dispatcher = host.App.Services.GetRequiredService<ApprovalOutboxDispatcher>();
        failAlpha = true; await dispatcher.DispatchOnceAsync();
        Assert.False(Assert.Single(host.App.Services.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(0, await db.Receipts.CountAsync());
            Assert.All(await db.Set<ApprovalOutboxMessage>().ToListAsync(), m => { Assert.Equal(OutboxStatus.Pending, m.Status); Assert.Equal(0, m.Attempts); });
        });
        await host.InTenant("bravo", async sp =>
        {
            var receipts = await sp.GetRequiredService<TenantDbContext>().Receipts.ToListAsync();
            Assert.NotEmpty(receipts); Assert.All(receipts, r => Assert.Equal(b.InstanceId, r.InstanceId));
        });
        failAlpha = false; await dispatcher.DispatchOnceAsync(); await dispatcher.DispatchOnceAsync();
        Assert.True(Assert.Single(host.App.Services.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var messages = await db.Set<ApprovalOutboxMessage>().OrderBy(m => m.Id).ToListAsync();
                var receipts = await db.Receipts.OrderBy(r => r.Id).ToListAsync();
                Assert.NotEmpty(messages); Assert.All(messages, m => { Assert.Equal(OutboxStatus.Succeeded, m.Status); Assert.Equal(1, m.Attempts); });
                Assert.Equal(messages.Select(m => m.Id), receipts.Select(r => r.Id));
                Assert.All(receipts, r => Assert.Equal(tenant == "alpha" ? a.InstanceId : b.InstanceId, r.InstanceId));
                var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                var duplicate = JsonSerializer.Deserialize<ApprovalEvent>(messages[0].PayloadJson, json)!;
                await sp.GetRequiredService<IApprovalEventHandler>().HandleAsync(duplicate, CancellationToken.None);
                Assert.False(db.ChangeTracker.HasChanges());
                Assert.Equal(messages.Count, await db.Receipts.CountAsync());
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCommands_PreserveTenantAndConvergeForSameOperation(bool sameTenant)
    {
        var barrier = new LookupBarrier();
        await using var host = await Host.Start(services =>
            services.ConfigureDbContext<TenantDbContext>(o => o.AddInterceptors(barrier)));
        await host.Publish("alpha"); await host.Publish("bravo");
        barrier.Enabled = true;
        var results = await Task.WhenAll(host.Submit("alpha"), host.Submit(sameTenant ? "alpha" : "bravo"));
        barrier.Enabled = false;
        Assert.Equal(new[] { "alpha", sameTenant ? "alpha" : "bravo" }.Order(), barrier.Partitions.Order());
        if (sameTenant) Assert.Equal(results[0], results[1]); else Assert.NotEqual(results[0].InstanceId, results[1].InstanceId);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                Assert.Equal(tenant == "bravo" && sameTenant ? 0 : 1, await db.Expenses.CountAsync());
                Assert.Equal(tenant == "bravo" && sameTenant ? 0 : 1, await db.Set<ApprovalInstance>().CountAsync());
            });
    }

    private sealed class LookupBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public bool Enabled { get; set; }
        public ConcurrentQueue<string> Partitions { get; } = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Enabled && data.CommandSource == CommandSource.LinqQuery && command.CommandText.Contains("\"ApprovalOperations\"", StringComparison.Ordinal))
            {
                var arrival = Interlocked.Increment(ref _arrivals);
                if (arrival <= 2)
                {
                    Partitions.Enqueue(((TenantDbContext)data.Context!).PartitionKey);
                    if (arrival == 2) _release.SetResult();
                    await _release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                }
            }
            return result;
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _database;
        public WebApplication App { get; }
        public HttpClient Client { get; private set; } = null!;
        public static string Flow => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MultiTenantExpenseFlow.json"));
        private Host(WebApplication app, string database) { App = app; _database = database; }
        public static IConfigurationRoot Configuration() => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "MultiTenantConfiguration.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "MultiTenantDevelopmentConfiguration.json")).Build();
        public static async Task<Host> Start(Action<IServiceCollection>? configureServices = null)
        {
            var database = Path.Combine(Path.GetTempPath(), "approval-multitenant-" + Guid.NewGuid().ToString("N") + ".db");
            var app = TenantApplication.Build(["--environment", "Development"], b =>
            {
                b.WebHost.UseTestServer(); b.Logging.ClearProviders(); b.Configuration.Sources.Clear(); b.Configuration.AddConfiguration(Configuration());
                b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Approval"] = "Data Source=" + database + ";Pooling=False", ["Approval:EnableBackgroundServices"] = "false" });
            }, configureServices);
            var host = new Host(app, database);
            try { await TenantDatabase.MigrateAsync(app.Services); await app.StartAsync(); host.Client = app.GetTestClient(); return host; }
            catch { await host.DisposeAsync(); throw; }
        }
        public async Task InTenant(string tenant, Func<IServiceProvider, Task> operation)
        {
            using var context = App.Services.GetRequiredService<TenantScopes>().ForTenant(tenant).Enter();
            await using var scope = App.Services.CreateAsyncScope(); await operation(scope.ServiceProvider);
        }
        public async Task<HttpResponseMessage> Send(string tenant, string user, HttpMethod method, string path, string? key = null, object? body = null,
            string? raw = null, string? tenantHeader = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", $"multitenant-demo-{tenant}-{user}-0000000000000000");
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (tenantHeader is not null) request.Headers.Add("X-Tenant-Id", tenantHeader);
            if (raw is not null) request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
            else if (body is not null) request.Content = JsonContent.Create(body);
            return await Client.SendAsync(request);
        }
        public async Task Publish(string tenant, string? flow = null)
        {
            using var result = await Send(tenant, "admin", HttpMethod.Post, "/api/approval/definitions/expense?name=Expense", "publish", raw: flow ?? Flow);
            Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        }
        public async Task<TenantApplication.ExpenseResult> Submit(string tenant)
        {
            using var result = await Send(tenant, "alice", HttpMethod.Post, "/demo/expenses", "submit", new { businessKey = "EXP-001", amount = 12 });
            Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
            return (await result.Content.ReadFromJsonAsync<TenantApplication.ExpenseResult>())!;
        }
        public async Task<Guid> Task(string tenant)
        {
            using var result = await Send(tenant, "bob", HttpMethod.Get, "/api/approval/todo");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            return Assert.Single((await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()).GetProperty("taskId").GetGuid();
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose(); await App.DisposeAsync();
            foreach (var path in new[] { _database, _database + "-wal", _database + "-shm" }) if (File.Exists(path)) File.Delete(path);
        }
    }
}
