using System.Net;
using Approval.Core.Definitions;
using Approval.Core.Notifications;
using Certificate.Api.Infrastructure;

namespace Certificate.Api.Tests;

public sealed class DemoWebhookChannelTests
{
    private static readonly ChannelMessage Message = new(
        Guid.Parse("00000000-0000-0000-0000-000000000001"), NotifyEvent.Approved,
        Guid.Parse("00000000-0000-0000-0000-000000000002"), "review", "Approved", "Approved request", ["u1"], null);

    private sealed class Transport(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body, Key, Signature;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://host.example/notify", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            Body = await request.Content!.ReadAsStringAsync(ct);
            Key = request.Headers.GetValues("Idempotency-Key").Single();
            Signature = request.Headers.TryGetValues("X-Approval-Signature", out var values) ? values.Single() : null;
            return new HttpResponseMessage(status);
        }
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(DemoWebhookChannel.HttpClientName, name);
            return client;
        }
    }

    [Fact]
    public async Task HostWebhook_PreservesEventBodyStableIdAndSignature()
    {
        using var transport = new Transport(HttpStatusCode.OK);
        using var client = new HttpClient(transport);
        var channel = new DemoWebhookChannel(new() { Url = "https://host.example/notify", Secret = "test-secret" }, new Clients(client));
        await channel.SendAsync(Message, CancellationToken.None);
        Assert.Equal("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"event\":\"Approved\",\"instanceId\":\"00000000-0000-0000-0000-000000000002\",\"nodeId\":\"review\",\"title\":\"Approved\",\"content\":\"Approved request\",\"recipients\":[\"u1\"],\"link\":null}", transport.Body);
        Assert.Equal(Message.Id.ToString(), transport.Key);
        Assert.Equal("sha256=7d927d7e9e403087d2c1cd975d4acbb56718cd0b6d0fbde6a7513be0f4487532", transport.Signature);
    }

    [Fact]
    public async Task HostWebhook_PropagatesFailureForOutboxRetry()
    {
        using var transport = new Transport(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(transport);
        var channel = new DemoWebhookChannel(new() { Url = "https://host.example/notify" }, new Clients(client));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(Message, CancellationToken.None));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(Message.Id.ToString(), transport.Key);
        Assert.Null(transport.Signature);
    }
}
