using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine;
using Approval.Core.Engine.Extensions;
using Approval.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Actions;

internal sealed class HttpActionHandler(IHttpClientFactory clients, IEnumerable<ApprovalHttpActionEndpoint> endpoints,
    ILogger<HttpActionHandler> logger, IApprovalActionSecretProvider? secrets = null) : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    internal const string ClientName = "Approval.WorkflowAction.Http";
    private const int MaxRequestBytes = 32 * 1024;
    private static readonly HashSet<string> Methods = new(["GET", "POST", "PUT", "PATCH", "DELETE"], StringComparer.Ordinal);
    private static readonly HashSet<string> Headers = new(["Accept", "Accept-Language", "If-Match", "If-None-Match"], StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private sealed record Config(string Endpoint, string Method, int TimeoutSeconds = 5,
        Dictionary<string, string>? Query = null, Dictionary<string, string>? Headers = null,
        JsonElement Body = default, Dictionary<string, string>? Form = null);

    private readonly Dictionary<string, ApprovalHttpActionEndpoint> _endpoints = endpoints.ToDictionary(e => e.Name, StringComparer.Ordinal);

    public bool ProvidesOutput => false;

    internal string DestinationStamp(JsonElement config)
    {
        var value = Read(config);
        var endpoint = _endpoints[value.Endpoint];
        return ApprovalCommandHash.Of(endpoint.Address.AbsoluteUri, value.Method, endpoint.BearerSecret ?? "");
    }

    public ApprovalActionDescriptor Descriptor => new("Http", "HTTP 请求", "调用宿主登记的 HTTPS 目标",
        [ActionExecutionMode.Sync, ActionExecutionMode.Async], ConfigSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "endpoint", "method" },
            properties = new
            {
                endpoint = new { type = "string", @enum = _endpoints.Keys.Order(StringComparer.Ordinal).ToArray() },
                method = new { type = "string", @enum = Methods.Order(StringComparer.Ordinal).ToArray() },
                timeoutSeconds = new { type = "integer", minimum = 1, maximum = 15, @default = 5 },
                query = new { type = "object" }, headers = new { type = "object" },
                body = new { type = "object" }, form = new { type = "object" }
            }
        }));

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => ValidateConfig(version, ActionExecutionMode.Async, config);

    public IReadOnlyList<string> ValidateConfig(int version, ActionExecutionMode mode, JsonElement config)
    {
        try
        {
            var value = Read(config);
            if (version != 1 || value.Endpoint is null || !_endpoints.TryGetValue(value.Endpoint, out var endpoint) ||
                !PublicHttpTransport.IsAllowedAddress(endpoint.Address) || !Methods.Contains(value.Method) ||
                !endpoint.Methods.Contains(value.Method) || value.TimeoutSeconds is < 1 or > 15 ||
                endpoint.BearerSecret is not null && secrets is null)
                return ["HTTP 目标、方法、超时或宿主凭证配置无效"];
            if (mode == ActionExecutionMode.Sync && (value.Method != "GET" || !endpoint.AllowSynchronousRead))
                return ["只有宿主显式允许的只读 GET 可同步执行"];
            if (value.Body.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array) ||
                (value.Method == "GET" || value.Form is not null) && value.Body.ValueKind != JsonValueKind.Undefined ||
                value.Method == "GET" && value.Form is not null || !ValidPairs(value.Query) || !ValidPairs(value.Form) ||
                !ValidPairs(value.Headers) || value.Headers?.Keys.Any(name => !Headers.Contains(name)) == true ||
                !ValidBody(value.Body))
                return ["HTTP 请求参数、请求头或请求体配置无效"];
            return [];
        }
        catch (JsonException) { return ["HTTP 动作配置无效"]; }
    }

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        var value = Read(config);
        var endpoint = _endpoints[value.Endpoint];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(value.TimeoutSeconds));
        using var request = await BuildRequestAsync(value, endpoint, context, timeout.Token);
        using var client = clients.CreateClient(ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new WorkflowActionDeliveryException("HttpStatus", (int)response.StatusCode);
        logger.LogInformation("HTTP 动作 {ExecutionId} 返回 {HttpStatusCode}", context.ExecutionId, (int)response.StatusCode);
        if (context.Mode == ActionExecutionMode.Async) return new(true);

        var output = await ReadResponseAsync(response, timeout.Token);
        return new(true, JsonSerializer.SerializeToElement(new { statusCode = (int)response.StatusCode, body = output }));
    }

    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = Read(config);
        var root = ActionVariables.Snapshot(context);
        return Task.FromResult(new ApprovalActionResult(true, JsonSerializer.SerializeToElement(new
        {
            endpoint = value.Endpoint, method = value.Method, queryKeys = value.Query?.Keys.ToArray(),
            body = value.Body.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : ActionVariables.Bind(value.Body, root)
        })));
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(Config config, ApprovalHttpActionEndpoint endpoint,
        ApprovalActionContext context, CancellationToken ct)
    {
        var root = ActionVariables.Snapshot(context);
        var query = RenderPairs(config.Query, root);
        var address = new UriBuilder(endpoint.Address) { Query = string.Join('&', query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))) }.Uri;
        if (address.AbsoluteUri.Length > 4096) throw new WorkflowActionDeliveryException("HttpAddressTooLong");
        var request = new HttpRequestMessage(new HttpMethod(config.Method), address)
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        try
        {
            foreach (var header in RenderPairs(config.Headers, root)) request.Headers.Add(header.Key, header.Value);
            if (context.Mode == ActionExecutionMode.Async)
                request.Headers.Add("Idempotency-Key", context.ExecutionId.ToString("N"));
            if (endpoint.BearerSecret is { } alias)
            {
                var token = await secrets!.ResolveBearerTokenAsync(alias, ct);
                if (string.IsNullOrWhiteSpace(token)) throw new WorkflowActionDeliveryException("MissingHttpCredential");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            if (config.Form is not null) request.Content = new FormUrlEncodedContent(RenderPairs(config.Form, root));
            else if (config.Body.ValueKind != JsonValueKind.Undefined)
                request.Content = new StringContent(ActionVariables.Bind(config.Body, root).GetRawText(), Encoding.UTF8, "application/json");
            if (request.Content?.Headers.ContentLength > MaxRequestBytes)
                throw new WorkflowActionDeliveryException("HttpRequestTooLarge");
            return request;
        }
        catch { request.Dispose(); throw; }
    }

    private static async Task<JsonElement?> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var limit = ApprovalActionValidation.MaxOutputBytes - 256;
        if (response.Content.Headers.ContentLength > limit) throw new WorkflowActionDeliveryException("HttpResponseTooLarge");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[limit + 1];
        var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, ct);
        if (length > limit) throw new WorkflowActionDeliveryException("HttpResponseTooLarge");
        if (length == 0) return null;
        using var document = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 14, AllowDuplicateProperties = false });
        return document.RootElement.Clone();
    }

    private static Config Read(JsonElement json) => json.Deserialize<Config>(Json) ?? throw new JsonException();

    private static bool ValidPairs(Dictionary<string, string>? pairs) => pairs is null ||
        pairs.Count <= 32 && pairs.All(p => p.Key.Length is > 0 and <= 80 && p.Value is { Length: <= 4096 } && ActionVariables.IsTemplate(p.Value));

    private static bool ValidBody(JsonElement body) => body.ValueKind switch
    {
        JsonValueKind.Object => body.EnumerateObject().All(p => ValidBody(p.Value)),
        JsonValueKind.Array => body.EnumerateArray().All(ValidBody),
        JsonValueKind.String => ActionVariables.IsTemplate(body.GetString()!),
        _ => true
    };

    private static KeyValuePair<string, string>[] RenderPairs(Dictionary<string, string>? pairs, JsonElement root) =>
        pairs?.Select(pair => KeyValuePair.Create(pair.Key, ActionVariables.Render(pair.Value, root))).ToArray() ?? [];
}
