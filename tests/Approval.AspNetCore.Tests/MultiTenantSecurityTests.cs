using System.Net;
using System.Security.Claims;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.MultiTenantHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed partial class MultiTenantHostTests
{
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
