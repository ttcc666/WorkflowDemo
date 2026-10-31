using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Notifications;

// 内置的可选渠道。宿主按需注册，例如：
//   .AddWebhookChannel(o => o.Url = "...")
//   .AddWeComRobotChannel(o => o.WebhookUrl = "...")
//   .AddDingTalkRobotChannel(o => { o.WebhookUrl = "..."; o.Secret = "..."; })
//   .AddEmailChannel(o => { o.Host = "smtp.example.com"; o.From = "noreply@example.com"; })

public sealed class WebhookChannelOptions
{
    public string Name { get; set; } = "Webhook";
    public string DisplayName { get; set; } = "Webhook";
    public string Url { get; set; } = "";

    /// <summary>设置后对请求体做 HMAC-SHA256 签名，放在 X-Approval-Signature 头（sha256=十六进制）。</summary>
    public string? Secret { get; set; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>通用 Webhook：把消息以 JSON POST 到指定地址，非 2xx 视为失败。</summary>
public sealed class WebhookChannel(WebhookChannelOptions options, IHttpClientFactory httpClientFactory) : IApprovalChannel
{
    public string Name => options.Name;
    public string DisplayName => options.DisplayName;

    public async Task SendAsync(ChannelMessage message, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(message, ChannelJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.Id.ToString());
        foreach (var (key, value) in options.Headers) request.Headers.TryAddWithoutValidation(key, value);
        if (!string.IsNullOrEmpty(options.Secret))
        {
            var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Secret), Encoding.UTF8.GetBytes(body));
            request.Headers.TryAddWithoutValidation("X-Approval-Signature", "sha256=" + Convert.ToHexStringLower(signature));
        }

        using var response = await httpClientFactory.CreateClient(ChannelJson.HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Webhook 返回 HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }
}

public sealed class WeComRobotChannelOptions
{
    public string Name { get; set; } = "WeCom";
    public string DisplayName { get; set; } = "企业微信";

    /// <summary>群机器人地址：https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...</summary>
    public string WebhookUrl { get; set; } = "";
}

/// <summary>企业微信群机器人（markdown 消息）；配置了联系方式时 @ 接收人（企业微信 UserId）。</summary>
public sealed class WeComRobotChannel(
    WeComRobotChannelOptions options, IHttpClientFactory httpClientFactory, IApprovalContactProvider? contacts = null) : IApprovalChannel
{
    public string Name => options.Name;
    public string DisplayName => options.DisplayName;

    public async Task SendAsync(ChannelMessage message, CancellationToken ct)
    {
        var mentions = await ChannelJson.ContactsAsync(contacts, message.Recipients, Name, ct);
        var content = new StringBuilder($"**{message.Title}**\n{message.Content}");
        if (message.Link is not null) content.Append($"\n[查看详情]({message.Link})");
        if (mentions.Count > 0) content.Append('\n').AppendJoin(' ', mentions.Select(m => $"<@{m}>"));

        var payload = new { msgtype = "markdown", markdown = new { content = content.ToString() } };
        await ChannelJson.PostRobotAsync(httpClientFactory, options.WebhookUrl, payload, "企业微信", ct);
    }
}

public sealed class DingTalkRobotChannelOptions
{
    public string Name { get; set; } = "DingTalk";
    public string DisplayName { get; set; } = "钉钉";

