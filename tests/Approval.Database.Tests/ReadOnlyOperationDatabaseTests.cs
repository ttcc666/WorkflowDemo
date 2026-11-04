using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class ReadOnlyStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new NonRetryingExecutionStrategy(dependencies);
    }

    private Task<ServiceProvider> ReadOnlyHostAsync() => AtomicHostAsync(new AtomicFaults(),
        configure: o => o.EnableAutomaticTimeoutDecisions = true,
        configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(new QueryClock(Timestamp));
            // 本组直接检验共享 DbContext / UnitOfWork，使用无重试策略以保留陈旧跟踪图。
            services.ConfigureDbContext<AtomicContractContext>(o => o.ReplaceService<IExecutionStrategyFactory, ReadOnlyStrategyFactory>());
        });

    private static async Task<ApprovalInstance> StartReadOnlyInstance(ServiceProvider host) =>
        await AtomicRun(host, "alice", sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(
            new("doc", AtomicBusiness.Type, "readonly", "alice") { OperationId = "readonly-start" }));

    private static Task<bool> ApproveReadOnlyInstance(ServiceProvider host, Guid taskId) =>
        AtomicRun(host, "bob", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("readonly.approve", taskId.ToString(), "approve", null, ApprovalPermissions.TaskApprove, new() { TaskId = taskId }),
            async (attempt, ct) => { await attempt.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, "bob", ct: ct); return true; }));

    [Theory]
    [InlineData("can-handle")]
    [InlineData("return-targets")]
    [InlineData("not-due")]
    [InlineData("handled")]
    [InlineData("not-running")]
    public async Task ReadOnlyTaskChecks_PreservePersistedStampWithSharedHostSaveOnEachProvider(string check)
    {
        await using var host = await ReadOnlyHostAsync();
        var instance = await StartReadOnlyInstance(host);
        var taskId = Assert.Single(instance.Tasks).Id;
        if (check == "handled")
            await ApproveReadOnlyInstance(host, taskId);
        await using (var storage = fixture.CreateStorage())
        {
            var stored = await storage.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
            Assert.Single(stored.Tasks).NextRemindAt = check == "not-due" ? Timestamp.AddMinutes(10) : Timestamp;
            if (check == "not-running") stored.Status = InstanceStatus.Approved;
            await storage.SaveChangesAsync();
            instance = stored;
        }
        await using var scope = host.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (check is "can-handle" or "return-targets")
            ((AtomicResolver)sp.GetRequiredService<IApprovalContextResolver>()).User = "bob";
        else sp.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("timeout", [ApprovalPermissions.TaskTimeout]);
        var engine = sp.GetRequiredService<ApprovalEngine>();
        var db = sp.GetRequiredService<AtomicContractContext>();
        await sp.GetRequiredService<IApprovalUnitOfWork>().ExecuteAsync(async ct =>
        {
            if (check == "can-handle") Assert.True(await engine.CanHandleAsync(taskId, "bob", ct));
            else if (check == "return-targets") Assert.True(Assert.Single(await engine.GetReturnTargetsAsync(taskId, "bob", ct)).IsInitiator);
            else Assert.False(await engine.HandleTimeoutAsync(taskId, ct));
            Assert.Equal(instance.ConcurrencyStamp, Assert.Single(db.ChangeTracker.Entries<ApprovalInstance>()).Entity.ConcurrencyStamp);
            db.Add(new AtomicHostRecord { Id = Guid.NewGuid(), Value = 77 });
            await db.SaveChangesAsync(ct);
        });
        await using var verify = fixture.CreateStorage();
        var saved = await verify.Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
        Assert.Equal(instance.ConcurrencyStamp, saved.ConcurrencyStamp);
        Assert.Equal(instance.Status, saved.Status);
        Assert.Equal(Assert.Single(instance.Tasks).NextRemindAt, Assert.Single(saved.Tasks).NextRemindAt);
        Assert.Equal(1, await db.Set<AtomicHostRecord>().CountAsync(r => r.Value == 77));
    }

    [Theory]
    [InlineData("Remind", InstanceStatus.Running, ApprovalAction.Remind)]
    [InlineData("AutoApprove", InstanceStatus.Approved, ApprovalAction.TimeoutApprove)]
    [InlineData("AutoReject", InstanceStatus.Rejected, ApprovalAction.TimeoutReject)]
    [InlineData("None", InstanceStatus.Running, ApprovalAction.Remind)]
    public async Task TimeoutWrites_PersistStampAndDecisionOrInconsistentScheduleCleanupOnEachProvider(string action, InstanceStatus status, ApprovalAction logAction)
    {
        await using var host = await ReadOnlyHostAsync();
        await using (var storage = fixture.CreateStorage())
        {
            var definition = await storage.Set<ApprovalDefinition>().SingleAsync();
            var flow = FlowDefinition.Parse(definition.DefinitionJson);
            if (action != "None") flow.Nodes["review"].Timeout = new TimeoutPolicy { Minutes = 10, Action = Enum.Parse<TimeoutAction>(action) };
            definition.DefinitionJson = flow.ToJson();
            await storage.SaveChangesAsync();
        }
        var instance = await StartReadOnlyInstance(host);
        var taskId = Assert.Single(instance.Tasks).Id;
        await using (var storage = fixture.CreateStorage())
        {
            var task = await storage.Set<ApprovalTask>().SingleAsync();
            task.NextRemindAt = Timestamp;
            await storage.SaveChangesAsync();
        }
        await using (var scope = host.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("timeout", [ApprovalPermissions.TaskTimeout]);
            Assert.Equal(action != "None", await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(taskId));
        }
        await using var verify = fixture.CreateStorage();
        var saved = await verify.Set<ApprovalInstance>().Include(i => i.Tasks).Include(i => i.Logs).SingleAsync();
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Equal(status, saved.Status);
        Assert.Null(Assert.Single(saved.Tasks).NextRemindAt);
        Assert.Equal(action == "None" ? 0 : 1, Assert.Single(saved.Tasks).RemindCount);
        if (action == "None")
        {
            Assert.DoesNotContain(saved.Logs, l => l.Action is ApprovalAction.Remind or ApprovalAction.TimeoutApprove or ApprovalAction.TimeoutReject);
            Assert.Null(Assert.Single(saved.Tasks).LastRemindedAt);
        }
        else
        {
            Assert.Contains(saved.Logs, l => l.Action == logAction);
            Assert.Equal(Timestamp, Assert.Single(saved.Tasks).LastRemindedAt);
            Assert.True(await verify.Set<ApprovalOutboxMessage>().AnyAsync(m => m.InstanceId == saved.Id && m.Description == logAction.ToString()));
        }
    }

    [Fact]
    public async Task RealApproval_RejectsStaleStampAndRollsBackSideEffectsOnEachProvider()
    {
        await using var host = await ReadOnlyHostAsync();
        var instance = await StartReadOnlyInstance(host);
        var taskId = Assert.Single(instance.Tasks).Id;
        await using (var storage = fixture.CreateStorage())
        {
            var task = await storage.Set<ApprovalTask>().SingleAsync();
            task.NextRemindAt = Timestamp;
            await storage.SaveChangesAsync();
        }
        await using var stale = host.CreateAsyncScope();
        var sp = stale.ServiceProvider;
        // Explicit capability isolates stale EF tracking from command reservation, which serializes SQLite writers.
        sp.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("bob", [ApprovalPermissions.TaskApprove]);
        await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalInstance>().Include(i => i.Tasks).SingleAsync();
        // 清理是实际写命令，但不追加日志。让失败审批确定检验实例令牌，避免 Provider 先检查日志序号唯一约束。
        await using (var winner = host.CreateAsyncScope())
        {
            winner.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeSystem("timeout", [ApprovalPermissions.TaskTimeout]);
            Assert.False(await winner.ServiceProvider.GetRequiredService<ApprovalEngine>().HandleTimeoutAsync(taskId));
        }
        var error = await Assert.ThrowsAsync<ApprovalException>(() => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, "bob"));
        Assert.Equal(ApprovalErrorCode.Conflict, error.Code);
        await using var verify = fixture.CreateStorage();
        var saved = await verify.Set<ApprovalInstance>().Include(i => i.Tasks).Include(i => i.Logs).SingleAsync();
        Assert.Equal(instance.ConcurrencyStamp + 1, saved.ConcurrencyStamp);
        Assert.Equal(InstanceStatus.Running, saved.Status);
        Assert.Equal(ApprovalTaskStatus.Pending, Assert.Single(saved.Tasks).Status);
        Assert.Null(Assert.Single(saved.Tasks).NextRemindAt);
        Assert.Equal(instance.Logs.Count, saved.Logs.Count);
        Assert.DoesNotContain(saved.Logs, l => l.Action == ApprovalAction.Approve);
        await using var businessScope = host.CreateAsyncScope();
        Assert.Equal(0, await businessScope.ServiceProvider.GetRequiredService<AtomicContractContext>().Set<AtomicHostRecord>().CountAsync(r => r.Value == 10));
    }
}
