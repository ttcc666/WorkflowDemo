using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Diagnostics;
using Approval.Core.Domain;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class SingleHostBoundaryTests
{
    [Fact]
    public async Task Approval_model_has_no_tenant_columns_filters_or_runtime_catalog()
    {
        await using var host = new TestHost();
        await host.RunAsync(sp =>
        {
            var model = sp.GetRequiredService<TestDbContext>().Model;
            foreach (var type in model.GetEntityTypes().Where(t => typeof(IApprovalEntity).IsAssignableFrom(t.ClrType)))
            {
                Assert.Null(type.FindProperty("TenantId"));
                Assert.DoesNotContain(type.GetIndexes(), i => i.Properties.Any(p => p.Name == "TenantId"));
            }
            Assert.DoesNotContain(typeof(ApprovalOptions).GetProperties(), p => p.Name is "MultiTenant" or "FixedTenantId");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Background_failure_is_observable_and_does_not_prevent_next_run()
    {
        await using var host = new TestHost();
        await host.RunAsync(async sp =>
        {
            var runner = sp.GetRequiredService<ApprovalBackgroundRunner>();
            Assert.Equal(0, await runner.RunAsync("probe", (_, _) => Task.FromException<int>(new InvalidOperationException("controlled failure"))));
            Assert.False(Assert.Single(sp.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
            Assert.Equal(7, await runner.RunAsync("probe", (_, _) => Task.FromResult(7)));
            Assert.True(Assert.Single(sp.GetRequiredService<ApprovalBackgroundState>().Snapshot()).Succeeded);
        });
    }

    [Fact]
    public void Trusted_actor_cannot_be_reset_or_initialized_with_invalid_permissions()
    {
        var actor = new ApprovalExecutionContext();
        actor.InitializeSystem("worker", [ApprovalPermissions.TaskTimeout]);
        Assert.Equal("worker", actor.ActorId); Assert.True(actor.IsSystem);
        Assert.Single(actor.AllowedPermissions);
        Assert.Throws<InvalidOperationException>(() => actor.InitializeSystem("attacker", [ApprovalPermissions.DefinitionPublish]));
        Assert.Throws<ArgumentException>(() => new ApprovalExecutionContext().InitializeSystem("worker", ["invented.permission"]));
    }
}