    /// <summary>群机器人地址：https://oapi.dingtalk.com/robot/send?access_token=...</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>机器人“加签”密钥（可选）。</summary>
    public string? Secret { get; set; }
}

/// <summary>钉钉群机器人（markdown 消息），支持加签；配置了联系方式时 @ 接收人（钉钉 UserId）。</summary>
public sealed class DingTalkRobotChannel(
    DingTalkRobotChannelOptions options, IHttpClientFactory httpClientFactory, TimeProvider clock,
    IApprovalContactProvider? contacts = null) : IApprovalChannel
{
    public string Name => options.Name;
    public string DisplayName => options.DisplayName;

    public async Task SendAsync(ChannelMessage message, CancellationToken ct)
    {
        var mentions = await ChannelJson.ContactsAsync(contacts, message.Recipients, Name, ct);
        var text = new StringBuilder($"#### {message.Title}\n\n{message.Content}");
        if (message.Link is not null) text.Append($"\n\n[查看详情]({message.Link})");
        if (mentions.Count > 0) text.Append("\n\n").AppendJoin(' ', mentions.Select(m => $"@{m}"));

        var payload = new
        {
            msgtype = "markdown",
            markdown = new { title = message.Title, text = text.ToString() },
            at = new { atUserIds = mentions }
        };
        await ChannelJson.PostRobotAsync(httpClientFactory, SignedUrl(), payload, "钉钉", ct);
    }

    /// <summary>钉钉加签：sign = Base64(HmacSHA256(secret, "{timestamp}\n{secret}"))。</summary>
    private string SignedUrl()
    {
        if (string.IsNullOrEmpty(options.Secret)) return options.WebhookUrl;
        var timestamp = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Secret), Encoding.UTF8.GetBytes($"{timestamp}\n{options.Secret}"));
        return $"{options.WebhookUrl}&timestamp={timestamp}&sign={Uri.EscapeDataString(Convert.ToBase64String(hash))}";
    }
}

public sealed class EmailChannelOptions
{
    public string Name { get; set; } = "Email";
    public string DisplayName { get; set; } = "邮件";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "";
}

/// <summary>SMTP 邮件。收件地址通过 <see cref="IApprovalContactProvider"/> 按用户查询，查不到的用户跳过。</summary>
public sealed class EmailChannel(EmailChannelOptions options, ILogger<EmailChannel> logger, IApprovalContactProvider? contacts = null)
    : IApprovalChannel
{
    public string Name => options.Name;
    public string DisplayName => options.DisplayName;

    public async Task SendAsync(ChannelMessage message, CancellationToken ct)
    {
        var addresses = await ChannelJson.ContactsAsync(contacts, message.Recipients, Name, ct);
        if (addresses.Count == 0)
        {
            logger.LogWarning("邮件通知 {Id} 没有可用的收件地址，接收人：{Recipients}", message.Id, string.Join(",", message.Recipients));
            return;
        }

        using var mail = new MailMessage { From = new MailAddress(options.From), Subject = message.Title, BodyEncoding = Encoding.UTF8 };
        mail.Body = message.Link is null ? message.Content : $"{message.Content}\n\n查看详情：{message.Link}";
        foreach (var address in addresses) mail.To.Add(address);

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.UserName))
            client.Credentials = new System.Net.NetworkCredential(options.UserName, options.Password);
        await client.SendMailAsync(mail, ct);
    }
}

internal static class ChannelJson
{
    public const string HttpClientName = "Approval.Channels";

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task<List<string>> ContactsAsync(
        IApprovalContactProvider? contacts, IReadOnlyList<string> userIds, string channel, CancellationToken ct)
    {
        var result = new List<string>();
        if (contacts is null) return result;
        foreach (var userId in userIds)
        {
            if (await contacts.GetContactAsync(userId, channel, ct) is { Length: > 0 } contact) result.Add(contact);
        }
        return result;
    }

    /// <summary>企业微信、钉钉机器人：HTTP 200 但 errcode 非 0 同样视为失败。</summary>
    public static async Task PostRobotAsync(IHttpClientFactory factory, string url, object payload, string channel, CancellationToken ct)
    {
        using var response = await factory.CreateClient(HttpClientName).PostAsJsonAsync(url, payload, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{channel}返回 HTTP {(int)response.StatusCode}：{text}", null, response.StatusCode);

        var errcode = JsonNode.Parse(text)?["errcode"]?.GetValue<int>() ?? 0;
        if (errcode != 0) throw new InvalidOperationException($"{channel}返回错误：{text}");
    }
}
