using Approval.AspNetCore;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddDbContext<InvoiceDbContext>(o => o.UseSqlite(InvoiceDesignFactory.Connection));
builder.Services.AddSingleton<HostActor>();
builder.Services.AddScoped<IApprovalContextResolver, HostIdentityResolver>();
builder.Services.AddScoped<IApprovalIdentityProvider, HostUsers>();
builder.Services.AddScoped<IApprovalUserStatusProvider, HostUsers>();
builder.Services.AddSingleton<IApprovalPermissionProvider, HostPermissions>();
builder.Services.AddSingleton<HostEvents>();
builder.Services.AddSingleton<IApprovalEventHandler>(sp => sp.GetRequiredService<HostEvents>());
builder.Services.AddApprovalWorkflow<InvoiceDbContext>(o => { o.EnableBackgroundServices = false; o.Outbox.BaseRetryDelay = TimeSpan.FromMilliseconds(1); });
builder.Services.AddApprovalAspNetCore();
await using var app = builder.Build();
var actor = app.Services.GetRequiredService<HostActor>();
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
async Task<T> As<T>(string user, Func<IServiceProvider, Task<T>> operation)
{
    actor.UserId = user;
    await using var scope = app.Services.CreateAsyncScope();
    return await operation(scope.ServiceProvider);
}
Task<T> Command<T>(string user, string kind, string key, object payload, string permission,
    Func<IServiceProvider, CancellationToken, Task<T>> operation, ApprovalResource? resource = null) => As(user, sp =>
        sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(new(kind, "consumer", key, payload, permission, resource), operation));
Task Act(string user, string kind, string key, string permission, ApprovalResource resource, Func<ApprovalEngine, CancellationToken, Task> operation) =>
    Command(user, kind, key, new { key }, permission, async (sp, ct) => { await operation(sp.GetRequiredService<ApprovalEngine>(), ct); return true; }, resource);
async Task<ApprovalTask> Pending(string user, Guid instance) => await As(user, async sp =>
{
    var todo = await sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync(user);
    var taskId = todo.Items.Single(t => t.InstanceId == instance).TaskId;
    return await sp.GetRequiredService<InvoiceDbContext>().Set<ApprovalTask>().SingleAsync(t => t.Id == taskId);
});
await As("alice", async sp =>
{
    var db = sp.GetRequiredService<InvoiceDbContext>(); await db.Database.MigrateAsync();
    foreach (var user in new[] { "alice", "bob", "carol", "dave" }) db.Users.Add(new HostUser { Id = user, UserName = user });
    await db.SaveChangesAsync();
    Check(await db.Users.CountAsync() == 4, "ExistingIdentityDbContext");
    Check(!db.Model.GetEntityTypes().Any(e => e.GetTableName() is "ApprovalFiles" or "ApprovalDrafts" or "ApprovalArchives"), "CoreOnlyModel");
    return true;
});
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
foreach (var (key, json) in new[] { ("invoice", flow), ("parallel", parallel) })
    await Command("alice", "publish", "publish-" + key, json, ApprovalPermissions.DefinitionPublish,
        (sp, ct) => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync(key, key, json, ct));
var invoiceId = Guid.NewGuid();
Task<Guid> Submit() => Command("alice", "invoice.submit", "invoice-1", new { invoiceId, amount = 42 }, ApprovalPermissions.InstanceStart,
    async (sp, ct) =>
    {
        sp.GetRequiredService<InvoiceDbContext>().Invoices.Add(new Invoice { Id = invoiceId, Amount = 42 });
        return (await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("invoice", "Invoice", invoiceId.ToString(), "alice") { FormData = new { amount = 42 } }, ct)).Id;
    });
