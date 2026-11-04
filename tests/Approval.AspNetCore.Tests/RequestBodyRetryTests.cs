using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Approval.Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class RequestBodyRetryTests
{
    private const string Flow = """
        {"startNodeId":"start","nodes":{"start":{"type":"Start","name":"开始","next":"review"},
        "review":{"type":"Approval","name":"审核","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"结束"}}}
        """;

    [Fact]
    public async Task ReadBodyAsStringAsync_LeavesBufferedUtf8BodyOpenForRereading()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(Flow));
        using var body = new FileBufferingReadStream(source, 30 * 1024);
        var http = new DefaultHttpContext();
        http.Request.Body = body;

        Assert.Equal(Flow, await http.ReadBodyAsStringAsync(CancellationToken.None));
        body.Position = 0;
        Assert.Equal(Flow, await http.ReadBodyAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DefinitionPublication_RetriesWithFreshContext_AndOriginalKeyReplaysWithoutRepublishing()
    {
        var fault = new PublicationFault();
        await using var host = await ApiHost.StartAsync(
            build: b => b.Services.ConfigureDbContext<ApiDbContext>(options => options
                .ReplaceService<IExecutionStrategyFactory, PublicationRetryStrategyFactory>()
                .AddInterceptors(fault)),
            workflow: options => options.RequireOperationId = true);

        async Task<HttpResponseMessage> PublishAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/approval/definitions/doc");
            request.Headers.Add(HeaderCurrentUser.Header, "admin");
            request.Headers.Add("Idempotency-Key", "publish-once");
            request.Content = new StringContent(Flow, Encoding.UTF8, "application/json");
            return await host.Client.SendAsync(request);
        }

        using var first = await PublishAsync();
        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var published = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = published.GetProperty("id").GetGuid();
        Assert.Equal(1, published.GetProperty("version").GetInt32());
        Assert.Equal(2, fault.Contexts.Count);
        Assert.NotEqual(fault.Contexts[0], fault.Contexts[1]);

        using var replay = await PublishAsync();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, replayed.GetProperty("id").GetGuid());
        Assert.Equal(1, replayed.GetProperty("version").GetInt32());
        Assert.Equal(2, fault.Contexts.Count);

        await host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApiDbContext>();
            var definition = await db.Set<ApprovalDefinition>().SingleAsync();
            Assert.Equal(id, definition.Id);
            Assert.Equal(1, definition.Version);
            using var json = JsonDocument.Parse(definition.DefinitionJson);
            Assert.Equal("审核", json.RootElement.GetProperty("nodes").GetProperty("review").GetProperty("name").GetString());
            Assert.Equal(1, await db.Set<ApprovalDefinitionAudit>().CountAsync());
            Assert.Equal(1, (await db.Set<ApprovalDefinitionManagement>().SingleAsync()).Revision);
            Assert.Equal(1, await db.Set<ApprovalOperation>().CountAsync());
            return true;
        });
    }

    private sealed class PublicationTransientException : Exception { }

    private sealed class PublicationRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new PublicationRetryStrategy(dependencies);

        private sealed class PublicationRetryStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
        {
            protected override bool ShouldRetryOn(Exception exception) => exception is PublicationTransientException;
        }
    }

    private sealed class PublicationFault : SaveChangesInterceptor
    {
        public List<Guid> Contexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } db && db.ChangeTracker.Entries<ApprovalDefinition>().Any(e => e.State == EntityState.Added))
            {
                Contexts.Add(db.ContextId.InstanceId);
                if (Contexts.Count == 1) throw new PublicationTransientException();
            }
            return ValueTask.FromResult(result);
        }
    }
}
