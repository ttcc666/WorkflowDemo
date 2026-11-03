using Approval.Core;
using Approval.Forms.Runtime;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Approval.Core.Abstractions;

namespace Approval.Forms.Attachments;

/// <summary>附件内容的存储（本地磁盘、OSS / S3 等），宿主可通过 <c>AddFileStore&lt;T&gt;()</c> 替换。</summary>
public interface IApprovalFileStore
{
    /// <summary>保存内容，返回存储键。读取 <paramref name="content"/> 时抛出异常需清理已写入的部分。</summary>
    Task<string> SaveAsync(Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    /// <summary>删除内容；不存在时忽略。</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

/// <summary>本地磁盘存储：按 <c>yyyy/MM/随机名</c> 保存（不含用户提供的文件名与扩展名）。</summary>
public sealed class LocalApprovalFileStore(ApprovalFormOptions options, TimeProvider clock, IHostEnvironment? environment = null) : IApprovalFileStore
{
    private readonly string _root = Path.GetFullPath(Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, options.Files.RootPath));

    public async Task<string> SaveAsync(Stream content, CancellationToken ct)
    {
        var key = $"{clock.GetUtcNow():yyyy/MM}/{Guid.NewGuid():N}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(PathOf(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        File.Delete(PathOf(storageKey));
        return Task.CompletedTask;
    }

    /// <summary>存储键来自数据库，仍校验解析后的路径位于根目录内，防止路径穿越。</summary>
    private string PathOf(string key)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^(?:tenants/[0-9a-f]{64}/)?[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ApprovalException("无效的存储键");
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalException("无效的存储键");
        return path;
    }
}

/// <summary>读取时计算 SHA-256 并限制总字节数，超过时抛出 <see cref="ApprovalException"/>。</summary>
internal sealed class LimitedHashingStream(Stream inner, long maxBytes, string tooLargeMessage) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long BytesRead { get; private set; }

    public string HashHex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override int Read(byte[] buffer, int offset, int count) => Track(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Track((await inner.ReadAsync(buffer, ct) is var n ? buffer[..n] : default).Span);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    private int Track(ReadOnlySpan<byte> data)
    {
        BytesRead += data.Length;
        if (BytesRead > maxBytes) throw new ApprovalException(tooLargeMessage);
        _hash.AppendData(data);
        return data.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
