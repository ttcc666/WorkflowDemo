using System.Data.Common;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var services = new ServiceCollection();
services.AddLogging();
var faults = new CommitFault();
services.AddDbContext<HostDb>(o => o.UseSqlite(connection).AddInterceptors(faults, new VerifyFault(faults)));
var approval = services.AddApprovalWorkflow<HostDb>(o =>
{
    o.EnableBackgroundServices = false;
    o.Outbox.BaseRetryDelay = TimeSpan.FromMinutes(1);
}).AddQueryScope<HostQueryScope>().AddApprovalTesting(o => o.InitialUtcTime = DateTimeOffset.UnixEpoch);
services.AddSingleton<HostEffects>();
approval.AddEventHandler<HostHandler>();
await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

var actor = host.GetRequiredService<ApprovalTestActor>();
var users = host.GetRequiredService<ApprovalTestUsers>();
var permissions = host.GetRequiredService<ApprovalTestPermissions>();
var events = host.GetRequiredService<ApprovalTestEvents>();
var clock = host.GetRequiredService<FakeTimeProvider>();
var checks = new List<string>();

void Check(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException(scenario);
    checks.Add(scenario);
}

async Task<T> As<T>(string user, Func<IServiceProvider, Task<T>> run)
{
    using var identity = actor.Use(user);
    await using var scope = host.CreateAsyncScope();
    return await run(scope.ServiceProvider);
}

Task<T> Command<T>(string user, string kind, string key, object payload, string permission,
    Func<IServiceProvider, CancellationToken, Task<T>> run, ApprovalResource? resource = null) => As(user, sp =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new(kind, "testing-host", key, payload, permission, resource), run));

Task Act(string user, string key, string permission, ApprovalResource resource, Func<ApprovalEngine, CancellationToken, Task> run) =>
    Command(user, key, key, resource, permission, async (sp, ct) =>
    {
        await run(sp.GetRequiredService<ApprovalEngine>(), ct);
        return true;
    }, resource);

Task<T> Read<T>(Func<HostDb, Task<T>> read) => As("alice", sp => read(sp.GetRequiredService<HostDb>()));
Task<Guid> Pending(Guid instance, string user) => Read(db => db.Set<ApprovalTask>()
    .Where(task => task.InstanceId == instance && task.AssigneeValue == user && task.Status == ApprovalTaskStatus.Pending)
    .Select(task => task.Id).SingleAsync());
Task<InstanceStatus> Status(Guid id) => Read(db => db.Set<ApprovalInstance>().Where(instance => instance.Id == id).Select(i => i.Status).SingleAsync());

async Task ExpectApproval(Task operation, ApprovalErrorCode? expected = null)
{
    try { await operation; }
    catch (ApprovalException error) when (expected is null || error.Code == expected) { return; }
    throw new InvalidOperationException($"Expected an approval error ({expected}).");
}

await Read(db => db.Database.EnsureCreatedAsync());
users.SetUser("alice", "Alice");
users.SetUser("bob", "Bob", ["reviewers"]);
users.SetUser("carol", "Carol", ["reviewers"]);
users.SetUser("dave", "Dave");
users.SetUser("auditor", "Auditor");
Check((await users.SearchUsersAsync("car", 10, default)).Single().Name == "Carol"
    && (await users.GetUserIdsInRoleAsync("reviewers", default)).SequenceEqual(["bob", "carol"]), "ConfiguredDirectoryAndRoles");