var first = await Submit(); Check(first == await Submit(), "StableOperationReplay");
await As("alice", async sp => { var db = sp.GetRequiredService<InvoiceDbContext>(); Check(await db.Invoices.CountAsync() == 1 && await db.Invoices.Select(i => i.SavedByHost).SingleAsync() == "invoice-host", "SharedTransactionAndSaveOverride"); return true; });
async Task RejectHostWrite(string name, Func<InvoiceDbContext, Task> write)
{
    try { await As("alice", async sp => { await write(sp.GetRequiredService<InvoiceDbContext>()); return true; }); }
    catch (InvalidOperationException) { Check(true, name); return; }
    throw new InvalidOperationException("Host write was accepted: " + name);
}
await RejectHostWrite("HostTaskStateWriteRejected", async db =>
{ (await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == first)).Status = ApprovalTaskStatus.Approved; await db.SaveChangesAsync(); });
await RejectHostWrite("HostInstanceStateWriteRejected", async db =>
{ (await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == first)).Status = InstanceStatus.Approved; await db.SaveChangesAsync(); });
await RejectHostWrite("HostLogDeleteRejected", async db =>
{ db.Remove(await db.Set<ApprovalLog>().FirstAsync(l => l.InstanceId == first)); await db.SaveChangesAsync(); });
await RejectHostWrite("HostOperationWriteRejected", async db =>
{ (await db.Set<ApprovalOperation>().FirstAsync()).ResultJson = "forged"; await db.SaveChangesAsync(); });
await RejectHostWrite("HostBulkTaskWriteRejected", async db =>
{ await db.Set<ApprovalTask>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ApprovalTaskStatus.Approved)); });
await RejectHostWrite("HostBulkInstanceDeleteRejected", async db => { await db.Set<ApprovalInstance>().ExecuteDeleteAsync(); });
await As("alice", async sp =>
{
    var db = sp.GetRequiredService<InvoiceDbContext>();
    Check((await db.Set<ApprovalInstance>().SingleAsync(i => i.Id == first)).Status == InstanceStatus.Running
        && (await db.Set<ApprovalTask>().SingleAsync(t => t.InstanceId == first)).Status == ApprovalTaskStatus.Pending,
        "RejectedHostWritesPreserveDatabaseState");
    var invoice = await db.Invoices.SingleAsync(); invoice.Amount = 99; await db.SaveChangesAsync();
    Check((await db.Invoices.AsNoTracking().SingleAsync()).Amount == 99, "OrdinaryHostBusinessUpdatesRemainAllowed");
    return true;
});
var review = await Pending("bob", first);
await Act("bob", "addsign", "before", ApprovalPermissions.TaskAddSign, new() { TaskId = review.Id }, (e, ct) => e.AddSignAsync(review.Id, "bob", ["dave"], AddSignMode.Before, ct: ct));
var sign = await Pending("dave", first);
await Act("dave", "approve", "sign", ApprovalPermissions.TaskApprove, new() { TaskId = sign.Id }, (e, ct) => e.ApproveAsync(sign.Id, "dave", ct: ct));
review = await Pending("bob", first);
await Act("bob", "return", "return", ApprovalPermissions.TaskReturn, new() { TaskId = review.Id }, (e, ct) => e.ReturnAsync(review.Id, "bob", "amend", "start", ct));
var amend = await Pending("alice", first);
await Act("alice", "resubmit", "resubmit", ApprovalPermissions.TaskResubmit, new() { TaskId = amend.Id }, (e, ct) => e.ResubmitAsync(amend.Id, "alice", "resubmit", new { amount = 43 }, ct));
review = await Pending("bob", first);
await Act("bob", "approve", "final", ApprovalPermissions.TaskApprove, new() { TaskId = review.Id }, (e, ct) => e.ApproveAsync(review.Id, "bob", ct: ct));
await As("alice", async sp => { var detail = await sp.GetRequiredService<ApprovalQueryService>().GetInstanceForViewerAsync(first, new("alice", false)); Check(detail?.Status == InstanceStatus.Approved && detail.Logs.Any(l => l.Action == ApprovalAction.AddSign) && detail.Logs.Any(l => l.Action == ApprovalAction.Return) && detail.Logs.Any(l => l.Action == ApprovalAction.Resubmit), "AddSignReturnResubmitWithoutForms"); return true; });
var parallelId = await Command("alice", "start", "parallel", new { }, ApprovalPermissions.InstanceStart,
    async (sp, ct) => (await sp.GetRequiredService<ApprovalEngine>().StartAsync(new("parallel", "Business", "parallel", "alice"), ct)).Id);
