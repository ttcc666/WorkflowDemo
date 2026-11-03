using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Templates;

namespace Approval.Core.Actions;

/// <summary>
/// 内置动作：HTTP 请求。只能访问 <see cref="ApprovalHttpActionOptions.AllowedHosts"/> 中的主机；
/// 非 2xx 响应视为失败（异步模式下自动重试）。输出为 <c>{ status, body }</c>，JSON 响应体会被自动解析，
/// 可在后续条件中使用，如输出变量为 <c>risk</c> 时：<c>risk.body.level == "high"</c>。
/// </summary>
public sealed class HttpActionHandler(IHttpClientFactory httpClientFactory, ApprovalOptions options) : IApprovalActionHandler
{
    public const string ActionType = "Http";
    public const string HttpClientName = "Approval.HttpAction";
    public bool HasExternalSideEffects => true;

    public ApprovalActionDescriptor Descriptor { get; } = new(ActionType, "HTTP 请求",
    [
        new ActionParameter("method", "请求方法", ActionParameterKind.Select)
        {
            Required = true, DefaultValue = "POST", Options = ["GET", "POST", "PUT", "PATCH", "DELETE"]
        },
        new ActionParameter("url", "地址")
        {
            Required = true, Help = "填写最终地址（不跟随重定向）；支持占位符，如 {appBaseUrl}/api/archive?id={实例ID|url}", Template = TemplateEncoding.Raw
        },
        new ActionParameter("headers", "请求头", ActionParameterKind.TextArea)
        {
            Help = "每行一个，格式 Name: Value"
        },
        new ActionParameter("body", "请求体", ActionParameterKind.Code)
        {
            Help = "占位符按 JSON 字符串转义，如 {\"title\": \"{标题}\", \"amount\": {form.amount}}",
            Template = TemplateEncoding.Json
        },
        new ActionParameter("contentType", "Content-Type") { DefaultValue = "application/json" },
        new ActionParameter("timeoutSeconds", "超时（秒）", ActionParameterKind.Number) { DefaultValue = "10" }
    ])
    {
        Description = "调用外部系统接口；有副作用的调用建议使用异步模式（失败自动重试）"
    };

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
    {
        var url = context.Get("url") ?? throw new InvalidOperationException("未配置请求地址");
        var uri = EnsureAllowed(url);

        using var request = new HttpRequestMessage(new HttpMethod(context.Get("method") ?? "POST"), uri);
        foreach (var line in (context.Get("headers") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0) throw new InvalidOperationException($"请求头格式错误：{line}");
            request.Headers.TryAddWithoutValidation(line[..index].Trim(), line[(index + 1)..].Trim());
        }
        // 接收方可据此去重：异步重试时该值不变
        request.Headers.TryAddWithoutValidation("Idempotency-Key", context.IdempotencyKey.ToString());

        if (context.Get("body") is { } body && request.Method != HttpMethod.Get)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Get("contentType") ?? "application/json");
        }

        var timeout = double.TryParse(context.Get("timeoutSeconds"), out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 120))
            : options.Http.DefaultTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"请求 {uri.GetLeftPart(UriPartial.Path)} 超时（{timeout.TotalSeconds:0} 秒）");
        }

        using (response)
        {
            var text = await ReadLimitedAsync(response.Content, options.Http.MaxResponseBytes, timeoutCts.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {status}：{Truncate(text, 200)}", null, response.StatusCode);

            var output = new JsonObject { ["status"] = status, ["body"] = ParseBody(text) };
            return new ApprovalActionResult { Output = output, Message = $"HTTP {status}" };
        }
    }

    public IEnumerable<string> ValidateParameters(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url)) yield break;
        // 地址以占位符开头（如 {appBaseUrl}）时只能在运行时检查
        if (url.TrimStart().StartsWith('{')) yield break;

        string? error = null;
        try
        {
            EnsureAllowed(TemplateRenderer.Render(url, new Dictionary<string, object?>()));
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }
        if (error is not null) yield return error;
    }

    private Uri EnsureAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"请求地址无效：{url}");
        if (!IsHostAllowed(uri.Host, options.Http.AllowedHosts))
            throw new InvalidOperationException($"主机 {uri.Host} 不在 HTTP 动作白名单中（ApprovalOptions.Http.AllowedHosts）");
        return uri;
    }

    public static bool IsHostAllowed(string host, IEnumerable<string> allowedHosts) =>
        allowedHosts.Any(pattern =>
            pattern == "*"
            || string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)
            || (pattern.StartsWith("*.", StringComparison.Ordinal) && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)));

    private static async Task<string> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), ct)) > 0) total += read;
        if (total > maxBytes) throw new InvalidOperationException($"响应体超过 {maxBytes} 字节");
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static JsonNode? ParseBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
