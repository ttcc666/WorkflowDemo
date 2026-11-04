namespace Approval.Forms.Attachments;

internal sealed class MissingApprovalFileStore : IApprovalFileStore
{
    private static InvalidOperationException Missing() => new("附件上传/下载需要宿主显式注册 IApprovalFileStore");

    public Task<string> SaveAsync(Stream content, CancellationToken ct) => Task.FromException<string>(Missing());

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromException<Stream>(Missing());

    public Task DeleteAsync(string key, CancellationToken ct) => Task.FromException(Missing());
}
