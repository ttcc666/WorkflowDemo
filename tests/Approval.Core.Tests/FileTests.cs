using System.Text;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Files;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

/// <summary>内存附件存储（测试用）。</summary>
public sealed class InMemoryFileStore : IApprovalFileStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task<string> SaveAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var key = Guid.NewGuid().ToString("N");
        Files[key] = buffer.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Files[storageKey]));

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        Files.Remove(storageKey);
        return Task.CompletedTask;
    }
}

/// <summary>表单附件：上传限制、提交时校验与绑定、下载权限（含隐藏字段）、草稿、清理。</summary>
public sealed class FileTests : IAsyncLifetime
{
    /// <summary>审核（dave，可修改发票）→ 抄送（carol，隐藏发票）→ 结束。</summary>
    private const string Flow = """
    {
      "startNodeId": "start",
      "settings": { "startable": true },
      "form": { "fields": [
        { "key": "title", "label": "标题", "type": "Text" },
        { "key": "invoices", "label": "发票", "type": "File", "maxFiles": 2, "accept": ["pdf", "png"] }
      ] },
      "nodes": {
        "start":  { "name": "提交", "type": "Start", "next": "review" },
        "review": { "name": "审核", "type": "Approval", "approvers": [{ "type": "User", "value": "dave" }],
                    "fieldPermissions": { "invoices": "Editable" }, "next": "cc" },
        "cc":     { "name": "抄送", "type": "Cc", "approvers": [{ "type": "User", "value": "carol" }],
                    "fieldPermissions": { "invoices": "Hidden" }, "next": "end" },
        "end":    { "name": "结束", "type": "End" }
      }
    }
    """;

    private static readonly InMemoryFileStore Store = new();

    private readonly TestHost _host = new(o => o.Files.MaxFileSizeMb = 1, b => b.AddFormApprovals().AddFileStore<SharedStore>());

