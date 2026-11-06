using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class HttpWorkflowActionTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"http"},
          "http":{"type":"Action","next":"end","actions":[
            {"id":"erp","type":"Http","mode":"Async","failurePolicy":"Retry",
             "config":{"endpoint":"erp","method":"POST","body":{"orderNo":"{{ business.key }}","total":"{{form.amount}}"}}}
          ]},"end":{"type":"End"}
        }}
        """;

    [Fact]
    public async Task External_write_runs_after_commit_and_retries_with_same_id_and_payload()
    {
        var transport = new RecordingTransport(HttpStatusCode.InternalServerError, HttpStatusCode.Created);
        await using var host = CreateHost(transport);
        await host.PublishAsync("order", Definition);
        var instance = await host.StartAsync("order", "PO-1", "alice", new { amount = 320 });
        Assert.Equal(InstanceStatus.Approved, instance.Status);
        Assert.Empty(transport.Requests);
        var pending = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
        Assert.Equal(OutboxStatus.Pending, pending.Status);
        Assert.Contains("Queued", Assert.Single(await host.LogsAsync(instance.Id), l => l.Action == ApprovalAction.ActionExecuted).Details);
        await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Contains("HttpStatus (500)", failed.LastError);
        Assert.DoesNotContain("sensitive", failed.LastError);
        host.Clock.Advance(TimeSpan.FromHours(1));
        await host.DispatchOutboxAsync();
        var succeeded = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
        Assert.Equal(OutboxStatus.Succeeded, succeeded.Status);
        Assert.Equal(2, succeeded.Attempts);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(transport.Requests[0], transport.Requests[1]);
        Assert.Equal(pending.Id.ToString("N"), transport.Requests[0].Key);
        using var body = JsonDocument.Parse(transport.Requests[0].Body!);
        Assert.Equal("PO-1", body.RootElement.GetProperty("orderNo").GetString());
        Assert.Equal(320, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Exhaustion_and_manual_retry_preserve_original_id()
    {
        var transport = new RecordingTransport(HttpStatusCode.Unauthorized, HttpStatusCode.Created);
        await using var host = CreateHost(transport, 1);
        await host.PublishAsync("order", Definition);
        await host.StartAsync("order", "PO-2", "alice", new { amount = 12 });
        await host.DispatchOutboxAsync();
        var failed = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
        Assert.Equal(OutboxStatus.Failed, failed.Status);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalOutboxService>().RetryAsync(failed.Id));
        await host.DispatchOutboxAsync();
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await host.OutboxAsync(), m => m.Id == failed.Id).Status);
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.Equal(failed.Id.ToString("N"), request.Key));
    }

    [Fact]
    public async Task Write_cannot_be_synchronous_and_redirect_is_a_failed_attempt()
    {
        var transport = new RecordingTransport(HttpStatusCode.Found);
        await using var host = CreateHost(transport, 1);
        var flow = JsonNode.Parse(Definition)!;
        var step = flow["nodes"]!["http"]!["actions"]![0]!;
        step["mode"] = "Sync";
        step["failurePolicy"] = "Fail";
        var ex = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("write", flow.ToJsonString()));
        Assert.Contains("只读 GET", ex.Message);
        await host.PublishAsync("order", Definition);
        await host.StartAsync("order", "PO-3", "alice", new { amount = 12 });
        await host.DispatchOutboxAsync();
        Assert.Single(transport.Requests);
        Assert.Contains("HttpStatus (302)", Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction").LastError);
        using var handler = PublicHttpTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task Later_sync_failure_rolls_back_the_queued_http_action()
    {
        var transport = new RecordingTransport(HttpStatusCode.Created);
        await using var host = CreateHost(transport);
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["http"]!["actions"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"fail","type":"JavaScript","config":{"script":"function execute() { throw new Error('rollback'); }"}}
            """));
        await host.PublishAsync("order", flow.ToJsonString());
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("order", "PO-4", "alice", new { amount = 12 }));
        Assert.Empty(await host.OutboxAsync());
        await host.DispatchOutboxAsync();
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Return_and_resubmit_keep_first_http_delivery_snapshot()
    {
        var transport = new RecordingTransport(HttpStatusCode.Created);
        await using var host = CreateHost(transport);
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["http"]!["next"] = "review";
        flow["nodes"]!["review"] = JsonNode.Parse("""
            {"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"}
            """);
        await host.PublishAsync("order", flow.ToJsonString());
        var instance = await host.StartAsync("order", "PO-5", "alice", new { amount = 12 });
        var review = Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ReturnAsync(review.Id, "bob", "change", "start"));
        var resubmit = Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks, t => t.Kind == ApprovalTaskKind.Resubmit);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ResubmitAsync(resubmit.Id, "alice", formData: new { amount = 99 }));
        var original = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
        await host.DispatchOutboxAsync();
        var request = Assert.Single(transport.Requests);
        Assert.Equal(original.Id.ToString("N"), request.Key);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(12, body.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Pending_http_action_survives_host_restart_with_original_input()
    {
        var database = Path.Combine(Path.GetTempPath(), $"workflow-outbox-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={database};Pooling=False";
        try
        {
            Guid executionId;
            var firstTransport = new RecordingTransport();
            await using (var first = CreateHost(firstTransport, connectionString: connectionString))
            {
                await first.PublishAsync("order", Definition);
                await first.StartAsync("order", "PO-restart", "alice", new { amount = 23 });
                executionId = Assert.Single(await first.OutboxAsync(), m => m.Target == "WorkflowAction").Id;
                Assert.Empty(firstTransport.Requests);
            }
            var restartedTransport = new RecordingTransport(HttpStatusCode.Created);
            await using (var restarted = CreateHost(restartedTransport, connectionString: connectionString))
            {
                await restarted.DispatchOutboxAsync();
                Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await restarted.OutboxAsync(), m => m.Id == executionId).Status);
                var request = Assert.Single(restartedTransport.Requests);
                Assert.Equal(executionId.ToString("N"), request.Key);
                Assert.Contains("PO-restart", request.Body);
            }
        }
        finally { File.Delete(database); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_readonly_get_binds_query_and_enforces_response_budget(bool oversize)
    {
        var reply = new ReadTransport(oversize);
        await using var host = new TestHost(build: builder =>
        {
            builder.AddHttpActions(new ApprovalHttpActionEndpoint("stock", new Uri("https://erp.example.com/stock"),
                new HashSet<string> { "GET" }, AllowSynchronousRead: true));
            builder.Services.AddHttpClient(HttpActionHandler.ClientName).ConfigurePrimaryHttpMessageHandler(() => reply);
        });
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["http"]!["actions"]![0] = JsonNode.Parse("""
            {"id":"stock","type":"Http","outputKey":"stock","config":{
              "endpoint":"stock","method":"GET","query":{"code":"{{form.code}}"},"headers":{"Accept":"application/json"}
            }}
            """);
        await host.PublishAsync("read", flow.ToJsonString());
        var read = () => host.StartAsync("read", "PO-6", "alice", new { code = "P&01" });
        if (oversize)
        {
            await Assert.ThrowsAsync<ApprovalException>(read);
            Assert.Empty(await host.OutboxAsync());
        }
        else
        {
            var instance = await read();
            var result = ExecutionState.Load(await host.GetInstanceAsync(instance.Id)).ActionOutputs["stock"].Value;
            Assert.Equal(200, result.GetProperty("statusCode").GetInt32());
            Assert.Equal(42, result.GetProperty("body").GetProperty("quantity").GetInt32());
        }
        Assert.Equal("?code=P%2601", reply.Address?.Query);
        Assert.Equal("application/json", reply.Accept);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("10.2.3.4")]
    [InlineData("100.100.100.200")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public void Refuses_local_and_metadata_addresses(string address) =>
        Assert.False(PublicHttpTransport.IsPublic(IPAddress.Parse(address)));

    private static TestHost CreateHost(RecordingTransport transport, int maxAttempts = 3, string? connectionString = null) =>
        new(o => o.Outbox.MaxAttempts = maxAttempts, b =>
        {
            b.AddCalculationActions().AddHttpActions(new ApprovalHttpActionEndpoint("erp", new Uri("https://erp.example.com/orders"),
                new HashSet<string> { "GET", "POST" }, AllowSynchronousRead: true));
            b.Services.AddHttpClient(HttpActionHandler.ClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        }, connectionString);

    private sealed class RecordingTransport(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public List<(string Method, Uri Address, string Key, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method.Method, request.RequestUri!, request.Headers.GetValues("Idempotency-Key").Single(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            var result = new HttpResponseMessage(statuses[Requests.Count - 1])
                { Content = new StringContent("sensitive response", Encoding.UTF8, "text/plain") };
            result.Headers.Location = new Uri("https://169.254.169.254");
            return result;
        }
    }

    private sealed class ReadTransport(bool oversize) : HttpMessageHandler
    {
        internal Uri? Address { get; private set; }
        internal string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Address = request.RequestUri;
            Accept = request.Headers.Accept.Single().MediaType;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(oversize ? new string(' ', 20_000) : """{"quantity":42}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
