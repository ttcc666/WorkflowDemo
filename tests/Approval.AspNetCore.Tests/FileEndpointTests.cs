using Approval.Forms;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Approval.Core.Domain;
using Approval.Forms.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore.Tests;

public sealed class MemoryStore : IApprovalFileStore
{
    private static readonly Dictionary<string, byte[]> Files = [];

    public async Task<string> SaveAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var key = Guid.NewGuid().ToString("N");
        lock (Files) Files[key] = buffer.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        lock (Files) return Task.FromResult<Stream>(new MemoryStream(Files[storageKey]));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        lock (Files) Files.Remove(storageKey);
        return Task.CompletedTask;
    }
}

/// <summary>附件接口：原始请求体上传（文件名在请求头）、下载响应头、权限。</summary>
public sealed class FileEndpointTests
{
    private static Task<ApiHost> StartAsync() => ApiHost.StartAsync(build: b => b.AddApprovalForms().AddFileStore<MemoryStore>());

    private static Task<HttpResponseMessage> UploadAsync(ApiHost host, string user, string? name, string content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/approval/files") { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) };
        request.Headers.Add(HeaderCurrentUser.Header, user);
        if (name is not null) request.Headers.Add(FileNameHeader, Uri.EscapeDataString(name));
        return host.Client.SendAsync(request);
    }

