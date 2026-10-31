using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Forms;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Files;

/// <summary>
/// 表单附件：上传（大小、扩展名白名单、文件名清洗、SHA-256）、按权限下载、提交表单时校验并绑定到实例 / 草稿、清理过期的未绑定文件。
/// 下载权限：上传者；或文件已绑定实例、查看者能查看该实例，且文件出现在查看者可见的字段中（隐藏字段中的附件不可下载）。
/// </summary>
public sealed class ApprovalFileService(
    ApprovalDbContextAccessor accessor,
    IApprovalFileStore store,
    ApprovalQueryService query,
    ApprovalOptions options,
    TimeProvider clock)
{
    private const int MaxFileNameLength = 200;
    private const int CleanupBatchSize = 100;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["txt"] = "text/plain",
        ["csv"] = "text/csv",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["zip"] = "application/zip"
    };

    private readonly DbContext _db = accessor.Context;

    /// <summary>保存上传的文件（未绑定，只有上传者可用）。超过大小、扩展名不允许时抛出 <see cref="ApprovalException"/>。</summary>
    public async Task<ApprovalFile> UploadAsync(string userId, string fileName, Stream content, CancellationToken ct = default)
    {
        var name = SanitizeFileName(fileName);
        var extension = Path.GetExtension(name).TrimStart('.');
        if (extension.Length == 0 || !options.Files.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ApprovalException($"不支持的文件类型，允许：{string.Join("、", options.Files.AllowedExtensions)}");

        await using var limited = new LimitedHashingStream(content, options.Files.MaxFileSizeBytes, $"文件不能超过 {options.Files.MaxFileSizeMb} MB");
        var key = await store.SaveAsync(limited, ct);
        if (limited.BytesRead == 0)
        {
            await store.DeleteAsync(key, ct);
            throw new ApprovalException("文件内容为空");
        }

        var file = new ApprovalFile
        {
            Id = Guid.NewGuid(),
            FileName = name,
            ContentType = ContentTypes.GetValueOrDefault(extension, "application/octet-stream"),
            Size = limited.BytesRead,
            Sha256 = limited.HashHex(),
            StorageKey = key,
            UploadedBy = userId,
            UploadedAt = clock.GetUtcNow().UtcDateTime
        };
        _db.Set<ApprovalFile>().Add(file);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            await store.DeleteAsync(key, ct);
            throw;
        }
        return file;
    }

    /// <summary>打开文件供下载；不存在或无权访问时返回 null（接口统一返回 404）。</summary>
    public async Task<(ApprovalFile File, Stream Content)?> OpenAsync(Guid fileId, ApprovalViewer viewer, CancellationToken ct = default)
    {
        var file = await _db.Set<ApprovalFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || !await CanDownloadAsync(file, viewer, ct)) return null;
        return (file, await store.OpenReadAsync(file.StorageKey, ct));
    }

    private async Task<bool> CanDownloadAsync(ApprovalFile file, ApprovalViewer viewer, CancellationToken ct)
    {
        if (file.UploadedBy == viewer.UserId) return true;
        if (file.InstanceId is not { } instanceId) return false;

        // 按查看者过滤后的详情（已去掉隐藏字段）：文件须出现在可见的表单数据或修改记录中
        if (await query.GetInstanceForViewerAsync(instanceId, viewer, ct) is not { } detail) return false;
        if (detail.HiddenFields.Count == 0) return true;
        var id = file.Id.ToString();
        return detail.FormDataJson.Contains(id, StringComparison.OrdinalIgnoreCase)
               || detail.Logs.Any(l => l.Details?.Contains(id, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// 校验表单中引用的附件并补全元数据：文件须存在、由 <paramref name="userId"/> 上传且未绑定到其他实例 / 草稿
    /// （已属于 <paramref name="instanceId"/> 或 <paramref name="draftId"/> 的除外，可由他人上传，如重新提交、审批人修改时保留的附件），
    /// 类型须在字段允许的范围内。<paramref name="lenient"/>（草稿）时丢弃无效的文件项，否则抛出字段错误。
    /// 返回规范化后的数据与引用的文件（已跟踪，可修改绑定）。
    /// </summary>
    internal async Task<(string Json, List<ApprovalFile> Files)> ResolveAsync(FormSchema schema, string json, string userId,
        Guid? instanceId, Guid? draftId, bool lenient, CancellationToken ct)
    {
        var fields = schema.Fields.Where(f => f.Type == FormFieldType.File).ToList();
        if (fields.Count == 0 || JsonNode.Parse(json) is not JsonObject form) return (json, []);

        var ids = fields.SelectMany(f => form[f.Key] as JsonArray ?? [])
            .Select(i => Guid.TryParse(i?["id"]?.GetValue<string>(), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var found = ids.Count == 0 ? [] : await _db.Set<ApprovalFile>().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var errors = new Dictionary<string, string[]>();
        var used = new List<ApprovalFile>();
        foreach (var field in fields)
        {
            var items = new JsonArray();
            var problems = new List<string>();
            foreach (var item in form[field.Key] as JsonArray ?? [])
            {
                var id = Guid.TryParse(item?["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty;
                var problem = !found.TryGetValue(id, out var file) ? "附件不存在"
                    : !Usable(file, userId, instanceId, draftId) ? $"附件「{file.FileName}」不可用"
                    : !Accepts(field, file.FileName) ? $"附件「{file.FileName}」的类型不在允许范围（{string.Join("、", field.Accept)}）"
                    : null;
                if (problem is not null)
                {
                    problems.Add(problem);
                    continue;
                }
                items.Add(new JsonObject { ["id"] = file!.Id.ToString(), ["name"] = file.FileName, ["size"] = file.Size, ["contentType"] = file.ContentType });
                if (!used.Contains(file)) used.Add(file);
            }
            if (problems.Count > 0 && !lenient) errors[field.Key] = [.. problems];
            form[field.Key] = items;
        }
        if (errors.Count > 0) throw new ApprovalValidationException(errors, "附件有误，请重新上传");
        return (form.ToJsonString(FormData.JsonOptions), used);
    }

    private static bool Usable(ApprovalFile file, string userId, Guid? instanceId, Guid? draftId) =>
        (instanceId is not null && file.InstanceId == instanceId)
        || (file.UploadedBy == userId && file.InstanceId is null && (file.DraftId is null || file.DraftId == draftId));

    private static bool Accepts(FormField field, string fileName) =>
        field.Accept.Count == 0 || field.Accept.Contains(Path.GetExtension(fileName).TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    /// <summary>草稿引用的文件绑定到草稿，不再引用的解除绑定（过期后清理）。</summary>
    internal async Task BindToDraftAsync(Guid draftId, IReadOnlyCollection<ApprovalFile> files, CancellationToken ct)
    {
        foreach (var file in await _db.Set<ApprovalFile>().Where(f => f.DraftId == draftId).ToListAsync(ct))
            if (!files.Contains(file)) file.DraftId = null;
        foreach (var file in files) file.DraftId = draftId;
    }

    /// <summary>引用的文件绑定到实例；从草稿提交时，草稿中未引用的文件解除绑定。</summary>
    internal async Task BindToInstanceAsync(Guid instanceId, IReadOnlyCollection<ApprovalFile> files, Guid? fromDraftId, CancellationToken ct)
    {
        if (fromDraftId is { } draftId) await BindToDraftAsync(draftId, [], ct);
        foreach (var file in files)
        {
            file.InstanceId = instanceId;
            file.DraftId = null;
        }
    }

    /// <summary>删除超过保留时间仍未绑定的文件（内容与记录），返回删除数量；由超时扫描后台任务每轮调用。</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var before = clock.GetUtcNow().UtcDateTime - options.Files.UnboundRetention;
        var files = await _db.Set<ApprovalFile>()
            .Where(f => f.InstanceId == null && f.DraftId == null && f.UploadedAt < before)
            .OrderBy(f => f.UploadedAt)
            .Take(CleanupBatchSize)
            .ToListAsync(ct);
        foreach (var file in files)
        {
            await store.DeleteAsync(file.StorageKey, ct);
            _db.Set<ApprovalFile>().Remove(file);
        }
        await _db.SaveChangesAsync(ct);
        return files.Count;
    }

    /// <summary>只保留文件名部分，去掉控制字符与路径字符，限制长度（保留扩展名）。</summary>
    internal static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/').Split('/').Last());
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToHashSet();
        name = new string(name.Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim().Trim('.');
        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }
        return name.Length == 0 ? "file" : name;
    }
}
