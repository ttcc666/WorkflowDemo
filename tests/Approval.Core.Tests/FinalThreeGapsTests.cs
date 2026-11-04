using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class FinalThreeGapsTests
{
    private const string Flow = """
        {"startNodeId":"s","nodes":{"s":{"type":"Start","next":"cc"},"cc":{"type":"Cc","approvers":[{"type":"Role","value":"Readers"}],"next":"a"},
        "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"e"},"e":{"type":"End"}}}
        """;
    private static string Categorized(string? category) => Flow.Replace("\"startNodeId\"", "\"settings\":{\"category\":" + JsonSerializer.Serialize(category) + "},\"startNodeId\"");
    private sealed class Permissions : IApprovalPermissionProvider
    {
        public Func<ApprovalPermissionRequest, bool> Rule = _ => true;
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(Rule(request));
    }
    private sealed class Hidden : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) => Task.FromResult(instances.Where(i => i.BusinessKey != "hidden"));
    }
    private sealed class Directory : IApprovalUserDirectory
    {
        public List<string[]> Requests = [];
        public Task<IReadOnlyList<ApprovalUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            Requests.Add(ids.ToArray());
            return Task.FromResult<IReadOnlyList<ApprovalUser>>(ids.Where(id => id != "r2").Select(id => new ApprovalUser(id, "Name " + id, []) { IsActive = id != "r1" }).Append(new("unrequested", "SECRET", [])).ToArray());
        }
        public Task<IReadOnlyList<ApprovalUser>> SearchUsersAsync(string? keyword, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
    [Fact]
    public async Task Categories_follow_draft_then_selected_version_and_filter_including_uncategorized()
    {
        await using var host = new TestHost();
        await host.PublishAsync("one", Categorized(" 财务 ")); await host.PublishAsync("empty", Categorized("  "));
        var first = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("one"));
        await host.PublishAsync("one", Categorized("人事"));
        var selected = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SelectVersionAsync("one", first!.Id, 2));
        Assert.Equal("财务", selected.Category);
        var draft = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("one", "One", Categorized("草稿"), selected.Revision));
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SelectVersionAsync("one", first!.Id, draft.Revision));
        var filtered = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("草稿"));
        Assert.Equal("one", Assert.Single(filtered).Key); Assert.Equal("草稿", filtered[0].Category);
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("人事")));
        Assert.Equal("empty", Assert.Single(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync(null, true))).Key);
        var state = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("one"));
        var published = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("one", state!.Revision));
        Assert.Equal("草稿", published.Category);
        var copy = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().CopyAsync("one", "copy", "Copy"));
        Assert.Equal("草稿", copy.Category); Assert.Null(copy.ActiveDefinitionId);
        Assert.Equal(["草稿"], await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListCategoriesAsync()));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("草稿", true)));
    }
    [Theory]
    [InlineData(101, false)] [InlineData(1, true)]
    public async Task Invalid_category_is_rejected_without_partial_draft(int length, bool control)
    {
        await using var host = new TestHost();
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("new", "New", Categorized(control ? "a\nb" : new string('x', length)), 0)));
        await host.RunAsync(async sp => Assert.Empty(await sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinitionManagement>().ToListAsync()));
    }
    [Fact]
    public async Task Category_options_and_results_do_not_disclose_forbidden_definition()
    {
        var permissions = new Permissions(); await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(permissions));
        await host.PublishAsync("visible", Categorized("Public")); await host.PublishAsync("secret", Categorized("Secret"));
        permissions.Rule = r => r.Resource.DefinitionKey != "secret";
        Assert.Equal(["Public"], await host.RunAsAsync("reader", sp => sp.GetRequiredService<ApprovalDefinitionService>().ListCategoriesAsync()));
        Assert.Empty(await host.RunAsAsync("reader", sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("Secret")));
    }
    [Fact]
    public async Task Recipients_page_current_members_without_inventing_missing_or_inactive_users()
    {
        var directory = new Directory(); await using var host = new TestHost(build: b => b.Services.AddSingleton<IApprovalUserDirectory>(directory));
        host.Identity.Add("r1", "Readers"); host.Identity.Add("r2", "Readers"); host.Identity.Add("r3", "Readers");
        await host.PublishAsync("f", Flow); var instance = await host.StartAsync("f", "one", "alice");
        var copy = Assert.Single(await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().HistoryAsync("alice", instance.Id)));
        directory.Requests.Clear();
        var first = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id, new(1, 2)));
        Assert.True(first.IsCurrentRoleMembership); Assert.Equal(3, first.Total); Assert.Equal(["r1", "r2"], Assert.Single(directory.Requests));
        Assert.False(first.Items[0].IsActive); Assert.True(first.Items[0].IsKnown);
        Assert.False(first.Items[1].IsKnown); Assert.Null(first.Items[1].Name); Assert.Null(first.Items[1].IsActive);
        Assert.DoesNotContain(first.Items, i => i.UserId == "unrequested");
        host.Identity.UserRoles.Remove("r3"); host.Identity.Add("r4", "Readers");
        var second = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id, new(2, 2)));
        Assert.Equal("r4", Assert.Single(second.Items).UserId); Assert.Equal(["r4"], directory.Requests[1]);
        var empty = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id, new(3, 2)));
        Assert.Empty(empty.Items); Assert.Equal(2, directory.Requests.Count);
        foreach (var id in new[] { "r1", "r2", "r4" }) host.Identity.UserRoles.Remove(id);
        var noMembers = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id));
        Assert.Equal(0, noMembers.Total); Assert.Empty(noMembers.Items); Assert.True(noMembers.IsCurrentRoleMembership); Assert.Equal(2, directory.Requests.Count);
        await host.RunAsync(async sp => Assert.Empty(await sp.GetRequiredService<TestDbContext>().Set<ApprovalCarbonCopyRead>().ToListAsync()));
    }
    [Fact]
    public async Task Recipients_require_directory_and_instance_permission_and_query_scope()
    {
        var directory = new Directory(); var permissions = new Permissions();
        await using var host = new TestHost(build: b => { b.Services.AddSingleton<IApprovalPermissionProvider>(permissions); b.Services.AddSingleton<IApprovalUserDirectory>(directory); b.AddQueryScope<Hidden>(); });
        host.Identity.Add("r1", "Readers"); await host.PublishAsync("f", Flow);
        var visible = await host.StartAsync("f", "one", "alice"); var hidden = await host.StartAsync("f", "hidden", "alice");
        var ids = await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalCarbonCopy>().ToDictionaryAsync(c => c.InstanceId, c => c.Id));
        directory.Requests.Clear(); permissions.Rule = r => r.Permission != ApprovalPermissions.InstanceReadAll;
        Task<CarbonCopyRecipients> Query(string actor, Guid id) => host.RunAsAsync(actor, sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync(actor, id));
        Assert.Equal(ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<ApprovalException>(() => Query("stranger", ids[visible.Id]))).Code);
        Assert.Equal(ApprovalErrorCode.NotFound, (await Assert.ThrowsAsync<ApprovalException>(() => Query("alice", ids[hidden.Id]))).Code);
        foreach (var permission in new[] { ApprovalPermissions.DirectoryRead, ApprovalPermissions.InstanceRead }) {
            permissions.Rule = r => r.Permission != permission && r.Permission != ApprovalPermissions.InstanceReadAll;
            Assert.Equal(ApprovalErrorCode.Forbidden, (await Assert.ThrowsAsync<ApprovalException>(() => Query("alice", ids[visible.Id]))).Code);
        }
        Assert.Empty(directory.Requests);
        permissions.Rule = r => r.Permission != ApprovalPermissions.InstanceReadAll; Assert.Equal("r1", Assert.Single((await Query("bob", ids[visible.Id])).Items).UserId);
    }
    [Fact]
    public async Task Missing_directory_is_explicit_and_direct_user_is_not_expanded()
    {
        await using var host = new TestHost(); await host.PublishAsync("f", Flow.Replace("\"Role\",\"value\":\"Readers\"", "\"User\",\"value\":\"bob\""));
        var instance = await host.StartAsync("f", "one", "alice"); var copy = Assert.Single(await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().HistoryAsync("alice", instance.Id)));
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id)));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        var directory = new Directory(); var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id, publicDirectory: directory));
        Assert.False(result.IsCurrentRoleMembership); Assert.Equal("bob", Assert.Single(result.Items).UserId);
        Assert.Equal(["bob"], Assert.Single(directory.Requests));
    }
    [Theory]
    [InlineData("HostExtension", "Sync")]
    [InlineData("HostExtension", "Async")]
    [InlineData("Http", "Sync")]
    [InlineData("Script", "Async")]
    public async Task Unsupported_action_protocol_is_rejected_without_partial_writes(string type, string mode)
    {
        await using var host = new TestHost();
        var json = Flow.Replace("\"type\":\"Approval\"", "\"type\":\"Action\",\"actions\":[{\"type\":\"" + type + "\",\"mode\":\"" + mode + "\",\"continueOnError\":true,\"parameters\":{\"secret\":\"private\"}}]");
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("f", json));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("f", "F", json, 0)));
        await host.RunAsync(async sp => { var db = sp.GetRequiredService<TestDbContext>(); Assert.Empty(await db.Set<ApprovalInstance>().ToListAsync()); Assert.Empty(await db.Set<ApprovalDefinition>().ToListAsync()); Assert.Empty(await db.Set<ApprovalDefinitionManagement>().ToListAsync()); });
    }
    private sealed class DirtyIdentity : IApprovalIdentityProvider
    {
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["r2", "", "r1", "r2", " "]);
    }
    [Fact]
    public async Task Recipient_members_are_deduplicated_sorted_and_page_limits_are_normalized()
    {
        var directory = new Directory(); await using var host = new TestHost(build: b => { b.Services.AddSingleton<IApprovalIdentityProvider, DirtyIdentity>(); b.Services.AddSingleton<IApprovalUserDirectory>(directory); });
        await host.PublishAsync("f", Flow); var instance = await host.StartAsync("f", "one", "alice");
        var copy = Assert.Single(await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().HistoryAsync("alice", instance.Id))); directory.Requests.Clear();
        var result = await host.RunAsync(sp => sp.GetRequiredService<ApprovalCarbonCopyService>().GetRecipientsAsync("alice", copy.Id, new(0, 1000)));
        Assert.Equal(1, result.Page); Assert.Equal(100, result.PageSize); Assert.Equal(2, result.Total);
        Assert.Equal(["r1", "r2"], result.Items.Select(i => i.UserId)); Assert.Equal(["r1", "r2"], Assert.Single(directory.Requests));
    }
    [Fact]
    public async Task Category_accepts_exact_limit_and_draft_only_classification()
    {
        await using var host = new TestHost(); var category = new string('中', 100);
        var draft = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("only", "Only", Categorized(category), 0));
        Assert.Equal(category, draft.Category); Assert.Equal(0, draft.LastVersion);
        Assert.Equal("only", Assert.Single(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync(category))).Key);
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinition>().ToListAsync()));
        var published = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishDesignDraftAsync("only", draft.Revision));
        Assert.Equal(category, published.Category);
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("too-long", Categorized(category + "中")));
    }
    [Fact]
    public async Task Unknown_node_members_are_rejected_without_partial_writes()
    {
        await using var host = new TestHost();
        var json = Flow.Replace("\"type\":\"Approval\"", "\"type\":\"Approval\",\"actions\":[{\"type\":\"HostExtension\"}]");
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("hidden", json));
        await Assert.ThrowsAsync<ApprovalException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("hidden", "Hidden", json, 0)));
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinition>().ToListAsync()));
    }
    [Fact]
    public async Task Direct_publication_preserves_draft_category_and_unmanaged_definitions_are_not_startable()
    {
        await using var host = new TestHost();
        var draft = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().SaveDesignDraftAsync("f", "Draft", Categorized("Draft"), 0));
        await host.PublishAsync("f", Categorized("Published"));
        var state = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("f"));
        Assert.Equal("Draft", state!.Category); Assert.Equal(draft.DraftJson, state.DraftJson); Assert.True(state.HasDraftChanges);
        await host.SeedStorageAsync(async db => {
            db.Add(new ApprovalDefinition { Id = Guid.NewGuid(), Key = "unmanaged", Name = "Unmanaged", DefinitionJson = Categorized("Unmanaged"), Category = "Unmanaged", Version = 1, IsPublished = true });
            await db.SaveChangesAsync();
        });
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("Unmanaged")));
        Assert.Null(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetGovernanceAsync("unmanaged")));
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("unmanaged", "one", "alice"));
        Assert.Equal(ApprovalErrorCode.NotFound, error.Code);
        Assert.Empty(await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().ListGovernanceAsync("Published")));
    }
}
