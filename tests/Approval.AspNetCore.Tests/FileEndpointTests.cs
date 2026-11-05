using Approval.Forms;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Approval.Forms.Attachments;

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

    private static async Task<Guid> UploadIdAsync(ApiHost host, string name)
    {
        using var upload = await UploadAsync(host, "alice", name, "%PDF");
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
