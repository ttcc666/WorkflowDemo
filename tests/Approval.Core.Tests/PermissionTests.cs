using Approval.Forms;
using Approval.Forms.Runtime;
using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Core.Hosting;
using Approval.Forms.Attachments;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class PermissionTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"Review","mode":"Any","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;
    private sealed class Actor : IApprovalContextResolver
    {
        public string? User { get; set; }
        public ApprovalExecutionIdentity Resolve() => new(User);
    }
    private sealed class Permissions : IApprovalPermissionProvider
    {
        public Func<ApprovalPermissionRequest, CancellationToken, Task<bool>> Decide { get; set; } = (_, _) => Task.FromResult(true);
        public List<ApprovalPermissionRequest> Calls { get; } = [];
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
        { Calls.Add(request); return Decide(request, ct); }
    }
    private sealed class Scope : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromResult(instances.Where(i => i.BusinessKey != "hidden"));
    }
    private sealed class UnavailableScope : IApprovalQueryScope
    {
        public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
            Task.FromException<IQueryable<ApprovalInstance>>(new HttpRequestException("data permissions offline"));
    }
    private static TestHost Host(Permissions permissions, bool scope = false, IApprovalQueryScope? customScope = null, bool withForms = false) => new(
        configure: o => { o.EnableBackgroundServices = false; o.PermissionTimeout = TimeSpan.FromMilliseconds(150); },
        build: b =>
        {
            b.Services.AddScoped<IApprovalContextResolver, Actor>();
            b.Services.AddSingleton<IApprovalPermissionProvider>(permissions);
            if (scope) b.AddQueryScope<Scope>();
            if (customScope is not null) b.Services.AddSingleton(customScope);
            if (withForms) b.AddApprovalForms();
        });
    private static async Task<T> As<T>(TestHost host, string? user, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = host.NewScope(authorize: true);
        ((Actor)scope.ServiceProvider.GetRequiredService<IApprovalContextResolver>()).User = user;
        return await action(scope.ServiceProvider);
    }
    private static Task As(TestHost host, string? user, Func<IServiceProvider, Task> action) =>
        As<object?>(host, user, async sp => { await action(sp); return null; });
    private static Task<T> Command<T>(IServiceProvider sp, string permission, Func<IServiceProvider, Task<T>> action) =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("permission.test", "test", Guid.NewGuid().ToString("N"), null, permission), (attempt, _) => action(attempt));
    private static Task<T> AsCommand<T>(TestHost host, string? user, string permission, Func<IServiceProvider, Task<T>> action) =>
        As(host, user, sp => Command(sp, permission, action));
    private static Task AsCommand(TestHost host, string? user, string permission, Func<IServiceProvider, Task> action) =>
        AsCommand<object?>(host, user, permission, async sp => { await action(sp); return null; });
    private static async Task Seed(TestHost host)
    {
        await host.PublishAsync("doc", Flow);
        await host.StartAsync("doc", "visible", "alice");
    }
    private static async Task AssertCode(ApprovalErrorCode code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<ApprovalException>(action)).Code);

    [Fact]
    public async Task DirectManagement_DeniedWithoutMutations_ThenAllowedSeparately()
    {
        var permissions = new Permissions { Decide = (r, _) => Task.FromResult(!ApprovalPermissions.IsManagement(r.Permission)) };
        await using var host = Host(permissions); await Seed(host);
        var instance = Assert.Single(await host.TodoAsync("bob"));
        var outboxId = Guid.NewGuid();
        await host.SeedStorageAsync(async db => { db.Add(new ApprovalOutboxMessage { Id = outboxId, Target = "test", PayloadJson = "{}", Status = OutboxStatus.Failed }); await db.SaveChangesAsync(); });
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.InstanceTerminate, sp => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.InstanceId, "bob", "stop")));
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.TaskReassign, sp => sp.GetRequiredService<ApprovalEngine>().ReassignAsync(instance.TaskId, "bob", "other", "handover")));
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.OutboxRetry, sp => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(outboxId)));
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.DefinitionPublish, sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync("new", "New", Flow)));
        await AssertCode(ApprovalErrorCode.Forbidden, () => As(host, "bob", sp => sp.GetRequiredService<ApprovalQueryService>().GetInstanceAsync(instance.InstanceId)));
        Assert.Equal(InstanceStatus.Running, (await host.GetInstanceAsync(instance.InstanceId)).Status);
        Assert.Equal("bob", Assert.Single(await host.TodoAsync("bob")).AssigneeValue);
        permissions.Decide = (r, _) => Task.FromResult(r.Permission == ApprovalPermissions.InstanceTerminate);
        await AsCommand(host, "bob", ApprovalPermissions.InstanceTerminate, sp => sp.GetRequiredService<ApprovalEngine>().TerminateAsync(instance.InstanceId, "bob", "stop"));
        Assert.Equal(InstanceStatus.Terminated, (await host.GetInstanceAsync(instance.InstanceId)).Status);
    }

    [Fact]
    public async Task DefinitionLists_HonorResourcePermissionsForLatestVersionsAndSummaries()
    {
        var permissions = new Permissions();
        await using var host = Host(permissions);
        await host.PublishAsync("visible", Flow);
        await host.PublishAsync("visible", Flow.Replace("Review", "Updated review"));
        await host.PublishAsync("hidden", Flow.Replace("Review", "Confidential review"));
        var oldId = await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalDefinition>()
            .Where(d => d.Key == "visible" && d.Version == 1).Select(d => d.Id).SingleAsync());
        permissions.Decide = (request, _) => Task.FromResult(
            request.Permission is not (ApprovalPermissions.DefinitionRead or ApprovalPermissions.DefinitionReadSensitive)
            || request.Resource.DefinitionKey != "hidden" && request.Resource.DefinitionId != oldId);

        await As(host, "bob", async sp =>
        {
            var service = sp.GetRequiredService<ApprovalDefinitionService>();
            var latest = Assert.Single(await service.ListLatestAsync());
            Assert.Equal("visible", latest.Key);
            Assert.Equal(2, latest.Version);
            var version = Assert.Single(await service.ListVersionsAsync("visible"));
            Assert.Equal(latest.Id, version.Id);
            Assert.Equal("visible", Assert.Single(await service.ListFlowsAsync()).Key);
        });
        permissions.Decide = (_, _) => Task.FromResult(true);
        await As(host, "bob", async sp =>
        {
            var service = sp.GetRequiredService<ApprovalDefinitionService>();
            Assert.Equal(2, (await service.ListLatestAsync()).Count);
            Assert.Equal(new[] { 2, 1 }, (await service.ListVersionsAsync("visible")).Select(d => d.Version));
            Assert.Equal(new[] { "hidden", "visible" }, (await service.ListFlowsAsync()).Select(d => d.Key));
        });
    }

    [Fact]
    public async Task ActorSpoofAndMissingIdentity_AreRejectedBeforeProvider()
    {
        var permissions = new Permissions(); await using var host = Host(permissions); await Seed(host);
        var task = Assert.Single(await host.TodoAsync("bob")); permissions.Calls.Clear();
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "alice", ApprovalPermissions.TaskApprove, async sp =>
        {
            permissions.Calls.Clear(); // The command is authorized; the engine must reject the forged operator before another provider call.
            await sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob");
        }));
        await AssertCode(ApprovalErrorCode.Unauthorized, () => As(host, null, sp => sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("bob")));
        Assert.Empty(permissions.Calls);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single((await host.GetInstanceAsync(task.InstanceId)).Tasks).Status);
    }

    [Fact]
    public async Task FormDraftFileAndMessageEntries_DenyBeforeSideEffects()
    {
        var permissions = new Permissions { Decide = (_, _) => Task.FromResult(false) };
        await using var host = Host(permissions, withForms: true);
        using var json = JsonDocument.Parse("{}");
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.InstanceStart, sp => sp.GetRequiredService<ApprovalFormService>().StartAsync("doc", "bob", json.RootElement)));
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.DraftWrite, sp => sp.GetRequiredService<ApprovalFormService>().SaveDraftAsync("bob", "doc", null, json.RootElement, null)));
        using var content = new MemoryStream([1]);
        await AssertCode(ApprovalErrorCode.Forbidden, () => As(host, "bob", sp => sp.GetRequiredService<ApprovalFileService>().UploadAsync("bob", "test.txt", content)));
        Assert.Equal(0, content.Position);
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.MessageWrite, sp => sp.GetRequiredService<ApprovalMessageService>().MarkAllReadAsync("bob")));
        await AssertCode(ApprovalErrorCode.Forbidden, () => As(host, "bob", sp => sp.GetRequiredService<ApprovalDefinitionService>().ValidateAsync(FlowDefinition.Parse(Flow))));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal(0, await db.Set<ApprovalDraft>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalFile>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalInstance>().CountAsync());
        });
    }

    [Fact]
    public async Task ProviderReceivesCanonicalResource_RevocationHasNoCachedAllow()
    {
        var permissions = new Permissions(); await using var host = Host(permissions); await Seed(host);
        var task = Assert.Single(await host.TodoAsync("bob"));
        await As(host, "bob", async sp =>
        {
            var auth = sp.GetRequiredService<ApprovalAuthorizationService>();
            Assert.True(await auth.CanAsync(ApprovalPermissions.TaskApprove, new() { TaskId = task.TaskId, BusinessKey = "forged" }));
            var call = Assert.Single(permissions.Calls);
            Assert.Equal(new ApprovalPermissionSubject("bob", false), call.Subject);
            Assert.Equal(task.InstanceId, call.Resource.InstanceId); Assert.Equal("doc", call.Resource.DefinitionKey);
            Assert.Equal("visible", call.Resource.BusinessKey); Assert.NotNull(call.Resource.DefinitionId);
            permissions.Decide = (_, _) => Task.FromResult(false);
            Assert.False(await auth.CanAsync(ApprovalPermissions.TaskApprove, new() { TaskId = task.TaskId }));
        });
        Assert.Equal(2, permissions.Calls.Count);
    }

    [Fact]
    public async Task AllowDoesNotBypassAssignee_AndForgedViewerCannotBecomeAdmin()
    {
        var permissions = new Permissions { Decide = (r, _) => Task.FromResult(r.Permission != ApprovalPermissions.InstanceReadAll) };
        await using var host = Host(permissions); await Seed(host);
        var task = Assert.Single(await host.TodoAsync("bob"));
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "outsider", ApprovalPermissions.TaskApprove, sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "outsider")));
        var detail = await As(host, "outsider", sp => sp.GetRequiredService<ApprovalQueryService>().GetInstanceForViewerAsync(task.InstanceId, new("outsider", true)));
        Assert.Null(detail);
        Assert.Single(await host.TodoAsync("bob"));
        // Simulate an existing/legacy self-assigned task. New starts already exclude the initiator.
        await host.SeedStorageAsync(async db => { var row = await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == task.InstanceId); row.InitiatorId = "bob"; await db.SaveChangesAsync(); });
        await AssertCode(ApprovalErrorCode.Forbidden, () => AsCommand(host, "bob", ApprovalPermissions.TaskApprove, sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob")));
    }

    [Fact]
    public async Task DataScope_AppliesBeforePaginationCountAndBulkReassign()
    {
        var permissions = new Permissions(); await using var host = Host(permissions, scope: true); await Seed(host);
        var hidden = await host.StartAsync("doc", "hidden", "alice");
        await host.SeedStorageAsync(async db =>
        {
            db.Add(new ApprovalMessage { Id = Guid.NewGuid(), InstanceId = hidden.Id, UserId = "bob", Title = "hidden", Content = "hidden" });
            db.Add(new ApprovalOutboxMessage { Id = Guid.NewGuid(), InstanceId = hidden.Id, Target = "hidden", PayloadJson = "{}" });
            await db.SaveChangesAsync();
        });
        await As(host, "bob", async sp =>
        {
            var q = sp.GetRequiredService<ApprovalQueryService>();
            var todo = await q.GetTodoAsync("bob", page: new(1, 1));
            Assert.Equal(1, todo.Total); Assert.Equal("visible", Assert.Single(todo.Items).BusinessKey);
            Assert.Equal(1, await q.GetTodoCountAsync("bob"));
            Assert.Equal(1, (await q.SearchInstancesAsync(page: new(1, 1))).Total);
            Assert.Empty(await q.GetByBusinessAsync(RecordingBusinessHandler.Type, "hidden"));
            Assert.Empty(await sp.GetRequiredService<ApprovalMessageService>().ListAsync("bob"));
            Assert.Equal(0, await sp.GetRequiredService<ApprovalMessageService>().UnreadCountAsync("bob"));
            Assert.Equal(0, await Command(sp, ApprovalPermissions.MessageWrite, attempt => attempt.GetRequiredService<ApprovalMessageService>().MarkAllReadAsync("bob")));
            var visibleEvents = await sp.GetRequiredService<ApprovalOutboxService>().ListAsync(new());
            Assert.Equal(3, visibleEvents.Total);
            Assert.All(visibleEvents.Items, message => Assert.NotEqual(hidden.Id, message.InstanceId));
            await AssertCode(ApprovalErrorCode.NotFound, () => q.GetInstanceAsync(hidden.Id));
            var result = await sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "charlie", "bob", "handover", "handover");
            Assert.Equal(1, result.Count);
        });
        Assert.Equal(hidden.Id, Assert.Single(await host.TodoAsync("bob")).InstanceId);
    }

    [Fact]
    public async Task BulkGrant_DoesNotOverridePerTaskDenial()
    {
        var permissions = new Permissions { Decide = (r, _) => Task.FromResult(r.Permission != ApprovalPermissions.TaskReassign) };
        await using var host = Host(permissions); await Seed(host);
        var result = await As(host, "bob", sp => sp.GetRequiredService<ApprovalEngine>().ReassignUserAsync("bob", "charlie", "bob", "handover", "handover"));
        Assert.Equal(0, result.Count); Assert.Single(result.Skipped); Assert.Single(await host.TodoAsync("bob"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PermissionFailureOrTimeout_FailsClosed(bool timeout)
    {
        var permissions = new Permissions { Decide = (_, ct) => timeout ? new TaskCompletionSource<bool>().Task.WaitAsync(ct) : Task.FromException<bool>(new HttpRequestException("offline")) };
        await using var host = Host(permissions); await Seed(host);
        var task = Assert.Single(await host.TodoAsync("bob"));
        await AssertCode(ApprovalErrorCode.AuthorizationUnavailable, () => AsCommand(host, "bob", ApprovalPermissions.TaskApprove, sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(task.TaskId, "bob")));
        Assert.Single(await host.TodoAsync("bob"));
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsAuthorizationFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var permissions = new Permissions { Decide = (_, ct) => { cancellation.Cancel(); return new TaskCompletionSource<bool>().Task.WaitAsync(ct); } };
        await using var host = Host(permissions);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => As(host, "bob", sp => sp.GetRequiredService<ApprovalAuthorizationService>().CanAsync(ApprovalPermissions.TaskRead, ct: cancellation.Token)));
    }

    [Fact]
    public async Task UnavailableQueryScope_DoesNotReturnUnfilteredRows()
    {
        var permissions = new Permissions(); await using var host = Host(permissions, customScope: new UnavailableScope()); await Seed(host);
        await AssertCode(ApprovalErrorCode.AuthorizationUnavailable, () => As(host, "bob", sp => sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync("bob")));
        Assert.Single(await host.TodoAsync("bob"));
    }

    [Fact]
    public async Task RestrictedSystemActor_CannotPublishOrImpersonateUser()
    {
        var permissions = new Permissions(); await using var host = Host(permissions);
        await using var scope = host.NewScope(authorize: true); var sp = scope.ServiceProvider;
        sp.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("worker", [ApprovalPermissions.OutboxDispatch]);
        var auth = sp.GetRequiredService<ApprovalAuthorizationService>();
        Assert.True(await auth.CanAsync(ApprovalPermissions.OutboxDispatch));
        Assert.False(await auth.CanAsync(ApprovalPermissions.DefinitionPublish));
        await AssertCode(ApprovalErrorCode.Forbidden, () => auth.RequireAsync(ApprovalPermissions.OutboxDispatch, userId: "alice"));
        Assert.Single(permissions.Calls);
    }

    [Fact]
    public async Task StaffingPreflight_RequiresStartPermissionAndTrustedInitiator()
    {
        var permissions = new Permissions { Decide = (r, _) => Task.FromResult(r.Permission == ApprovalPermissions.InstanceStart) };
        await using var host = Host(permissions); await host.PublishAsync("doc", Flow);
        var request = new StartApprovalRequest("doc", RecordingBusinessHandler.Type, "preflight", "alice");
        var users = await As(host, "alice", sp => sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, "review"));
        Assert.Equal(["bob"], users);
        await AssertCode(ApprovalErrorCode.Forbidden, () => As(host, "bob", sp => sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, "review")));
        permissions.Decide = (_, _) => Task.FromResult(false);
        await AssertCode(ApprovalErrorCode.Forbidden, () => As(host, "alice", sp => sp.GetRequiredService<ApprovalEngine>().GetStartEligibleApproversAsync(request, "review")));
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
    }

    [Fact]
    public async Task DefaultPermissionProvider_DeniesMembersAndAdministrators()
    {
        var provider = new DefaultApprovalPermissionProvider();
        foreach (var actor in new[] { "member", "admin", "designer" })
        foreach (var permission in ApprovalPermissions.All.Where(p => !p.StartsWith("approval.system.")))
            Assert.False(await provider.AuthorizeAsync(new(new(actor, false), permission, new()), default));
    }
}