    private const string FileNameHeader = "X-File-Name";
    private const string AttachmentFlow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},"form":{"fields":[
          {"key":"secret","label":"Hidden attachment","type":"File"},
          {"key":"shared","label":"Visible attachment","type":"File"},
          {"key":"memo","label":"Memo","type":"Text"}]},"nodes":{
          "start":{"type":"Start","next":"review"},
          "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end",
            "fieldPermissions":{"secret":"Hidden","shared":"Editable","memo":"Editable"}},
          "end":{"type":"End"}}}
        """;

    private const string ReturnableFlow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true},"form":{"fields":[
          {"key":"secret","label":"Original attachment","type":"File"},
          {"key":"shared","label":"Shared attachments","type":"File"},
          {"key":"memo","label":"Memo","type":"Text"}]},"nodes":{
          "start":{"type":"Start","next":"review"},
          "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"confirm",
            "fieldPermissions":{"secret":"Editable","shared":"Editable","memo":"Editable"}},
          "confirm":{"type":"Approval","approvers":[{"type":"User","value":"hana"}],"next":"end"},
          "end":{"type":"End"}}}
        """;

    private static async Task<Guid> UploadIdAsync(ApiHost host, string name, string user = "alice")
    {
        using var upload = await UploadAsync(host, user, name, "%PDF");
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> MyTaskIdAsync(ApiHost host, Guid instanceId, string user) =>
        (await host.GetJsonAsync($"/api/approval/instances/{instanceId}/my-task", user)).GetProperty("task").GetProperty("id").GetGuid();

    private static async Task AssertDownloadAsync(ApiHost host, Guid id, string user, HttpStatusCode status)
    {
        using var response = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{id}", user);
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.OK) Assert.Equal("%PDF", await response.Content.ReadAsStringAsync());
    }

    private static Task<Dictionary<Guid, (Guid? InstanceId, Guid? DraftId)>> BindingsAsync(ApiHost host) =>
        host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalFile>().AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => (f.InstanceId, f.DraftId)));

    [Fact]
    public async Task ResubmitRemovedAttachmentCannotBeDownloadedOrCopiedWithoutHiddenFields()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("returnable", ReturnableFlow);
        await host.PublishAsync("other", AttachmentFlow);
        var removed = await UploadIdAsync(host, "removed.pdf");
        var visible = await UploadIdAsync(host, "visible.pdf");
        var history = await UploadIdAsync(host, "history.pdf");
        var reviewerFile = await UploadIdAsync(host, "reviewer.pdf", "bob");
        var foreign = await UploadIdAsync(host, "other.pdf");
        var missing = Guid.NewGuid();
        var otherInstanceId = (await host.PostJsonAsync("/api/approval/forms/other/start", "alice", new
        {
            formData = new { shared = new[] { new { id = foreign } } }
        })).GetProperty("instanceId").GetGuid();
        var instanceId = (await host.PostJsonAsync("/api/approval/forms/returnable/start", "alice", new
        {
            formData = new { secret = new[] { new { id = removed } }, shared = new[] { new { id = visible }, new { id = history } } }
        })).GetProperty("instanceId").GetGuid();
        await host.PublishAsync("returnable", ReturnableFlow.Replace("\"Shared attachments\",\"type\":\"File\"", "\"Shared text\",\"type\":\"Text\"", StringComparison.Ordinal));

        async Task ApproveAndReturnAsync(object edit)
        {
            var review = await MyTaskIdAsync(host, instanceId, "bob");
            await host.PostJsonAsync($"/api/approval/tasks/{review}/approve", "bob", edit);
            var confirmation = await MyTaskIdAsync(host, instanceId, "hana");
            await host.PostJsonAsync($"/api/approval/tasks/{confirmation}/reject", "hana", new { targetNodeId = "start", comment = $"Ticket {removed}" });
        }

        await ApproveAndReturnAsync(new { formData = new { shared = new[] { new { id = visible }, new { id = reviewerFile } } } });
        var revised = new { shared = new[] { new { id = visible }, new { id = reviewerFile } }, memo = $"Ticket {removed}" };
        var resubmit = await MyTaskIdAsync(host, instanceId, "alice");
        await host.PostJsonAsync($"/api/approval/tasks/{resubmit}/resubmit", "alice", new { formData = revised });
        var before = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "bob");
        Assert.Empty(before.GetProperty("hiddenFields").EnumerateArray());
        using var form = JsonDocument.Parse(before.GetProperty("formDataJson").GetString()!);
        Assert.Empty(form.RootElement.GetProperty("secret").EnumerateArray());
        Assert.Contains(removed.ToString(), form.RootElement.GetProperty("memo").GetString());
        var logs = before.GetProperty("logs").EnumerateArray().ToList();
        Assert.Contains(logs, l => l.GetProperty("action").GetString() == "Return" && l.GetRawText().Contains(removed.ToString()));
        Assert.Contains(history.ToString(), Assert.Single(logs, l => l.GetProperty("action").GetString() == "FormEdit").GetProperty("details").GetString());

        var task = await MyTaskIdAsync(host, instanceId, "bob");
        Task<HttpResponseMessage> CopyAsync(Guid id) => host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "bob",
            new { formData = new { secret = new[] { new { id } } } });

        using var download = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{removed}", "bob");
        using var copy = await CopyAsync(removed);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (download.StatusCode, copy.StatusCode));
        var unavailable = (await copy.ProblemAsync(400)).GetProperty("errors").GetProperty("secret")[0].GetString();
        foreach (var id in new[] { missing, foreign, history })
        {
            using var rejected = await CopyAsync(id);
            Assert.Equal(unavailable, (await rejected.ProblemAsync(400)).GetProperty("errors").GetProperty("secret")[0].GetString());
        }
        Assert.Equal(before.GetRawText(), (await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "bob")).GetRawText());
        Assert.Equal(task, await MyTaskIdAsync(host, instanceId, "bob"));
        var bindings = await BindingsAsync(host);
        foreach (var id in new[] { removed, visible, history, reviewerFile })
            Assert.Equal(((Guid?)instanceId, (Guid?)null), bindings[id]);
        Assert.Equal(((Guid?)otherInstanceId, (Guid?)null), bindings[foreign]);
        Assert.False(bindings.ContainsKey(missing));

        await AssertDownloadAsync(host, visible, "bob", HttpStatusCode.OK);
        await AssertDownloadAsync(host, history, "bob", HttpStatusCode.OK);
        await AssertDownloadAsync(host, removed, "alice", HttpStatusCode.OK);
        await AssertDownloadAsync(host, missing, "bob", HttpStatusCode.NotFound);
        await AssertDownloadAsync(host, foreign, "bob", HttpStatusCode.OK);
        await AssertDownloadAsync(host, foreign, "hana", HttpStatusCode.NotFound);

        await ApproveAndReturnAsync(new { formData = new { secret = new[] { new { id = visible } }, memo = "Updated" } });
        resubmit = await MyTaskIdAsync(host, instanceId, "alice");
        using var restoreRemoved = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{resubmit}/resubmit", "alice",
            new { formData = new { secret = new[] { new { id = removed } } } });
        Assert.Equal(unavailable, (await restoreRemoved.ProblemAsync(400)).GetProperty("errors").GetProperty("secret")[0].GetString());
        await host.PostJsonAsync($"/api/approval/tasks/{resubmit}/resubmit", "alice", new { formData = revised });
        await host.PostJsonAsync($"/api/approval/tasks/{await MyTaskIdAsync(host, instanceId, "bob")}/approve", "bob", null);
        await host.PostJsonAsync($"/api/approval/tasks/{await MyTaskIdAsync(host, instanceId, "hana")}/approve", "hana", null);
        Assert.Equal("Approved", (await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "bob")).GetProperty("status").GetString());
        await AssertDownloadAsync(host, removed, "bob", HttpStatusCode.NotFound);
        await AssertDownloadAsync(host, history, "bob", HttpStatusCode.OK);
        await AssertDownloadAsync(host, visible, "bob", HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartingDraftBindsOnlyReferencedAttachmentsAndCommitFailurePreservesDraftAndFiles()
    {
        var fault = new DraftCommitFault();
        await using var host = await ApiHost.StartAsync(build: b =>
        {
            b.AddApprovalForms().AddFileStore<MemoryStore>();
            b.Services.ConfigureDbContext<ApiDbContext>(o => o.AddInterceptors(fault));
        });
        await host.PublishAsync("returnable", ReturnableFlow);
        var used = await UploadIdAsync(host, "used.pdf");
        var omitted = await UploadIdAsync(host, "omitted.pdf");
        using var saved = await host.SendAsync(HttpMethod.Put, "/api/approval/forms/returnable/draft", "alice", new
        {
            formData = new { shared = new[] { new { id = used }, new { id = omitted } } }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var draftId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("draftId").GetGuid();
        var before = await BindingsAsync(host);
        Assert.Equal(((Guid?)null, (Guid?)draftId), before[used]);
        Assert.Equal(((Guid?)null, (Guid?)draftId), before[omitted]);

        var operationId = Guid.NewGuid().ToString("N");
        var body = new { draftId, formData = new { shared = new[] { new { id = used } }, memo = $"Ticket {omitted}" } };
        async Task<HttpResponseMessage> StartDraftAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/approval/forms/returnable/start") { Content = JsonContent.Create(body) };
            request.Headers.Add(HeaderCurrentUser.Header, "alice");
            request.Headers.Add("Idempotency-Key", operationId);
            return await host.Client.SendAsync(request);
        }

        fault.FailNextCommit = true;
        using var failed = await StartDraftAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(fault.FailNextCommit);
        var rolledBack = await BindingsAsync(host);
        Assert.Equal(before[used], rolledBack[used]);
        Assert.Equal(before[omitted], rolledBack[omitted]);
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<ApiDbContext>().Set<ApprovalInstance>().CountAsync()));
        Assert.Equal(draftId, (await host.GetJsonAsync($"/api/approval/forms/drafts/{draftId}", "alice")).GetProperty("id").GetGuid());
        await AssertDownloadAsync(host, used, "alice", HttpStatusCode.OK);
        await AssertDownloadAsync(host, omitted, "alice", HttpStatusCode.OK);

        using var start = await StartDraftAsync();
        Assert.True(start.IsSuccessStatusCode, await start.Content.ReadAsStringAsync());
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        var bindings = await BindingsAsync(host);
        Assert.Equal(((Guid?)started.GetProperty("instanceId").GetGuid(), (Guid?)null), bindings[used]);
        Assert.Equal(((Guid?)null, (Guid?)null), bindings[omitted]);
        using var draft = await host.SendAsync(HttpMethod.Get, $"/api/approval/forms/drafts/{draftId}", "alice");
        await draft.ProblemAsync(404);
        await AssertDownloadAsync(host, used, "bob", HttpStatusCode.OK);
        await AssertDownloadAsync(host, omitted, "bob", HttpStatusCode.NotFound);
        await AssertDownloadAsync(host, omitted, "alice", HttpStatusCode.OK);
    }

    private sealed class DraftCommitFault : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!FailNextCommit) return ValueTask.FromResult(result);
            FailNextCommit = false;
            throw new IOException("Draft submission commit failed");
        }
    }

    [Fact]
    public async Task HiddenAttachmentCannotBeAuthorizedByItsIdInVisibleTextOrAudit()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("attachments", AttachmentFlow);
        var secret = await UploadIdAsync(host, "secret.pdf");
        var shared = await UploadIdAsync(host, "shared.pdf");
        var started = await host.PostJsonAsync("/api/approval/forms/attachments/start", "alice", new
        {
            formData = new { secret = new[] { new { id = secret } }, shared = new[] { new { id = shared } }, memo = $"Ticket {secret}" }
        });
        var instanceId = started.GetProperty("instanceId").GetGuid();
        var details = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "bob");
        Assert.Equal("secret", Assert.Single(details.GetProperty("hiddenFields").EnumerateArray()).GetString());
        Assert.Contains(secret.ToString(), details.GetProperty("formDataJson").GetString());
        using var denied = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{secret}", "bob");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var allowed = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{shared}", "bob");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var owner = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{secret}", "alice");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);

        var myTask = await host.GetJsonAsync($"/api/approval/instances/{instanceId}/my-task", "bob");
        var taskId = myTask.GetProperty("task").GetProperty("id").GetGuid();
        await host.PostJsonAsync($"/api/approval/tasks/{taskId}/approve", "bob", new { formData = new { memo = "Updated", shared = Array.Empty<object>() } });
        // Both IDs are now present only in the visible edit audit. Only the real file field grants access.
        using var auditDenied = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{secret}", "bob");
        Assert.Equal(HttpStatusCode.NotFound, auditDenied.StatusCode);
        using var auditAllowed = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{shared}", "bob");
        Assert.Equal(HttpStatusCode.OK, auditAllowed.StatusCode);
    }

    [Fact]
    public async Task ReviewerCannotCopyHiddenAttachmentIntoAnEditableField()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("attachments", AttachmentFlow);
        var secret = await UploadIdAsync(host, "secret.pdf");
        var started = await host.PostJsonAsync("/api/approval/forms/attachments/start", "alice", new { formData = new { secret = new[] { new { id = secret } } } });
        var instanceId = started.GetProperty("instanceId").GetGuid();
        var myTask = await host.GetJsonAsync($"/api/approval/instances/{instanceId}/my-task", "bob");
        var taskId = myTask.GetProperty("task").GetProperty("id").GetGuid();
        using var copied = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{taskId}/approve", "bob", new
        {
            formData = new { shared = new[] { new { id = secret } } }
        });
        var problem = await copied.ProblemAsync(400);
        Assert.DoesNotContain("secret.pdf", problem.GetRawText());
        var state = await host.GetJsonAsync($"/api/approval/instances/{instanceId}", "alice");
        Assert.Equal("Running", state.GetProperty("status").GetString());
        Assert.DoesNotContain(state.GetProperty("logs").EnumerateArray(), log => log.GetProperty("action").GetString() == "FormEdit");
    }

    [Fact]
    public async Task CrossUserAttachmentValidationDoesNotDiscloseFileMetadata()
    {
        await using var host = await StartAsync();
        await host.PublishAsync("attachments", AttachmentFlow);
        var foreign = await UploadIdAsync(host, "confidential-name.pdf");

        async Task<string?> StartWith(Guid id)
        {
            using var result = await host.SendAsync(HttpMethod.Post, "/api/approval/forms/attachments/start", "bob",
                new { formData = new { shared = new[] { new { id } } } });
            return (await result.ProblemAsync(400)).GetProperty("errors").GetProperty("shared")[0].GetString();
        }

        var denied = await StartWith(foreign);
        Assert.Equal(await StartWith(Guid.NewGuid()), denied);
        Assert.DoesNotContain("confidential-name", denied);
    }

    [Fact]
    public async Task 上传后上传者可下载_响应强制附件下载_他人404()
    {
        await using var host = await StartAsync();

        var response = await UploadAsync(host, "alice", "发票 1.pdf", "%PDF");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var file = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("发票 1.pdf", file.GetProperty("name").GetString());
        var id = file.GetProperty("id").GetGuid();

        var download = await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{id}", "alice");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("%PDF", await download.Content.ReadAsStringAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("发票 1.pdf", download.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        await (await host.SendAsync(HttpMethod.Get, $"/api/approval/files/{id}", "bob")).ProblemAsync(404);
    }

    [Fact]
    public async Task 缺少文件名或类型不允许时返回400()
    {
        await using var host = await StartAsync();

        var missing = await (await UploadAsync(host, "alice", null, "x")).ProblemAsync(400);
        var exe = await (await UploadAsync(host, "alice", "a.exe", "x")).ProblemAsync(400);

        Assert.Contains("X-File-Name", missing.GetProperty("title").GetString());
        Assert.Contains("不支持的文件类型", exe.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OversizedExtensionReturnsBadRequestInsteadOfRangeException()
    {
        await using var host = await StartAsync();
        using var response = await UploadAsync(host, "alice", "report." + new string('p', 201), "%PDF");
        Assert.Contains("文件扩展名过长", (await response.ProblemAsync(400)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task 未启用表单审批时不映射附件接口()
    {
        await using var host = await ApiHost.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(host, "alice", "a.pdf", "x")).StatusCode);
    }
}