var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var firstActor = As("alice", async sp =>
{
    entered.SetResult();
    await release.Task;
    return sp.GetRequiredService<IApprovalActorContext>().ActorId;
});
await entered.Task;
var secondActor = await As("bob", sp => Task.FromResult(sp.GetRequiredService<IApprovalActorContext>().ActorId));
release.SetResult();
Check(await firstActor == "alice" && secondActor == "bob" && actor.Resolve().ActorId is null, "ConcurrentActorsAreIsolated");
using (actor.Use("alice"))
{
    using (actor.Use("bob")) Check(actor.Resolve().ActorId == "bob", "NestedActorOverridesOuterActor");
    Check(actor.Resolve().ActorId == "alice", "NestedActorRestoresOuterActor");
}
await using (var other = new ApprovalBuilder(new ServiceCollection()).AddApprovalTesting().Services.BuildServiceProvider())
{
    Check(other.GetRequiredService<IApprovalEventHandler>() is ApprovalTestEvents
        && !(await other.GetRequiredService<IApprovalUserStatusProvider>().IsActiveAsync("alice", default)), "IndependentContainerStartsWithEmptyDoubles");
}

const string flow = """
    {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
    "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
    """;
const string parallel = """
    {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"fork"},
    "fork":{"type":"Parallel","branches":[{"next":"a"},{"next":"b"}]},
    "a":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"join"},
    "b":{"type":"Approval","approvers":[{"type":"User","value":"carol"}],"next":"join"},
    "join":{"type":"Join","next":"end"},"end":{"type":"End"}}}
    """;

Task Publish(string key, string json) => Command("alice", "publish", key, new { key, json }, ApprovalPermissions.DefinitionPublish,
    (sp, ct) => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync(key, key, json, ct), new() { DefinitionKey = key });

await ExpectApproval(Publish("review", flow), ApprovalErrorCode.Forbidden);
Check(await Read(db => db.Set<ApprovalDefinition>().CountAsync()) == 0, "PermissionsDenyByDefault");
foreach (var permission in new[] { ApprovalPermissions.DefinitionPublish, ApprovalPermissions.InstanceStart, ApprovalPermissions.InstanceRead,
    ApprovalPermissions.InstanceWithdraw, ApprovalPermissions.TaskResubmit }) permissions.Grant("alice", permission);
foreach (var permission in new[] { ApprovalPermissions.InstanceRead, ApprovalPermissions.TaskReject,
    ApprovalPermissions.TaskReturn, ApprovalPermissions.TaskTransfer, ApprovalPermissions.TaskAddSign }) permissions.Grant("bob", permission);
permissions.Grant("carol", ApprovalPermissions.TaskApprove);
permissions.Grant("dave", ApprovalPermissions.TaskApprove);
permissions.Grant("auditor", ApprovalPermissions.InstanceReadAll);
permissions.Grant("approval-background", ApprovalPermissions.OutboxDispatch);
await Publish("review", flow);
await Publish("parallel", parallel);

Task<Guid> Submit(string businessKey, string flowKey = "review", bool throwAfterStart = false) =>
    Command("alice", "submit", businessKey, new { businessKey, flowKey, throwAfterStart }, ApprovalPermissions.InstanceStart,
        async (sp, ct) =>
        {
            var db = sp.GetRequiredService<HostDb>();
            db.Invoices.Add(new Invoice { Key = businessKey });
            var instance = await sp.GetRequiredService<ApprovalEngine>().StartAsync(new(flowKey, "Invoice", businessKey, "alice"), ct);
            if (throwAfterStart) throw new IOException("host callback failed");
            return instance.Id;
        }, new() { DefinitionKey = flowKey });

var first = await Submit("visible");
Check(first == await Submit("visible") && await Read(db => db.Invoices.CountAsync()) == 1, "SharedCommitAndStableReplay");
await ExpectApproval(Submit("approval-rollback", "missing"));
try { await Submit("host-rollback", throwAfterStart: true); throw new InvalidOperationException("Expected host failure."); }
catch (IOException) { }
Check(await Read(async db => await db.Invoices.CountAsync() == 1 && await db.Set<ApprovalInstance>().CountAsync() == 1
    && await db.Set<ApprovalOperation>().CountAsync(o => o.Kind == "submit") == 1), "ApprovalFailureAndHostFailureRollBackTheSharedTransaction");