    /// <summary>每个测试一个存储实例的包装（DI 按类型创建）。</summary>
    public sealed class SharedStore : IApprovalFileStore
    {
        public Task<string> SaveAsync(Stream content, CancellationToken ct) => Store.SaveAsync(content, ct);
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) => Store.OpenReadAsync(storageKey, ct);
        public Task DeleteAsync(string storageKey, CancellationToken ct) => Store.DeleteAsync(storageKey, ct);
    }

    public Task InitializeAsync() => _host.PublishAsync("expense", Flow);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<ApprovalFile> UploadAsync(string user, string name, string content = "%PDF-1.4 demo") =>
        _host.RunAsync(sp => sp.GetRequiredService<ApprovalFileService>().UploadAsync(user, name, new MemoryStream(Encoding.UTF8.GetBytes(content))));

    private static JsonElement Form(params ApprovalFile[] files) =>
        JsonSerializer.SerializeToElement(new { title = "报销", invoices = files.Select(f => new { id = f.Id, name = "伪造.exe", size = 1 }) });

    private Task<ApprovalInstance> StartAsync(string user, params ApprovalFile[] files) =>
        _host.RunAsync(sp => sp.GetRequiredService<ApprovalFormService>().StartAsync("expense", user, Form(files)));

    private Task<ApprovalFile> ReloadAsync(Guid id) =>
        _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalFile>().AsNoTracking().SingleAsync(f => f.Id == id));

    private Task<bool> CanDownloadAsync(Guid id, string user, bool admin = false) =>
        _host.RunAsync(async sp =>
        {
            var opened = await sp.GetRequiredService<ApprovalFileService>().OpenAsync(id, new ApprovalViewer(user, admin));
            if (opened is { } o) await o.Content.DisposeAsync();
            return opened is not null;
        });

    [Fact]
    public async Task 上传_清洗文件名_按扩展名确定类型_计算哈希()
    {
        var file = await UploadAsync("alice", "..\\..\\etc/发票<1>.PDF", "abc");

        Assert.Equal("发票1.PDF", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(3, file.Size);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", file.Sha256);
        Assert.Equal("abc", Encoding.UTF8.GetString(Store.Files[file.StorageKey]));
    }

    [Fact]
    public async Task 上传_扩展名不允许或超过大小时拒绝且不留下文件()
    {
        var before = Store.Files.Count;

        var type = await Assert.ThrowsAsync<ApprovalException>(() => UploadAsync("alice", "run.exe"));
        var noExtension = await Assert.ThrowsAsync<ApprovalException>(() => UploadAsync("alice", "README"));
        var large = await Assert.ThrowsAsync<ApprovalException>(() => UploadAsync("alice", "big.pdf", new string('x', 1024 * 1024 + 1)));

        Assert.Contains("不支持的文件类型", type.Message);
        Assert.Contains("不支持的文件类型", noExtension.Message);
        Assert.Equal("文件不能超过 1 MB", large.Message);
        Assert.Equal(before, Store.Files.Count);
    }

    [Fact]
    public async Task 提交时以服务端元数据为准并绑定到实例()
    {
        var file = await UploadAsync("alice", "发票.pdf");

        var instance = await StartAsync("alice", file);

        var item = JsonDocument.Parse(instance.FormDataJson).RootElement.GetProperty("invoices")[0];
        Assert.Equal("发票.pdf", item.GetProperty("name").GetString());
        Assert.Equal(file.Size, item.GetProperty("size").GetInt64());
        Assert.Equal(instance.Id, (await ReloadAsync(file.Id)).InstanceId);
    }

    [Fact]
    public async Task 他人的文件_已用于其他申请的文件_类型不符的文件不能提交()
    {
        var bobs = await UploadAsync("bob", "b.pdf");
        var used = await UploadAsync("alice", "a.pdf");
        await StartAsync("alice", used);
        var image = await UploadAsync("alice", "x.jpg");

        foreach (var file in new[] { bobs, used, image })
        {
            var ex = await Assert.ThrowsAsync<ApprovalValidationException>(() => StartAsync("alice", file));
            Assert.True(ex.Errors.ContainsKey("invoices"));
        }
        var tooMany = await Assert.ThrowsAsync<ApprovalValidationException>(async () =>
            await StartAsync("alice", await UploadAsync("alice", "1.pdf"), await UploadAsync("alice", "2.pdf"), await UploadAsync("alice", "3.pdf")));
        Assert.Equal(["发票最多上传 2 个文件"], tooMany.Errors["invoices"]);
    }

    [Fact]
    public async Task 下载权限_未绑定只有上传者_绑定后按实例权限_隐藏字段不可下载()
    {
        var file = await UploadAsync("alice", "发票.pdf");
        Assert.True(await CanDownloadAsync(file.Id, "alice"));
        Assert.False(await CanDownloadAsync(file.Id, "dave"));

        await StartAsync("alice", file);
        Assert.True(await CanDownloadAsync(file.Id, "dave"));                 // 审核人
        Assert.True(await CanDownloadAsync(file.Id, "root", admin: true));
        Assert.False(await CanDownloadAsync(file.Id, "outsider"));
        Assert.False(await CanDownloadAsync(Guid.NewGuid(), "alice"));

        await _host.ApproveMyTaskAsync("dave");
        Assert.False(await CanDownloadAsync(file.Id, "carol"));               // 抄送节点隐藏了发票
    }

    [Fact]
    public async Task 审批人修改附件字段_保留原附件并绑定新上传的()
    {
        var original = await UploadAsync("alice", "a.pdf");
        var instance = await StartAsync("alice", original);
        var added = await UploadAsync("dave", "补充.png");
        var task = Assert.Single(await _host.TodoAsync("dave")).TaskId;

        await _host.RunAsync(sp => sp.GetRequiredService<ApprovalFormService>().ApproveAsync(task, "dave",
            JsonSerializer.SerializeToElement(new { invoices = new[] { new { id = original.Id }, new { id = added.Id } } })));

        var form = JsonDocument.Parse((await _host.GetInstanceAsync(instance.Id)).FormDataJson).RootElement;
        Assert.Equal(["a.pdf", "补充.png"], form.GetProperty("invoices").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(instance.Id, (await ReloadAsync(added.Id)).InstanceId);
    }

    [Fact]
    public async Task 草稿绑定附件_删除草稿后解除绑定_从草稿提交后绑定到实例()
    {
        var forms = (IServiceProvider sp) => sp.GetRequiredService<ApprovalFormService>();
        var kept = await UploadAsync("alice", "kept.pdf");
        var dropped = await UploadAsync("alice", "dropped.pdf");
        var bobs = await UploadAsync("bob", "bob.pdf");

        var draft = await _host.RunAsync(sp => forms(sp).SaveDraftAsync("alice", "expense", null, Form(kept, dropped, bobs), null));
        Assert.Equal(draft.Id, (await ReloadAsync(kept.Id)).DraftId);
        Assert.Equal(2, JsonDocument.Parse(draft.FormDataJson).RootElement.GetProperty("invoices").GetArrayLength());   // 他人的文件被丢弃

        await _host.RunAsync(sp => forms(sp).SaveDraftAsync("alice", "expense", draft.Id, Form(kept), null));
        Assert.Null((await ReloadAsync(dropped.Id)).DraftId);

        var instance = await _host.RunAsync(sp => forms(sp).StartAsync("expense", "alice", Form(kept), draftId: draft.Id));
        Assert.Equal((instance.Id, (Guid?)null), ((await ReloadAsync(kept.Id)).InstanceId!.Value, (await ReloadAsync(kept.Id)).DraftId));

        var other = await UploadAsync("alice", "other.pdf");
        var second = await _host.RunAsync(sp => forms(sp).SaveDraftAsync("alice", "expense", null, Form(other), null));
        await _host.RunAsync(sp => forms(sp).DeleteDraftAsync("alice", second.Id));
        Assert.Null((await ReloadAsync(other.Id)).DraftId);
    }

    [Fact]
    public async Task 清理只删除过期且未绑定的文件()
    {
        var stale = await UploadAsync("alice", "stale.pdf");
        var bound = await UploadAsync("alice", "bound.pdf");
        await StartAsync("alice", bound);
        _host.Clock.Advance(TimeSpan.FromHours(25));
        var fresh = await UploadAsync("alice", "fresh.pdf");

        await _host.ScanTimeoutsAsync();

        var remaining = await _host.RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalFile>().Select(f => f.Id).ToListAsync());
        Assert.DoesNotContain(stale.Id, remaining);
        Assert.False(Store.Files.ContainsKey(stale.StorageKey));
        Assert.Contains(bound.Id, remaining);
        Assert.Contains(fresh.Id, remaining);
    }

    [Fact]
    public async Task 本地存储按年月保存并拒绝路径穿越()
    {
        var root = Path.Combine(Path.GetTempPath(), "approval-files-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ApprovalOptions();
            options.Files.RootPath = root;
            var store = new LocalApprovalFileStore(options, _host.Clock);

            var key = await store.SaveAsync(new MemoryStream([1, 2, 3]), default);
            await using (var read = await store.OpenReadAsync(key, default))
                Assert.Equal(3, read.Length);
            await store.DeleteAsync(key, default);

            Assert.StartsWith("2026/01/", key);
            Assert.False(File.Exists(Path.Combine(root, key)));
            await Assert.ThrowsAsync<ApprovalException>(() => store.OpenReadAsync("../../secret", default));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
