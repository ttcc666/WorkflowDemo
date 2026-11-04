using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
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
        public Func<ApprovalPermissionRequest, bool> Rule = _ => true;
    }
    private sealed class Permissions(Requests requests) : IApprovalPermissionProvider
    {
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        {
            requests.Values.Add(request);
            return Task.FromResult(requests.Rule(request));
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
        Assert.Equal(13, requests.Values.Count);
        Assert.Single(requests.Values, r => r.Permission == ApprovalPermissions.DefinitionReadSensitive && r.Resource.DefinitionKey is null);
        foreach (var key in new[] { "alpha", "beta", "gamma" })
        {
            var scoped = requests.Values.Where(r => r.Resource.DefinitionKey == key).ToList();
            Assert.Single(scoped, r => r.Permission == ApprovalPermissions.DefinitionReadSensitive);
            foreach (var permission in new[] { ApprovalPermissions.DefinitionDesign, ApprovalPermissions.DefinitionPublish, ApprovalPermissions.DefinitionDisable })
                Assert.Single(scoped, r => r.Permission == permission);
        }
    }

    [Fact]
    public async Task GovernanceSummary_RechecksEveryResourceAfterRevocationAndPreservesCapabilities()
    {
        var requests = new Requests();
        await using var host = new TestHost(build: b => { b.Services.AddSingleton(requests); b.AddPermissionProvider<Permissions>(); });
        foreach (var key in new[] { "alpha", "beta", "gamma" }) await host.PublishAsync(key, Flows.SingleNode("Any", "User", "bob"));
        await host.SeedStorageAsync(async db =>
        {
            var values = await db.Set<ApprovalDefinitionManagement>().ToListAsync();
            foreach (var value in values) value.Category = value.Key == "gamma" ? null : "team";
            var alpha = values.Single(v => v.Key == "alpha");
            alpha.DraftJson = Flows.SingleNode("Any", "User", "carol"); alpha.DraftRevision = 2;
            alpha.PublishedDraftRevision = 1; alpha.Revision = 3; alpha.IsEnabled = false;
            await db.SaveChangesAsync();
        });
        var revoked = new HashSet<string>(StringComparer.Ordinal) { "beta" };
        requests.Rule = r => r.Resource.DefinitionKey is not { } key ||
            (!revoked.Contains(key) && !(key == "alpha" && r.Permission == ApprovalPermissions.DefinitionPublish));
        await host.RunAsAsync("admin", async sp =>
        {
            var service = sp.GetRequiredService<ApprovalDefinitionService>();
            requests.Values.Clear();
            var first = await service.ListGovernanceAsync();
            Assert.Equal(new[] { "alpha", "gamma" }, first.Select(v => v.Key));
            var alpha = first[0];
            Assert.Equal(3, alpha.Revision); Assert.Equal(1, alpha.LastVersion); Assert.False(alpha.IsEnabled);
            Assert.True(alpha.HasDraftChanges); Assert.NotNull(alpha.ActiveDefinitionId); Assert.Equal("team", alpha.Category);
            Assert.True(alpha.Capabilities[ApprovalPermissions.DefinitionDesign]);
            Assert.False(alpha.Capabilities[ApprovalPermissions.DefinitionPublish]);
            Assert.True(alpha.Capabilities[ApprovalPermissions.DefinitionDisable]);
            Assert.Equal(10, requests.Values.Count);
            AssertReadChecksOnce(requests, "alpha", "beta", "gamma");
            revoked.Add("alpha"); requests.Values.Clear();
            Assert.Equal("gamma", Assert.Single(await service.ListGovernanceAsync()).Key);
            Assert.Equal(7, requests.Values.Count);
            AssertReadChecksOnce(requests, "alpha", "beta", "gamma");
            Assert.DoesNotContain(requests.Values, r => r.Resource.DefinitionKey is "alpha" or "beta" && r.Permission != ApprovalPermissions.DefinitionReadSensitive);
            Assert.Empty(await service.ListGovernanceAsync("team"));
            Assert.Equal("gamma", Assert.Single(await service.ListGovernanceAsync(null, uncategorized: true)).Key);
            Assert.Empty(await service.ListCategoriesAsync());
            revoked.Remove("alpha");
            Assert.Equal(new[] { "team" }, await service.ListCategoriesAsync());
            return true;
        });
    }

    [Fact]
    public async Task GovernanceAndCategories_GlobalDenialStopsBeforeResourceQueries()
    {
        var reads = new Reads(); var requests = new Requests();
        await using var host = new TestHost(build: b =>
        {
            b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(reads));
            b.Services.AddSingleton(requests); b.AddPermissionProvider<Permissions>();
        });
        await host.PublishAsync("alpha", Flows.SingleNode("Any", "User", "bob"));
        requests.Rule = r => r.Resource.DefinitionKey is not null;
        await host.RunAsAsync("admin", async sp =>
        {
            var service = sp.GetRequiredService<ApprovalDefinitionService>();
            foreach (var categories in new[] { false, true })
            {
                reads.Management = 0; requests.Values.Clear();
                var failure = await Assert.ThrowsAsync<ApprovalException>(async () =>
                {
                    if (categories) await service.ListCategoriesAsync(); else await service.ListGovernanceAsync();
                });
                Assert.Equal(ApprovalErrorCode.Forbidden, failure.Code);
                Assert.Equal(0, reads.Management);
                var request = Assert.Single(requests.Values);
                Assert.Equal(ApprovalPermissions.DefinitionReadSensitive, request.Permission);
                Assert.Null(request.Resource.DefinitionKey);
            }
            return true;
        });
    }

    private static void AssertReadChecksOnce(Requests requests, params string[] keys)
    {
        Assert.Single(requests.Values, r => r.Permission == ApprovalPermissions.DefinitionReadSensitive && r.Resource.DefinitionKey is null);
        foreach (var key in keys)
            Assert.Single(requests.Values, r => r.Permission == ApprovalPermissions.DefinitionReadSensitive && r.Resource.DefinitionKey == key);
    }
}
