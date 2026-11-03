using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Approval.Core.Tests;

public sealed class DefinitionQueryOptimizationTests
{
    private sealed class Reads : DbCommandInterceptor
    {
        public int Management;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("DefinitionManagement", StringComparison.Ordinal)) Management++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Requests
    {
        public List<ApprovalPermissionRequest> Values { get; } = [];
    }
    private sealed class Permissions(Requests requests) : IApprovalPermissionProvider
    {
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        {
            requests.Values.Add(request);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task GovernanceSummary_LoadsOneProjectionAndPreservesPerResourcePermissionChecks()
    {
        var reads = new Reads(); var requests = new Requests();
        await using var host = new TestHost(build: b =>
        {
            b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(reads));
            b.Services.AddSingleton(requests);
            b.AddPermissionProvider<Permissions>();
        });
        foreach (var key in new[] { "alpha", "beta", "gamma" })
            await host.PublishAsync(key, Flows.SingleNode("Any", "User", "bob"));
        var values = await host.RunAsAsync("admin", async sp =>
        {
            reads.Management = 0; requests.Values.Clear();
            return await sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync();
        });
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, values.Select(v => v.Key));
        Assert.All(values, v => { Assert.Equal(1, v.Revision); Assert.Equal(1, v.LastVersion); Assert.True(v.IsEnabled); Assert.False(v.HasDraftChanges); });
        Assert.Equal(1, reads.Management);
        Assert.Equal(16, requests.Values.Count);
        foreach (var key in new[] { "alpha", "beta", "gamma" })
        {
            var scoped = requests.Values.Where(r => r.Resource.DefinitionKey == key).ToList();
            Assert.Equal(2, scoped.Count(r => r.Permission == ApprovalPermissions.DefinitionReadSensitive));
            foreach (var permission in new[] { ApprovalPermissions.DefinitionDesign, ApprovalPermissions.DefinitionPublish, ApprovalPermissions.DefinitionDisable })
                Assert.Single(scoped, r => r.Permission == permission);
        }
    }
}
