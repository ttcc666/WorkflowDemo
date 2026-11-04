using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Approval.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Core.Tests;

public sealed class ApprovalWriteProtectionTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
        "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
        """;

    private static async Task<(TestHost Host, Guid Instance)> CreateAsync()
    {
        var host = new TestHost(); host.Identity.Add("alice"); host.Identity.Add("bob");
        await host.PublishAsync("protected", Flow);
        return (host, (await host.StartAsync("protected", "one", "alice")).Id);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("instance")]
    [InlineData("log-delete")]
    [InlineData("log-add")]
    [InlineData("definition-delete")]
    [InlineData("management")]
    [InlineData("slot-delete")]
    [InlineData("outbox")]
    [InlineData("operation-add")]
    public async Task HostTrackedWrites_AreRejected_AndDatabaseRemainsUnchanged(string scenario)
    {
        var (host, instanceId) = await CreateAsync(); await using var lifetime = host;
        var before = await host.GetInstanceAsync(instanceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            switch (scenario)
            {
                case "task": (await db.Set<ApprovalTask>().FirstAsync()).Status = ApprovalTaskStatus.Approved; break;
                case "instance": (await db.Set<ApprovalInstance>().FirstAsync()).Status = InstanceStatus.Approved; break;
                case "log-delete": db.Remove(await db.Set<ApprovalLog>().FirstAsync()); break;
                case "log-add": db.Add(new ApprovalLog { Id = Guid.NewGuid(), InstanceId = instanceId, Sequence = 999, OperatorId = "forged", Action = ApprovalAction.Approve }); break;
                case "definition-delete": db.Remove(await db.Set<ApprovalDefinition>().FirstAsync()); break;
                case "management": (await db.Set<ApprovalDefinitionManagement>().FirstAsync()).IsEnabled = false; break;
                case "slot-delete": db.Remove(await db.Set<ApprovalBusinessSlot>().FirstAsync()); break;
                case "outbox": (await db.Set<ApprovalOutboxMessage>().FirstAsync()).Status = OutboxStatus.Succeeded; break;
                case "operation-add": db.Add(new ApprovalOperation { Id = Guid.NewGuid(), ActorId = "forged", Kind = "forged", Scope = "forged", RequestKey = "forged", LookupHash = new string('a', 64), RequestHash = new string('b', 64), ResultJson = "{}" }); break;
            }
            await db.SaveChangesAsync();
        }));
        var after = await host.GetInstanceAsync(instanceId);
        Assert.Equal(InstanceStatus.Running, after.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(after.Tasks).Status);
        Assert.Equal(before.Logs.Select(l => l.Id), after.Logs.Select(l => l.Id));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.True((await db.Set<ApprovalDefinitionManagement>().SingleAsync()).IsEnabled);
            Assert.Equal(1, await db.Set<ApprovalDefinition>().CountAsync());
            Assert.Equal(1, await db.Set<ApprovalBusinessSlot>().CountAsync());
            Assert.Equal(0, await db.Set<ApprovalOperation>().CountAsync());
            Assert.All(await db.Set<ApprovalOutboxMessage>().ToListAsync(), m => Assert.Equal(OutboxStatus.Pending, m.Status));
        });
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("projected-update")]
    [InlineData("anonymous-update")]
    public async Task HostBulkWrites_AreRejected_BeforeExecutingSql(string scenario)
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<TestDbContext>();
                if (scenario == "delete") await db.Set<ApprovalInstance>().Where(i => i.Id == id).ExecuteDeleteAsync();
                else if (scenario == "anonymous-update") await db.Set<ApprovalTask>().Select(t => new { Task = t }).ExecuteUpdateAsync(s => s.SetProperty(t => t.Task.Status, ApprovalTaskStatus.Approved));
                else if (scenario == "projected-update") await db.Set<ApprovalInstance>().SelectMany(i => i.Tasks).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ApprovalTaskStatus.Approved));
                else await db.Set<ApprovalTask>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ApprovalTaskStatus.Approved));
            }));
            Assert.Contains("审批", error.Message);
        }
        var instance = await host.GetInstanceAsync(id);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(instance.Tasks).Status);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("inside-unit")]
    [InlineData("after-engine")]
    public async Task PublicUnitOfWork_DoesNotAuthorizeHostApprovalChanges(string point)
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            var task = await db.Set<ApprovalTask>().SingleAsync();
            if (point == "before") task.Comment = "forged";
            await sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
            {
                if (point == "inside-unit") task.Status = ApprovalTaskStatus.Approved;
                else
                {
                    await sp.GetRequiredService<Approval.Core.Engine.ApprovalEngine>().ApproveAsync(task.Id, "bob", "real", ct);
                    if (point == "after-engine") task.Comment = "forged";
                }
            });
        }));
        var instance = await host.GetInstanceAsync(id);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(instance.Tasks).Status);
        Assert.Null(Assert.Single(instance.Tasks).Comment);
    }

    [Fact]
    public async Task BusinessSaveAndBulkWithApprovalReadFilter_ContinueToWork()
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Add(new TestRecord { Id = id, Name = "business" }); await db.SaveChangesAsync();
            Assert.Equal(1, await db.Set<TestRecord>().Where(b => db.Set<ApprovalInstance>().Any(i => i.Id == b.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "updated")));
        });
        await host.RunAsync(async sp => Assert.Equal("updated", await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().Select(b => b.Name).SingleAsync()));
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("interpolated")]
    [InlineData("reader")]
    [InlineData("sync")]
    public async Task HostSqlDml_IsRejected_InEveryExecutionPath(string path)
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            if (path == "raw") await db.Database.ExecuteSqlRawAsync("UPDATE /* guarded */ \"ApprovalTasks\" SET \"Status\" = 2");
            else if (path == "interpolated") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ApprovalTasks\" SET \"Status\" = {2}");
            else if (path == "reader") await db.Database.SqlQueryRaw<int>("UPDATE \"ApprovalTasks\" SET \"Status\" = 2 RETURNING \"Status\" AS \"Value\"").ToListAsync();
            else db.Database.ExecuteSqlRaw("DELETE FROM \"ApprovalInstances\"");
        }));
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single((await host.GetInstanceAsync(id)).Tasks).Status);
    }

    [Fact]
    public async Task BusinessRawSql_MayMentionAndReadApprovalTables()
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); db.Add(new TestRecord { Id = id, Name = "original" }); await db.SaveChangesAsync();
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync("UPDATE \"TestRecord\" SET \"Name\" = 'ApprovalTasks' WHERE EXISTS (SELECT 1 FROM \"ApprovalTasks\") /* ApprovalInstances */"));
        });
        await host.RunAsync(async sp => Assert.Equal("ApprovalTasks", await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().Select(r => r.Name).SingleAsync()));
    }

    [Fact]
    public async Task ForgedUnchangedAttachment_CannotChangeEngineAssigneeValidation()
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        var real = Assert.Single((await host.GetInstanceAsync(id)).Tasks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            db.Attach(new ApprovalTask { Id = real.Id, InstanceId = id, NodeId = real.NodeId, NodeName = real.NodeName,
                AssigneeKind = AssigneeKind.User, AssigneeValue = "mallory", Status = ApprovalTaskStatus.Pending });
            await sp.GetRequiredService<Approval.Core.Engine.ApprovalEngine>().ApproveAsync(real.Id, "mallory", "forged");
        }));
        Assert.Equal("bob", Assert.Single((await host.GetInstanceAsync(id)).Tasks).AssigneeValue);
    }

    [Fact]
    public async Task PublicDataEditCallback_CannotChangeTaskStateOrPersistedHistory()
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        var taskId = Assert.Single((await host.GetInstanceAsync(id)).Tasks).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(sp =>
            sp.GetRequiredService<Approval.Core.Engine.ApprovalEngine>().ApproveWithDataAsync(taskId, "bob", "real",
                (_, task, _) =>
                {
                    sp.GetRequiredService<TestDbContext>().Set<ApprovalTask>().Local.Single(t => t.Id == task.Id).AssigneeValue = "mallory";
                    return Task.FromResult<Approval.Core.Engine.ApprovalDataEdit?>(null);
                }, default)));
        var instance = await host.GetInstanceAsync(id);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal("bob", Assert.Single(instance.Tasks).AssigneeValue);
    }

    private sealed class LateMutation : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled) data.Context!.ChangeTracker.Entries<ApprovalTask>().Single().Entity.Status = ApprovalTaskStatus.Approved;
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task HostInterceptorAfterSaveGuard_CannotIntroduceApprovalChanges()
    {
        var mutation = new LateMutation();
        await using var host = new TestHost(build: b => b.Services.AddDbContext<TestDbContext>(o => o.UseApproval().AddInterceptors(mutation)));
        await host.PublishAsync("protected", Flow); var instance = await host.StartAsync("protected", "one", "alice");
        mutation.Enabled = true;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); await db.Set<ApprovalTask>().SingleAsync();
            db.Add(new TestRecord { Id = Guid.NewGuid(), Name = "business" }); await db.SaveChangesAsync();
        }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("保存校验之后", error.InnerException!.Message);
        mutation.Enabled = false;
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks).Status);
        await host.RunAsync(async sp => Assert.Equal(0, await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().CountAsync()));
    }

    [Theory]
    [InlineData("result")]
    [InlineData("hash")]
    [InlineData("delete")]
    public async Task HostCannotAlterCommittedOperation_AndOriginalKeyStillReplays(string mode)
    {
        var (host, _) = await CreateAsync(); await using var lifetime = host;
        var command = new ApprovalCommand("probe", "probe", "stable", new { amount = 1 }, ApprovalPermissions.InstanceStart);
        await host.RunAsAsync("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(command, (_, _) => Task.FromResult(7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>(); var row = await db.Set<ApprovalOperation>().SingleAsync();
            if (mode == "delete") db.Remove(row); else if (mode == "hash") row.RequestHash = "forged"; else row.ResultJson = "forged";
            await db.SaveChangesAsync();
        }));
        Assert.Equal(7, await host.RunAsAsync("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync<int>(command,
            (_, _) => throw new InvalidOperationException("replay must not execute"))));
        await host.RunAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>().CountAsync()));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("file")]
    public async Task HostCannotModifyFormsData_WhileNormalFormsServicesWork(string resource)
    {
        var store = new InMemoryFileStore();
        await using var host = new TestHost(build: b => { b.AddApprovalForms(); b.Services.AddSingleton<Approval.Forms.Attachments.IApprovalFileStore>(store); });
        const string form = """
            {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},"form":{"fields":[]},"nodes":{
            "start":{"type":"Start","next":"review"},"review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
            """;
        await host.PublishAsync("form", form);
        using var data = System.Text.Json.JsonDocument.Parse("{}");
        var draft = await host.RunAsync(sp => sp.GetRequiredService<Approval.Forms.Runtime.ApprovalFormService>().SaveDraftAsync("alice", "form", null, data.RootElement, "original"));
        using var content = new MemoryStream([1, 2, 3]);
        var file = await host.RunAsync(sp => sp.GetRequiredService<Approval.Forms.Attachments.ApprovalFileService>().UploadAsync("alice", "sample.pdf", content));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            if (resource == "draft") (await db.Set<Approval.Forms.Runtime.ApprovalDraft>().SingleAsync()).Comment = "forged";
            else (await db.Set<Approval.Forms.Attachments.ApprovalFile>().SingleAsync()).UploadedBy = "mallory";
            await db.SaveChangesAsync();
        }));
        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<TestDbContext>();
            Assert.Equal("original", (await db.Set<Approval.Forms.Runtime.ApprovalDraft>().SingleAsync(d => d.Id == draft.Id)).Comment);
            Assert.Equal("alice", (await db.Set<Approval.Forms.Attachments.ApprovalFile>().SingleAsync(f => f.Id == file.Id)).UploadedBy);
        });
        Assert.Single(store.Files);
    }

    private sealed class MutatingResult(TestDbContext db)
    {
        public int Value { get { db.Set<ApprovalOperation>().First(o => o.Kind == "probe").ResultJson = "forged"; return 1; } }
    }
    private sealed class MutatingInput(TestDbContext db)
    {
        public int Value { get { db.Set<ApprovalTask>().Single().Status = ApprovalTaskStatus.Approved; return 1; } }
    }
    [Fact]
    public async Task ResultGetter_CannotInheritOperationWriteAuthority()
    {
        var (host, _) = await CreateAsync(); await using var lifetime = host;
        var original = new ApprovalCommand("probe", "probe", "stable", null, ApprovalPermissions.InstanceStart);
        await host.RunAsAsync("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(original, (_, _) => Task.FromResult(7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsAsync("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>()
            .ExecuteAsync(new("forgery", "probe", "new", null, ApprovalPermissions.InstanceStart),
                (attempt, _) => Task.FromResult(new MutatingResult(attempt.GetRequiredService<TestDbContext>())))));
        Assert.Equal(7, await host.RunAsAsync("alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync<int>(original, (_, _) => throw new InvalidOperationException("must replay"))));
        await host.RunAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<TestDbContext>().Set<ApprovalOperation>().CountAsync()));
    }
    [Fact]
    public async Task InputGetter_CannotInheritEngineWriteAuthority()
    {
        var (host, id) = await CreateAsync(); await using var lifetime = host;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(sp =>
            sp.GetRequiredService<Approval.Core.Engine.ApprovalEngine>().StartAsync(new("protected", "business", "second", "alice")
            { FormData = new MutatingInput(sp.GetRequiredService<TestDbContext>()) })));
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single((await host.GetInstanceAsync(id)).Tasks).Status);
        await host.RunAsync(async sp => Assert.Equal(1, await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().CountAsync()));
    }
}
