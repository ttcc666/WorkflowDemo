using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Notifications;

namespace Certificate.Api.Infrastructure;

public sealed class DemoWebhookOptions
{
    public string Url { get; set; } = "";
    public string? Secret { get; set; }
}

/// <summary>示例宿主自己的通知发送实现；生产宿主可在此对接现有通知服务。</summary>
public sealed class DemoWebhookChannel(DemoWebhookOptions options, IHttpClientFactory clients) : IApprovalChannel
{
    public const string HttpClientName = "Certificate.DemoWebhook";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string Name => "Webhook";
    public string DisplayName => "宿主示例 Webhook";

    public async Task SendAsync(ChannelMessage message, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(message, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.Id.ToString());
        if (!string.IsNullOrEmpty(options.Secret))
        {
            var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Secret), Encoding.UTF8.GetBytes(body));
            request.Headers.TryAddWithoutValidation("X-Approval-Signature", "sha256=" + Convert.ToHexStringLower(signature));
        }
        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Webhook 返回 HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }
}
