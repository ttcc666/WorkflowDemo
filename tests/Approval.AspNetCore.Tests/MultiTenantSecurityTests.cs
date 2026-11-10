using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Domain;
using Approval.MultiTenantHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Approval.AspNetCore.Tests;

public sealed partial class MultiTenantHostTests
{
    [Theory]
    [InlineData("foreign-context")]
    [InlineData("ambient-switch")]
    public async Task BusinessHandler_WrongTenantRejectsAndRollsBackApproval(string attack)
    {
        var probe = new BusinessTenantProbe();
        await using var host = await Host.Start(services => services.Replace(ServiceDescriptor.Scoped<IApprovalBusinessHandler>(sp =>
            new WrongTenantBusinessHandler(sp.GetRequiredService<TenantDbContext>(), sp.GetRequiredService<TenantScopes>(), probe, attack))));
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        await using var foreignScope = host.App.Services.CreateAsyncScope();
        using (scopes.ForTenant("bravo").Enter())
        {
            probe.Foreign = foreignScope.ServiceProvider.GetRequiredService<TenantDbContext>();
            Assert.Equal(bravo.ExpenseId, (await probe.Foreign.Expenses.SingleAsync()).Id);
        }
        using var response = await host.Send("alpha", "bob", HttpMethod.Post, $"/api/approval/tasks/{await host.Task("alpha")}/approve", "wrong-handler", new { comment = "approve" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { "alpha" }, probe.Partitions);
        Assert.Equal(ApprovalErrorCode.Forbidden, probe.Rejected?.Code);
        Assert.Throws<ApprovalException>(() => scopes.Current);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var row = await db.Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, row.Id);
                Assert.Equal("Submitted", row.Status);
                Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
                Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync(t => t.Status == ApprovalTaskStatus.Pending));
                Assert.Equal(0, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Approve || l.Action == ApprovalAction.Complete));
                Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync(o => o.RequestKey == "wrong-handler"));
            });
    }

    private sealed class BusinessTenantProbe
    {
        public TenantDbContext Foreign = null!;
        public List<string> Partitions { get; } = [];
        public ApprovalException? Rejected;
    }

    private sealed class WrongTenantBusinessHandler(TenantDbContext db, TenantScopes scopes, BusinessTenantProbe probe, string attack) : IApprovalBusinessHandler
    {
        public string BusinessType => "Expense";
        public async Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct)
        {
            probe.Partitions.Add(scopes.Current);
            await new ExpenseLifecycle(db).OnApprovedAsync(instance, ct);
            try
            {
                if (attack == "foreign-context") await probe.Foreign.Expenses.SingleAsync(ct);
                else
                {
                    using var wrong = scopes.ForTenant("bravo").Enter();
                    await db.Expenses.SingleAsync(ct);
                }
            }
            catch (ApprovalException ex) { probe.Rejected = ex; throw; }
        }
        public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => throw new InvalidOperationException("Unexpected rejection");
    }

    [Fact]
    public async Task WorkflowRetry_KeepsOriginalTenantAndUsesFreshContext()
    {
        var fault = new TenantSubmissionFault();
        await using var host = await Host.Start(services => services.ConfigureDbContext<TenantDbContext>(options =>
            options.ReplaceService<IExecutionStrategyFactory, TenantRetryStrategyFactory>().AddInterceptors(fault)));
        await host.Publish("alpha"); await host.Publish("bravo");
        var bravo = await host.Submit("bravo");
        fault.Enabled = true;
        var alpha = await host.Submit("alpha");
        Assert.Equal(alpha, await host.Submit("alpha"));
        Assert.Equal(new[] { "alpha", "alpha" }, fault.Attempts.Select(a => a.Partition));
        Assert.Equal(2, fault.Attempts.Select(a => a.Context).Distinct().Count());
        Assert.Throws<ApprovalException>(() => host.App.Services.GetRequiredService<TenantScopes>().Current);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, (await db.Expenses.SingleAsync()).Id);
                Assert.Equal(tenant == "alpha" ? alpha.InstanceId : bravo.InstanceId, (await db.Set<ApprovalInstance>().SingleAsync()).Id);
                Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "sample.expense.submit"));
                Assert.Equal(1, await db.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Submit));
                Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync(t => t.Status == ApprovalTaskStatus.Pending));
                Assert.NotEmpty(await db.Set<ApprovalOutboxMessage>().ToArrayAsync());
            });
    }

    private sealed class TenantTransientException : Exception { }
    private sealed class TenantRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryStrategy(dependencies);
        private sealed class RetryStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
        { protected override bool ShouldRetryOn(Exception exception) => exception is TenantTransientException; }
    }
    private sealed class TenantSubmissionFault : SaveChangesInterceptor
    {
        public bool Enabled;
        public List<(string Partition, Guid Context)> Attempts { get; } = [];
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            // One command attempt can flush several times; inject/observe once per Context.
            if (Enabled && data.Context is TenantDbContext db && db.Expenses.Local.Any()
                && Attempts.All(a => a.Context != db.ContextId.InstanceId))
            {
                Attempts.Add((db.PartitionKey, db.ContextId.InstanceId));
                if (Attempts.Count == 1) throw new TenantTransientException();
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task SqlAction_TenantRequestRejectsBeforeOpeningConnectionAndRollsBack()
    {
        var opened = 0;
        await using var host = await Host.Start(services => new ApprovalBuilder(services).AddSqlActionQuery(
            new("host", "expenses", "SELECT BusinessKey FROM HostExpenses", new Dictionary<string, System.Data.DbType>()),
            sp => { opened++; return new Microsoft.Data.Sqlite.SqliteConnection(sp.GetRequiredService<TenantDbContext>().Database.GetConnectionString()); }));
        const string flow = """
            {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},"nodes":{
              "start":{"type":"Start","next":"query"},
              "query":{"type":"Action","next":"end","actions":[{"id":"read","type":"Sql","outputKey":"rows",
                "config":{"dataSource":"host","queryId":"expenses","parameters":{}}}]},"end":{"type":"End"}}}
            """;
        await host.Publish("bravo"); var bravo = await host.Submit("bravo");
        await host.Publish("alpha", flow);
        using var response = await host.Send("alpha", "alice", HttpMethod.Post, "/demo/expenses", "sql-attempt", new { businessKey = "EXP-001", amount = 12 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidOperation", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(0, opened);
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Empty(await db.Expenses.ToListAsync());
            Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalOutboxMessage>().ToListAsync());
            Assert.Empty(await db.Set<ApprovalLog>().ToListAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync(o => o.RequestKey == "sql-attempt"));
        });
        await host.InTenant("bravo", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            Assert.Equal(bravo.ExpenseId, (await db.Expenses.SingleAsync()).Id);
            Assert.Equal(InstanceStatus.Running, (await db.Set<ApprovalInstance>().SingleAsync()).Status);
            Assert.Equal(1, await db.Set<ApprovalTask>().CountAsync(t => t.Status == ApprovalTaskStatus.Pending));
        });
    }

    [Theory]
    [InlineData("alpha", "ALPHA")]
    [InlineData("Alpha", "alpha")]
    [InlineData("alpha", "alpha")]
    public void TenantDirectory_RejectsCaseEquivalentIds(string first, string second)
    {
        var configuration = Host.Configuration();
        configuration["Directory:Tenants:0:id"] = first;
        configuration["Directory:Tenants:1:id"] = second;
        Assert.Throws<InvalidOperationException>(() => new TenantDirectory(configuration));
        Assert.Equal(first, configuration["Directory:Tenants:0:id"]);
        Assert.Equal(second, configuration["Directory:Tenants:1:id"]);
    }

    [Theory]
    [InlineData("leading-space")]
    [InlineData("trailing-space")]
    [InlineData("unicode-space")]
    [InlineData("tab")]
    [InlineData("line-break")]
    [InlineData("control")]
    [InlineData("empty")]
    [InlineData("too-long")]
    public void TenantDirectory_RejectsInvalidIdsWithoutNormalizing(string kind)
    {
        var id = kind switch
        {
            "leading-space" => " alpha", "trailing-space" => "alpha ", "unicode-space" => "alpha\u00a0",
            "tab" => "al\tpha", "line-break" => "al\npha", "control" => "al\0pha", "empty" => "",
            "too-long" => new string('a', 65), _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var configuration = Host.Configuration();
        configuration["Directory:Tenants:0:id"] = id;
        Assert.Throws<InvalidOperationException>(() => new TenantDirectory(configuration));
        Assert.Equal(id, configuration["Directory:Tenants:0:id"]);
    }

    [Theory]
    [InlineData("Alpha")]
    [InlineData("01-Team_A")]
    [InlineData("部门一")]
    [InlineData("length-64")]
    public void TenantDirectory_PreservesValidExistingIds(string value)
    {
        var id = value == "length-64" ? new string('a', 64) : value;
        var configuration = Host.Configuration();
        configuration["Directory:Tenants:0:id"] = id;
        var directory = new TenantDirectory(configuration);
        var scopes = new TenantScopes(directory);
        using (scopes.ForTenant(id).Enter())
        {
            Assert.Equal(id, scopes.Current);
            Assert.Equal(id, scopes.Capture().PartitionKey);
            Assert.Equal(id, directory.Require(id).Id);
        }
        Assert.Throws<ApprovalException>(() => scopes.Current);
        Assert.Equal(id, configuration["Directory:Tenants:0:id"]);
    }

    [Theory]
    [InlineData("ALPHA")]
    [InlineData(" alpha")]
    [InlineData("alpha ")]
    public async Task TenantIdentity_RejectsNoncanonicalClaimsAndPreservesStoredRows(string forged)
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var directory = host.App.Services.GetRequiredService<TenantDirectory>();
        var identity = directory.DemoIdentities.Single(i => i.TenantId == "alpha" && i.UserId == "alice");
        identity.TenantId = forged; // Simulate an authenticated issuer carrying a noncanonical claim.
        using var response = await host.Send("alpha", "alice", HttpMethod.Get, "/demo/expenses");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var row = await db.Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, row.Id);
                Assert.Equal("Submitted", row.Status);
                Assert.Equal(0, await db.Expenses.CountAsync(e => EF.Property<string>(e, TenantDbContext.TenantProperty) == forged));
            });
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("directory")]
    public async Task ReusedIdentityServices_CannotCrossTenantWithSameUserId(string service)
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        var directory = host.App.Services.GetRequiredService<TenantDirectory>();
        TenantUsers users;
        using (scopes.ForTenant("alpha").Enter())
        {
            users = new TenantUsers(scopes, directory);
            Assert.Equal(new[] { "bob" }, await users.GetUserIdsInRoleAsync("Reviewer", default));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice"), new Claim("tenant_id", "alpha")], "verified"));
        var actor = new TenantActor(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }, scopes, directory);
        Exception? error;
        using (scopes.ForTenant("bravo").Enter())
            error = await Record.ExceptionAsync(async () =>
            {
                if (service == "actor") actor.Resolve();
                else await users.GetUserIdsInRoleAsync("Reviewer", default);
            });
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var expense = await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, expense.Id);
                Assert.Equal("Submitted", expense.Status);
            });
        Assert.Equal(ApprovalErrorCode.Forbidden, Assert.IsType<ApprovalException>(error).Code);
    }

    [Theory]
    [InlineData("raw-read", false)]
    [InlineData("raw-read", true)]
    [InlineData("raw-write", false)]
    [InlineData("raw-write", true)]
    [InlineData("partition-update", false)]
    [InlineData("partition-update", true)]
    [InlineData("filter-delete", false)]
    [InlineData("filter-delete", true)]
    public async Task TenantDatabase_AttacksAreRejectedAndBothExpensesRemainIntact(string attack, bool async)
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        Exception? error = null;
        await host.InTenant("alpha", async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            error = await Record.ExceptionAsync(async () =>
            {
                switch (attack)
                {
                    case "raw-read":
                        var query = db.Database.SqlQueryRaw<string>("SELECT BusinessKey AS Value FROM HostExpenses");
                        if (async) await query.ToArrayAsync(); else query.ToArray(); break;
                    case "raw-write":
                        if (async) await db.Database.ExecuteSqlRawAsync("DELETE FROM HostExpenses");
                        else db.Database.ExecuteSqlRaw("DELETE FROM HostExpenses"); break;
                    case "partition-update":
                        if (async) await db.Expenses.ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<string>(r, TenantDbContext.TenantProperty), "bravo"));
                        else db.Expenses.ExecuteUpdate(s => s.SetProperty(r => EF.Property<string>(r, TenantDbContext.TenantProperty), "bravo")); break;
                    case "filter-delete":
                        if (async) await db.Expenses.IgnoreQueryFilters().ExecuteDeleteAsync();
                        else db.Expenses.IgnoreQueryFilters().ExecuteDelete(); break;
                }
            });
        });
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var db = sp.GetRequiredService<TenantDbContext>();
                var expense = Assert.Single(await db.Expenses.AsNoTracking().ToArrayAsync());
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, expense.Id);
                Assert.Equal("Submitted", expense.Status); Assert.Equal(12m, expense.Amount);
                Assert.Equal(0, await db.Expenses.CountAsync(e => e.Id == (tenant == "alpha" ? bravo.ExpenseId : alpha.ExpenseId)));
            });
        Assert.IsType<InvalidOperationException>(error);
    }

    [Theory]
    [InlineData("double-dispose")]
    [InlineData("out-of-order")]
    public async Task TenantScopes_InvalidDisposalCannotRestoreAnotherTenant(string scenario)
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        var first = scopes.ForTenant("alpha").Enter();
        if (scenario == "double-dispose") first.Dispose();
        using (scopes.ForTenant("bravo").Enter())
        {
            if (scenario == "out-of-order") Assert.Throws<InvalidOperationException>(first.Dispose);
            else first.Dispose();
            Assert.Equal("bravo", scopes.Current);
            await using var serviceScope = host.App.Services.CreateAsyncScope();
            Assert.Equal(bravo.ExpenseId, (await serviceScope.ServiceProvider.GetRequiredService<TenantDbContext>().Expenses.SingleAsync()).Id);
        }
        if (scenario == "out-of-order")
        {
            Assert.Equal("alpha", scopes.Current);
            await using var serviceScope = host.App.Services.CreateAsyncScope();
            Assert.Equal(alpha.ExpenseId, (await serviceScope.ServiceProvider.GetRequiredService<TenantDbContext>().Expenses.SingleAsync()).Id);
            first.Dispose();
        }
        Assert.Throws<ApprovalException>(() => scopes.Current);
    }

    [Fact]
    public async Task TenantScope_DisposeInChildFlowCannotPreventParentRestoration()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        var lease = scopes.ForTenant("alpha").Enter();
        await System.Threading.Tasks.Task.Run(() =>
        {
            lease.Dispose();
            Assert.Throws<ApprovalException>(() => scopes.Current);
        });
        Assert.Equal("alpha", scopes.Current);
        lease.Dispose();
        Assert.Throws<ApprovalException>(() => scopes.Current);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var expense = await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, expense.Id);
                Assert.Equal("Submitted", expense.Status);
            });
    }

    [Fact]
    public async Task DisabledIdentity_CannotReadStoredTenantData()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        host.App.Services.GetRequiredService<TenantDirectory>().Require("alpha").Users.Single(u => u.Id == "alice").IsActive = false;
        using var denied = await host.Send("alpha", "alice", HttpMethod.Get, "/demo/expenses?TenantId=bravo", tenantHeader: "bravo");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp =>
            {
                var expense = await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync();
                Assert.Equal(tenant == "alpha" ? alpha.ExpenseId : bravo.ExpenseId, expense.Id);
                Assert.Equal("Submitted", expense.Status);
            });
    }

    [Fact]
    public async Task BackgroundScopes_UseIndependentContextsRestoreAndRetryOriginalTenant()
    {
        await using var host = await Host.Start();
        await host.Publish("alpha"); await host.Publish("bravo");
        var alpha = await host.Submit("alpha"); var bravo = await host.Submit("bravo");
        var scopes = host.App.Services.GetRequiredService<TenantScopes>();
        var runner = host.App.Services.GetRequiredService<ApprovalBackgroundRunner>();
        var seen = new List<(string Tenant, Guid Context, Guid Expense)>();
        using (scopes.ForTenant("bravo").Enter())
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Assert.Equal(attempt == 0 ? 1 : 2, await runner.RunAsync("security-probe", async (sp, ct) =>
                {
                    var db = sp.GetRequiredService<TenantDbContext>();
                    var expense = await db.Expenses.SingleAsync(ct);
                    seen.Add((scopes.Current, db.ContextId.InstanceId, expense.Id));
                    if (attempt == 0 && scopes.Current == "alpha") throw new IOException("isolated failure");
                    return 1;
                }));
                Assert.Equal("bravo", scopes.Current);
            }
        }
        Assert.Throws<ApprovalException>(() => scopes.Current);
        Assert.Equal(new[] { "alpha", "bravo", "alpha", "bravo" }, seen.Select(x => x.Tenant));
        Assert.Equal(4, seen.Select(x => x.Context).Distinct().Count());
        Assert.Equal(new[] { alpha.ExpenseId, bravo.ExpenseId, alpha.ExpenseId, bravo.ExpenseId }, seen.Select(x => x.Expense));
        foreach (var tenant in new[] { "alpha", "bravo" })
            await host.InTenant(tenant, async sp => Assert.Equal("Submitted", (await sp.GetRequiredService<TenantDbContext>().Expenses.SingleAsync()).Status));
    }
}
