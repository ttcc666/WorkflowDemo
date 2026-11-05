using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class DefinitionTransferEndpointTests
{
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true,"category":"采购"},"nodes":{
        "start":{"type":"Start","name":"Start","next":"review"},
        "review":{"type":"Approval","name":"审批","approvers":[{"type":"User","value":"bob"}],"next":"end"},
        "end":{"type":"End","name":"End"}}}
        """;
    private const string Import = "/api/approval/definitions/copied/import";

    [Fact]
    public async Task ExportPreviewAndImport_RoundTripWithReplay_OnlyManualPublishCreatesAVersion()
    {
        await using var host = await ApiHost.StartAsync();
        await host.PublishAsync("original", Flow);
        await host.PublishAsync("original", Flow.Replace("审批", "复审"));
        var versions = await host.GetJsonAsync("/api/approval/definitions/original/versions", "admin");
        var v1 = versions[1];
        using var export = await host.SendAsync(HttpMethod.Get, $"/api/approval/definitions/by-id/{v1.GetProperty("id").GetGuid()}/export", "admin");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(export.Headers.CacheControl?.NoStore);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        using var package = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "definition", "kind", "packageVersion", "source" }, package.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(1, package.RootElement.GetProperty("source").GetProperty("version").GetInt32());
        Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(v1.GetProperty("definitionJson").GetString()!), package.RootElement.GetProperty("definition")));

        var initial = await CountAsync(host);
        using var preview = await SendAsync(host, Import + "/validate", bytes, null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("original", (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        Assert.Equal(initial, await CountAsync(host));
        Assert.Equal("OperationIdRequired", (await (await SendAsync(host, Import, bytes, null)).ProblemAsync(400)).GetProperty("code").GetString());

        using var first = await SendAsync(host, Import, bytes, "import");
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        using var repeated = await SendAsync(host, Import, bytes, "import");
        Assert.Equal(await first.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(host, Import, [.. bytes, (byte)' '], "import")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(host, Import, bytes, "other-key")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(host, Import.Replace("copied", "original"), bytes, "occupied")).StatusCode);
        Assert.Equal((initial.Definitions, initial.Management + 1, initial.Audits + 1, initial.Operations + 1, 0, 0), await CountAsync(host));

        var governance = await host.GetJsonAsync("/api/approval/definitions/copied/governance", "admin");
        Assert.True(governance.GetProperty("hasDraftChanges").GetBoolean());
        Assert.Equal(0, governance.GetProperty("lastVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, governance.GetProperty("activeDefinitionId").ValueKind);
        Assert.Equal("采购", governance.GetProperty("category").GetString());
        var audit = await host.GetJsonAsync("/api/approval/definitions/copied/audit", "admin");
        Assert.Equal("导入自 original v1", Assert.Single(audit.GetProperty("items").EnumerateArray()).GetProperty("changeNote").GetString());

        var published = await host.PostJsonAsync("/api/approval/definitions/copied/publish-draft", "admin", new { expectedRevision = 1 });
        Assert.Equal(1, published.GetProperty("version").GetInt32());
        Assert.True(JsonElement.DeepEquals(versions, await host.GetJsonAsync("/api/approval/definitions/original/versions", "admin")));
    }

    [Theory]
    [InlineData("json", "UTF-8 JSON")]
    [InlineData("utf8", "UTF-8 JSON")]
    [InlineData("package", "包类型或版本")]
    [InlineData("schema", "协议版本")]
    [InlineData("duplicates", "UTF-8 JSON")]
    [InlineData("nodes", "missing")]
    [InlineData("resolver", "MissingResolver")]
    [InlineData("channel", "MissingChannel")]
    [InlineData("forms", "可选模块")]
    public async Task InvalidPackageOrUnavailableDependency_LeavesNoRecords(string testCase, string error)
    {
        await using var host = await ApiHost.StartAsync();
        var bytes = testCase switch
        {
            "json" => "{"u8.ToArray(),
            "utf8" => new byte[] { 0xC3, 0x28 },
            "package" => Package(Flow, 2),
            "schema" => Package(Flow.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")),
            "duplicates" => Package(Flow.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")),
            "nodes" => Package(Flow.Replace("\"next\":\"end\"", "\"next\":\"missing\"")),
            "resolver" => Package(Flow.Replace("\"type\":\"User\"", "\"type\":\"MissingResolver\"")),
            "channel" => Package(Flow.Replace("\"approvers\":", "\"notifications\":[{\"events\":[\"Enter\"],\"recipients\":[{\"type\":\"User\",\"value\":\"bob\"}],\"channels\":[\"MissingChannel\"]}],\"approvers\":")),
            "forms" => Package(Flow.Replace("\"nodes\":", "\"form\":{\"fields\":[{\"key\":\"quantity\",\"type\":\"Number\",\"label\":\"数量\"}]},\"nodes\":")),
            _ => throw new ArgumentOutOfRangeException(nameof(testCase))
        };
        using var response = await SendAsync(host, Import, bytes, "bad-file");
        var problem = await response.ProblemAsync(400);
        Assert.Equal("InvalidDefinition", problem.GetProperty("code").GetString());
        Assert.Contains(error, problem.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0, 0, 0, 0, 0), await CountAsync(host));
    }

    [Fact]
    public async Task SizeLimitsApplyWithAndWithoutContentLength_AndAcceptExactFileBoundary()
    {
        await using var host = await ApiHost.StartAsync();
        var bytes = Package(Flow);
        Array.Resize(ref bytes, 1024 * 1024);
        // JSON permits trailing whitespace. Padding preserves an otherwise valid package.
        Array.Fill(bytes, (byte)' ', Package(Flow).Length, bytes.Length - Package(Flow).Length);
        using var boundary = await SendAsync(host, Import + "/validate", bytes, null);
        Assert.True(boundary.IsSuccessStatusCode, await boundary.Content.ReadAsStringAsync());
        using var largeFile = await SendAsync(host, Import, [.. bytes, (byte)' '], "size");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, largeFile.StatusCode);
        using var stream = new NonSeekableStream(new byte[1536 * 1024 + 1]);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, Import + "/validate") { Content = content };
        request.Headers.Add(HeaderCurrentUser.Header, "admin");
        Assert.Null(content.Headers.ContentLength);
        using var largeBody = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, largeBody.StatusCode);
        Assert.Equal((0, 0, 0, 0, 0, 0), await CountAsync(host));
    }

    [Fact]
    public async Task FormsSnapshotIsPreserved_WhenHostEnablesForms()
    {
        await using var host = await ApiHost.StartAsync(build: b => b.AddApprovalForms());
        var flow = Flow.Replace("\"nodes\":", "\"form\":{\"fields\":[{\"key\":\"quantity\",\"type\":\"Number\",\"label\":\"数量\"}]},\"nodes\":");
        using var response = await SendAsync(host, Import, Package(flow), "forms");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var state = await host.GetJsonAsync("/api/approval/definitions/copied/governance", "admin");
        using var draft = JsonDocument.Parse(state.GetProperty("draftJson").GetString()!);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(flow).GetProperty("form"), draft.RootElement.GetProperty("form")));
    }

    [Fact]
    public async Task DefinitionTransferRequiresSensitiveReadAndTargetDesignPermissions()
    {
        var policy = new TestPermissions();
        await using var host = await ApiHost.StartAsync(build: b => b.Services.AddSingleton<IApprovalPermissionProvider>(policy));
        await host.PublishAsync("original", Flow);
        var source = (await host.GetJsonAsync("/api/approval/definitions/original/versions", "admin"))[0].GetProperty("id").GetGuid();
        var initial = await CountAsync(host);
        using var export = await host.SendAsync(HttpMethod.Get, $"/api/approval/definitions/by-id/{source}/export", "bob");
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.DoesNotContain("approvers", await export.Content.ReadAsStringAsync());
        using var preview = await SendAsync(host, Import + "/validate", Package(Flow), null, "bob");
        using var write = await SendAsync(host, Import, Package(Flow), "forbidden", "bob");
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(initial, await CountAsync(host));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportCommitFault_IsAtomic_AndOriginalKeyRecovers(bool afterCommit)
    {
        var fault = new CommitFault();
        await using var host = await ApiHost.StartAsync(build: b => b.Services.ConfigureDbContext<ApiDbContext>(o =>
            o.AddInterceptors(fault, new VerificationFault(fault))));
        fault.Mode = afterCommit ? 2 : 1;
        var bytes = Package(Flow);
        using var failed = await SendAsync(host, Import, bytes, "fault");
        if (afterCommit)
            Assert.Equal("CommitOutcomeUnknown", (await failed.ProblemAsync(503)).GetProperty("code").GetString());
        else
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        fault.BlockVerification = false;
        Assert.Equal(afterCommit ? (0, 1, 1, 1, 0, 0) : (0, 0, 0, 0, 0, 0), await CountAsync(host));
        using var recovered = await SendAsync(host, Import, bytes, "fault");
        Assert.True(recovered.IsSuccessStatusCode, await recovered.Content.ReadAsStringAsync());
        Assert.Equal((0, 1, 1, 1, 0, 0), await CountAsync(host));
    }

    private static byte[] Package(string flow, int version = 1) => Encoding.UTF8.GetBytes($$"""
        {"packageVersion":{{version}},"kind":"approval-definition","source":{"key":"original","name":"采购审批","version":1,"publishedAt":"2026-01-01T00:00:00Z"},"definition":{{flow}}}
        """);

    private static async Task<HttpResponseMessage> SendAsync(ApiHost host, string url, byte[] bytes, string? operationId, string user = "admin")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = JsonContent.Create(new { packageBase64 = Convert.ToBase64String(bytes) }) };
        request.Headers.Add(HeaderCurrentUser.Header, user);
        if (operationId is not null) request.Headers.Add("Idempotency-Key", operationId);
        return await host.Client.SendAsync(request);
    }

    private static Task<(int Definitions, int Management, int Audits, int Operations, int Instances, int Outbox)> CountAsync(ApiHost host) =>
        host.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApiDbContext>();
            return (await db.Set<ApprovalDefinition>().CountAsync(), await db.Set<ApprovalDefinitionManagement>().CountAsync(),
                await db.Set<ApprovalDefinitionAudit>().CountAsync(), await db.Set<ApprovalOperation>().CountAsync(),
                await db.Set<ApprovalInstance>().CountAsync(), await db.Set<ApprovalOutboxMessage>().CountAsync());
        });

    private sealed class TestPermissions : IApprovalPermissionProvider
    {
        public Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct) =>
            Task.FromResult(request.Subject.ActorId == "admin");
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public int Mode { get; set; }
        public bool BlockVerification { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Mode == 1) { Mode = 0; throw new IOException("before commit"); }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Mode == 2) { Mode = 0; BlockVerification = true; throw new IOException("commit acknowledgement lost"); }
            return Task.CompletedTask;
        }
    }

    private sealed class VerificationFault(CommitFault fault) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (fault.BlockVerification && command.CommandText.Contains("ApprovalOperations", StringComparison.Ordinal))
                throw new IOException("verification unavailable");
            return ValueTask.FromResult(result);
        }
    }
}