faults.Enabled = true;
await ExpectApproval(Submit("unknown"), ApprovalErrorCode.CommitOutcomeUnknown);
faults.BlockVerification = false;
var recovered = await Submit("unknown");
Check(await Read(async db => await db.Invoices.CountAsync(i => i.Key == "unknown") == 1
    && await db.Set<ApprovalInstance>().CountAsync(i => i.BusinessKey == "unknown" && i.Id == recovered) == 1), "LostCommitAcknowledgementRecoversWithOriginalKey");
var visible = await As("auditor", sp => sp.GetRequiredService<ApprovalQueryService>().SearchInstancesAsync(page: new(1, 1)));
Check(visible.Total == 1 && visible.Items.Single().Id == first, "HostQueryScopeFiltersBeforeCountAndPaging");

var task = await Pending(first, "bob");
Task Approve(string user, Guid id, string key) => Act(user, key, ApprovalPermissions.TaskApprove,
    new() { TaskId = id }, (engine, ct) => engine.ApproveAsync(id, user, ct: ct));
await ExpectApproval(Approve("bob", task, "deny"), ApprovalErrorCode.Forbidden);
permissions.Grant("bob", ApprovalPermissions.TaskApprove, resource => resource.TaskId == task);
await ExpectApproval(Approve("bob", await Pending(recovered, "bob"), "other-resource"), ApprovalErrorCode.Forbidden);
users.SetActive("bob", false);
await ExpectApproval(Approve("bob", task, "inactive"));
users.SetActive("bob", true);
permissions.Failure = new IOException("directory permissions offline");
await ExpectApproval(Approve("bob", task, "unavailable"), ApprovalErrorCode.AuthorizationUnavailable);
permissions.Failure = null;
Check(await Status(first) == InstanceStatus.Running, "DeniedInactiveAndUnavailableApprovalsDoNotChangeState");

await Act("bob", "transfer", ApprovalPermissions.TaskTransfer, new() { TaskId = task }, (e, ct) => e.TransferAsync(task, "bob", "carol", ct: ct));
await Approve("carol", await Pending(first, "carol"), "transferred-approve");
Check(await Status(first) == InstanceStatus.Approved, "TransferAndApproveAsNewAssignee");
permissions.Revoke("bob", ApprovalPermissions.TaskApprove);
var collab = await Submit("collaboration");
task = await Pending(collab, "bob");
await Act("bob", "return", ApprovalPermissions.TaskReturn, new() { TaskId = task }, (e, ct) => e.ReturnAsync(task, "bob", "amend", "start", ct));
Check(await Status(collab) == InstanceStatus.Returned, "ReturnToInitiator");
var returned = await Pending(collab, "alice");
await Act("alice", "resubmit", ApprovalPermissions.TaskResubmit, new() { TaskId = returned }, (e, ct) => e.ResubmitAsync(returned, "alice", ct: ct));
task = await Pending(collab, "bob");
await Act("bob", "add-sign", ApprovalPermissions.TaskAddSign, new() { TaskId = task }, (e, ct) => e.AddSignAsync(task, "bob", ["dave"], AddSignMode.Before, ct: ct));
await Approve("dave", await Pending(collab, "dave"), "sign-approve");
await ExpectApproval(Approve("bob", task, "revoked"), ApprovalErrorCode.Forbidden);
permissions.Grant("bob", ApprovalPermissions.TaskApprove);
await Approve("bob", task, "original-approve");
Check(await Status(collab) == InstanceStatus.Approved, "ResubmitAddSignAndComplete");

task = await Pending(recovered, "bob");
await Act("bob", "reject", ApprovalPermissions.TaskReject, new() { TaskId = task }, (e, ct) => e.RejectAsync(task, "bob", "declined", ct));
Check(await Status(recovered) == InstanceStatus.Rejected, "RejectTerminatesInstance");
var withdraw = await Submit("withdraw");
await Act("alice", "withdraw", ApprovalPermissions.InstanceWithdraw, new() { InstanceId = withdraw }, (e, ct) => e.WithdrawAsync(withdraw, "alice", ct: ct));
Check(await Status(withdraw) == InstanceStatus.Withdrawn, "InitiatorWithdraws");

