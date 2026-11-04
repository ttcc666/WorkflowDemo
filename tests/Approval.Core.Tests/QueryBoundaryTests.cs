using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Reflection;

namespace Approval.Core.Tests;

public sealed class QueryBoundaryTests
{
    private sealed class DefinitionReads : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("DefinitionJson", StringComparison.Ordinal)) Count++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class DenyPolicy : IApprovalAccessPolicy
    {
        public Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstanceContext instance, CancellationToken ct) => Task.FromResult(ApprovalAccess.None);
    }

    [Fact]
    public async Task BusinessHistory_BatchesDefinitionReadsAcrossVersionsAndReusesTheRequestCache()
    {
        var reads = new DefinitionReads();
        await using var host = new TestHost(o => o.SingleRunningInstancePerBusiness = false,
            b => b.Services.ConfigureDbContext<TestDbContext>(o => o.AddInterceptors(reads)));
        await host.PublishAsync("history", Flows.SingleNode("Any", "User", "bob"));
        await host.StartAsync("history", "one", "alice", new { amount = 1 });
        await host.PublishAsync("history", Flows.SingleNode("Any", "User", "carol"));
        await host.StartAsync("history", "one", "alice", new { amount = 2 });
        await host.RunAsync(async sp =>
        {
            var query = sp.GetRequiredService<ApprovalQueryService>();
            reads.Count = 0;
            var values = await query.GetByBusinessForViewerAsync(RecordingBusinessHandler.Type, "one", new("alice", true));
            Assert.Equal(2, values.Count);
            Assert.Equal(new[] { 1, 2 }, values.Select(i => i.DefinitionVersion).Order());
            Assert.Equal(new[] { "bob", "carol" }, values.SelectMany(i => i.Tasks).Select(t => t.AssigneeValue).Order());
            Assert.Equal(1, reads.Count);
            var again = await query.GetByBusinessForViewerAsync(RecordingBusinessHandler.Type, "one", new("alice", true));
            Assert.Equal(values.Select(i => i.Id).Order(), again.Select(i => i.Id).Order());
            Assert.Equal(1, reads.Count);
        });
    }

    [Fact]
    public async Task DeniedHistory_DoesNotParseAnUnreadableCorruptDefinition()
    {
        await using var host = new TestHost(build: b => b.AddAccessPolicy<DenyPolicy>());
        await host.PublishAsync("history", Flows.SingleNode("Any", "User", "bob"));
        await host.StartAsync("history", "one", "alice");
        await host.SeedStorageAsync(async db =>
        {
            (await db.Set<ApprovalDefinition>().SingleAsync()).DefinitionJson = "not valid JSON";
            await db.SaveChangesAsync();
        });
        var values = await host.RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>()
            .GetByBusinessForViewerAsync(RecordingBusinessHandler.Type, "one", new("outsider", false)));
        Assert.Empty(values);
    }

    [Fact]
    public void PublicQueryResults_DoNotContainPersistenceEntities()
    {
        var seen = new HashSet<Type>();
        foreach (var method in typeof(ApprovalQueryService).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) Inspect(method.ReturnType);
        void Inspect(Type type)
        {
            if (!seen.Add(type)) return;
            Assert.False(typeof(IApprovalEntity).IsAssignableFrom(type), $"Entity returned by query contract: {type.FullName}");
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Inspect(argument);
            if (type.IsArray) Inspect(type.GetElementType()!);
            if (type.Assembly != typeof(ApprovalQueryService).Assembly || type.IsEnum) return;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) Inspect(property.PropertyType);
        }
    }
}
