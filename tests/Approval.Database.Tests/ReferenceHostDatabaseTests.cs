using Approval.Core.Abstractions;
using Approval.ReferenceHost;
using Approval.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private async Task RunReferenceHostAsync(Func<ReferenceHostScenario, Dictionary<string, string?>, Task> scenario)
    {
        await ClearDataAsync();
        string connection;
        await using (var original = fixture.Create())
        {
            connection = original.Database.GetConnectionString()!;
            await original.GetService<IMigrator>().MigrateAsync("0");
        }
        var config = ReferenceHostScenario.Configuration(fixture.Provider, connection);
        try
        {
            await using var first = await ReferenceHostScenario.StartAsync(config);
            await scenario(first, config);
        }
        finally
        {
            await using var app = ReferenceApplication.Build(["--environment", "Development"], b => ReferenceHostScenario.ConfigureBuilder(b, config));
            await using var scope = await app.Services.GetRequiredService<IApprovalScopeFactory>().CreateAsync();
            var reference = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            await reference.GetService<IMigrator>().MigrateAsync("0");
            await using var original = fixture.Create();
            await original.Database.MigrateAsync();
        }
    }
    [Fact]
    public Task ReferenceHost_ProviderMigrationsSingleHostWorkflowAndPermissionChanges() =>
        RunReferenceHostAsync(async (host, config) =>
        {
            // Reapplying the host's own migration chain must be harmless.
            await using (var again = await ReferenceHostScenario.StartAsync(config)) { }
            await host.SingleHostWorkflow();
        });
    [Fact]
    public Task ReferenceHost_BackgroundScopesSkipDisabledAndIsolateTenantFailure() =>
        RunReferenceHostAsync((host, _) => host.BackgroundIsolation());
    [Fact]
    public Task ReferenceHost_TwentyHttpRequestsAcrossTwoHostsHaveOneProviderResult() =>
        RunReferenceHostAsync(async (host, config) =>
        {
            await using var second = await ReferenceHostScenario.StartAsync(config);
            await host.TwentyRequestsAcross(second);
        });
}