var joined = await Submit("join", "parallel");
await Approve("bob", await Pending(joined, "bob"), "first-branch");
Check(await Status(joined) == InstanceStatus.Running, "JoinWaitsForSecondBranch");
await Approve("carol", await Pending(joined, "carol"), "second-branch");
Check(await Status(joined) == InstanceStatus.Approved, "JoinCompletesAfterBothBranches");

Check(events.Snapshot().Count == 0 && await Read(db => db.Set<ApprovalOutboxMessage>().AnyAsync()), "EventsPersistInOutboxBeforeDelivery");
events.FailNext("Completed");
var dispatcher = host.GetRequiredService<ApprovalOutboxDispatcher>();
await dispatcher.DispatchOnceAsync();
var failedId = await Read(db => db.Set<ApprovalOutboxMessage>().Where(m => m.Status == OutboxStatus.Pending && m.Attempts == 1).Select(m => m.Id).SingleAsync());
await dispatcher.DispatchOnceAsync();
Check(events.Snapshot().Count(e => e.Id == failedId) == 1, "NoOutboxRetryBeforeDueTime");
clock.Advance(TimeSpan.FromMinutes(2));
await dispatcher.DispatchOnceAsync();
var attempts = events.Snapshot().Where(e => e.Id == failedId).ToArray();
Check(attempts.Length == 2 && attempts.All(e => e.EventType == "Completed" && e.InstanceId == attempts[0].InstanceId)
    && await Read(db => db.Set<ApprovalOutboxMessage>().AnyAsync(m => m.Id == failedId && m.Status == OutboxStatus.Succeeded)), "OutboxRetriesWithStableEventIdentity");
Check(host.GetRequiredService<HostEffects>().Applied.Count == events.Snapshot().Select(e => e.Id).Distinct().Count(), "HostDeduplicatesRepeatedDeliveryByEventId");

var report = JsonSerializer.Serialize(new { passed = true, checks });
if (Environment.GetEnvironmentVariable("APPROVAL_TESTING_CONSUMER_REPORT") is { } path) await File.WriteAllTextAsync(path, report);
Console.WriteLine(report);

sealed class HostDb(DbContextOptions<HostDb> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureApproval();
        model.Entity<Invoice>().HasKey(invoice => invoice.Key);
    }
}
sealed class Invoice { public required string Key { get; init; } }

sealed class HostQueryScope : IApprovalQueryScope
{
    public Task<IQueryable<ApprovalInstance>> ApplyAsync(ApprovalPermissionSubject subject, IQueryable<ApprovalInstance> instances, CancellationToken ct) =>
        Task.FromResult(subject.ActorId == "auditor" ? instances.Where(i => i.BusinessKey == "visible") : instances);
}

sealed class HostEffects { public HashSet<Guid> Applied { get; } = []; }

sealed class HostHandler(ApprovalTestEvents recorder, HostEffects effects) : IApprovalEventHandler
{
    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    {
        // Example of a host sink deduplicating external work after the previous call lost its acknowledgement.
        effects.Applied.Add(approvalEvent.Id);
        return recorder.HandleAsync(approvalEvent, ct);
    }
}

sealed class CommitFault : DbTransactionInterceptor
{
    public bool Enabled { get; set; }
    public bool BlockVerification { get; set; }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return Task.CompletedTask;
        Enabled = false;
        BlockVerification = true;
        throw new IOException("commit acknowledgement lost");
    }
}

sealed class VerifyFault(CommitFault fault) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (fault.BlockVerification && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal))
            throw new IOException("verification unavailable");
        return ValueTask.FromResult(result);
    }
}