var a = await Pending("bob", parallelId); var b = await Pending("carol", parallelId);
await Act("bob", "approve", "branch-a", ApprovalPermissions.TaskApprove, new() { TaskId = a.Id }, (e, ct) => e.ApproveAsync(a.Id, "bob", ct: ct));
await As("alice", async sp => { Check((await sp.GetRequiredService<InvoiceDbContext>().Set<ApprovalInstance>().SingleAsync(i => i.Id == parallelId)).Status == InstanceStatus.Running, "JoinWaitsForOtherBranch"); return true; });
await Act("carol", "approve", "branch-b", ApprovalPermissions.TaskApprove, new() { TaskId = b.Id }, (e, ct) => e.ApproveAsync(b.Id, "carol", ct: ct));
await As("alice", async sp => { Check((await sp.GetRequiredService<InvoiceDbContext>().Set<ApprovalInstance>().SingleAsync(i => i.Id == parallelId)).Status == InstanceStatus.Approved, "ParallelJoinWithoutForms"); return true; });
for (var attempt = 0; attempt < 5; attempt++)
{ await app.Services.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync(); await Task.Delay(10); }
var events = app.Services.GetRequiredService<HostEvents>();
Check(events.Attempts.Count(e => e.Id == events.Attempts[0].Id) == 2, "StableEventIdOnRetry");
Check(events.Attempts.Any(e => e.EventType == "Completed" && e.InstanceId == first), "HostEventHandler");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("Approval.Forms", StringComparison.Ordinal) == true), "NoFormsAssemblyLoaded");
var evidence = System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks });
if (Environment.GetEnvironmentVariable("APPROVAL_CONSUMER_REPORT") is { } report)
    await File.WriteAllTextAsync(report, evidence);
Console.WriteLine(evidence);

public sealed class HostUser : IdentityUser { }
public sealed class Invoice { public Guid Id { get; set; } public decimal Amount { get; set; } public string? SavedByHost { get; set; } }
public sealed class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : IdentityDbContext<HostUser>(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    protected override void OnModelCreating(ModelBuilder model) { base.OnModelCreating(model); model.ConfigureApproval(); model.Entity<Invoice>().HasKey(i => i.Id); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { foreach (var entry in ChangeTracker.Entries<Invoice>().Where(e => e.State == EntityState.Added)) entry.Entity.SavedByHost = "invoice-host"; return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
}
public sealed class InvoiceDesignFactory : IDesignTimeDbContextFactory<InvoiceDbContext>
{
    public static string Connection => "Data Source=" + (Environment.GetEnvironmentVariable("APPROVAL_CONSUMER_DATABASE") ?? "consumer.db");
    public InvoiceDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(Connection).UseApproval().Options);
}
public sealed class HostActor { public string UserId { get; set; } = "alice"; }
public sealed class HostIdentityResolver(HostActor actor) : IApprovalContextResolver
{ public ApprovalExecutionIdentity Resolve() => new(actor.UserId); }
public sealed class HostUsers(InvoiceDbContext db) : IApprovalIdentityProvider, IApprovalUserStatusProvider
{
    public Task<bool> IsActiveAsync(string user, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == user, ct);
    public Task<bool> IsInRoleAsync(string user, string role, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> GetRolesAsync(string user, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
public sealed class HostPermissions : IApprovalPermissionProvider
{
    public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(request.Subject.IsSystem || request.Subject.ActorId is "alice" or "bob" or "carol" or "dave");
}
public sealed class HostEvents : IApprovalEventHandler
{
    public List<ApprovalEvent> Attempts { get; } = [];
    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct)
    { Attempts.Add(approvalEvent); if (Attempts.Count == 1) throw new IOException("retry host event"); return Task.CompletedTask; }
}
