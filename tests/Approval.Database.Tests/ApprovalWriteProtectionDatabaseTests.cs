using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    private sealed class ProtectionActor : IApprovalContextResolver
    {
        public string User = "alice";
        public ApprovalExecutionIdentity Resolve() => new(User);
    }
    private sealed class ProtectionIdentity : IApprovalIdentityProvider, IApprovalUserStatusProvider
    {
        public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(userId is "alice" or "bob");
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
    }
    private sealed class ProtectionPermissions : IApprovalPermissionProvider
    { public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) => Task.FromResult(true); }

    [Fact]
    public async Task ApprovalWriteProtection_RejectsHostTrackedBulkAndSqlButPreservesEngine()
    {
        await ClearDataAsync();
        switch (fixture.Provider)
        {
            case "MySql": await ExerciseProtection<MySqlContractContext>(); break;
            case "SqlServer": await ExerciseProtection<SqlServerContractContext>(); break;
            case "PostgreSql": await ExerciseProtection<PostgreSqlContractContext>(); break;
            default: await ExerciseProtection<SqliteContractContext>(); break;
        }
    }
    private async Task ExerciseProtection<TContext>() where TContext : ContractDbContext
    {
        const string flow = """
            {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
            "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
            """;
        var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<TContext>();
        services.AddScoped<IApprovalContextResolver, ProtectionActor>();
        services.AddSingleton<IApprovalIdentityProvider, ProtectionIdentity>();
        services.AddSingleton<IApprovalUserStatusProvider, ProtectionIdentity>();
        services.AddSingleton<IApprovalPermissionProvider, ProtectionPermissions>();
        services.AddApprovalWorkflow<TContext>(o => { o.EnableBackgroundServices = false; o.Outbox.EnableDispatcher = false; });
        await using var provider = services.BuildServiceProvider();
        Guid instanceId;
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("fixture.publish", "write-protection", "publish", flow, ApprovalPermissions.DefinitionPublish),
                (sp, ct) => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync("write-protection", "Write protection", flow, ct));
            instanceId = (await scope.ServiceProvider.GetRequiredService<ApprovalEngine>().StartAsync(new("write-protection", "business", "one", "alice") { OperationId = "start" })).Id;
        }
        foreach (var mode in new[] { "task", "instance", "log", "definition", "bulk-update", "bulk-delete", "raw" })
        {
            await using var scope = provider.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TContext>();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                switch (mode)
                {
                    case "task": (await db.Set<ApprovalTask>().SingleAsync()).Status = ApprovalTaskStatus.Approved; break;
                    case "instance": (await db.Set<ApprovalInstance>().SingleAsync()).Status = InstanceStatus.Approved; break;
                    case "log": db.Remove(await db.Set<ApprovalLog>().FirstAsync()); break;
                    case "definition": (await db.Set<ApprovalDefinition>().SingleAsync()).Name = "forged"; break;
                    case "bulk-update": await db.Set<ApprovalTask>().ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ApprovalTaskStatus.Approved)); return;
                    case "bulk-delete": await db.Set<ApprovalInstance>().ExecuteDeleteAsync(); return;
                    case "raw":
                        var sql = db.GetService<ISqlGenerationHelper>();
                        var statement = $"UPDATE {sql.DelimitIdentifier("ApprovalTasks")} SET {sql.DelimitIdentifier("Status")} = 2";
                        await db.Database.ExecuteSqlRawAsync(statement); return;
                }
                await db.SaveChangesAsync();
            });
        }
        await using (var read = fixture.Create())
        {
            Assert.Equal(InstanceStatus.Running, (await read.Set<ApprovalInstance>().SingleAsync()).Status);
            Assert.Equal(ApprovalTaskStatus.Pending, (await read.Set<ApprovalTask>().SingleAsync()).Status);
            Assert.All(await read.Set<ApprovalLog>().ToListAsync(), l => Assert.NotEqual(ApprovalAction.Approve, l.Action));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            ((ProtectionActor)scope.ServiceProvider.GetRequiredService<IApprovalContextResolver>()).User = "bob";
            var taskId = await scope.ServiceProvider.GetRequiredService<TContext>().Set<ApprovalTask>().Select(t => t.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                new("fixture.approve", taskId.ToString(), "approve", null, ApprovalPermissions.TaskApprove, new() { TaskId = taskId }),
                async (sp, ct) => { await sp.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, "bob", "approved", ct); return true; });
        }
        await using var verify = fixture.Create();
        Assert.Equal(InstanceStatus.Approved, (await verify.Set<ApprovalInstance>().SingleAsync(i => i.Id == instanceId)).Status);
        Assert.Equal(ApprovalTaskStatus.Approved, (await verify.Set<ApprovalTask>().SingleAsync()).Status);
        Assert.Equal(1, await verify.Set<ApprovalLog>().CountAsync(l => l.Action == ApprovalAction.Approve));
        Assert.True(await verify.Set<ApprovalOutboxMessage>().AnyAsync());
    }
}
