using Approval.Forms;
using System.Net;
using System.Text;
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
    public async Task 未启用表单审批时不映射附件接口()
    {
        await using var host = await ApiHost.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(host, "alice", "a.pdf", "x")).StatusCode);
    }
}
