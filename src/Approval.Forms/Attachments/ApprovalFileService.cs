using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Forms.Runtime;
using Approval.Forms.Schema;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace Approval.Forms.Attachments;

/// <summary>
/// 表单附件：上传（大小、扩展名白名单、文件名清洗、SHA-256）、按权限下载、提交表单时校验并绑定到实例 / 草稿。
/// 下载权限：上传者；或文件已绑定实例、查看者能查看该实例，且文件出现在查看者可见的字段中（隐藏字段中的附件不可下载）。
/// </summary>
public sealed class ApprovalFileService(
    ApprovalDbContextAccessor accessor,
    IApprovalFileStore store,
    ApprovalQueryService query,
    ApprovalFormOptions options,
    TimeProvider clock)
{
    private const int MaxFileNameLength = 200;

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

    private readonly Approval.Core.Authorization.ApprovalAuthorizationService _authorization = ApprovalFormsBridge.Authorization(accessor);
    private readonly DbContext _db = accessor.Context;
    private readonly ApprovalFormsBridge _bridge = new(accessor);

    /// <summary>保存上传的文件（未绑定，只有上传者可用）。超过大小、扩展名不允许时抛出 <see cref="ApprovalException"/>。</summary>
    public async Task<ApprovalFile> UploadAsync(string userId, string fileName, Stream content, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.FileUpload, new(), userId, ct);
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
        using var authority = _bridge.WriteMetadata();
        _db.Set<ApprovalFile>().Add(file);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            _db.Entry(file).State = EntityState.Detached;
            await store.DeleteAsync(key, ct);
            throw;
        }
        return file;
    }

    /// <summary>打开文件供下载；不存在或无权访问时返回 null（接口统一返回 404）。</summary>
    public async Task<(ApprovalFile File, Stream Content)?> OpenAsync(Guid fileId, ApprovalViewer viewer, CancellationToken ct = default)
    {
        var file = await _db.Set<ApprovalFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return null;
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.FileRead, new() { FileId = fileId }, viewer.UserId, ct);
        viewer = await _bridge.ViewerAsync(viewer, file.InstanceId, ct);
        if (!await CanDownloadAsync(file, viewer, ct)) return null;
        return (file, await store.OpenReadAsync(file.StorageKey, ct));
    }

    private async Task<bool> CanDownloadAsync(ApprovalFile file, ApprovalViewer viewer, CancellationToken ct)
    {
        if (file.UploadedBy == viewer.UserId) return true;
        if (file.InstanceId is not { } instanceId) return false;

        if (await query.GetInstanceForViewerAsync(instanceId, viewer, ct) is not { } detail) return false;
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.Id == detail.DefinitionId)
            .Select(d => d.DefinitionJson).SingleAsync(ct);
        return FlowDefinition.Parse(definition).GetForm() is { } schema
            && VisibleFileIds(schema, detail, includeHistory: true).Contains(file.Id);
    }

    private static HashSet<Guid> VisibleFileIds(FormSchema schema, InstanceDetail detail, bool includeHistory)
    {
        var hidden = detail.HiddenFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = schema.Fields.Where(f => f.Type == FormFieldType.File && !hidden.Contains(f.Key))
            .Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var form = JsonNode.Parse(detail.FormDataJson) as JsonObject;
        var ids = keys.SelectMany(key => FileIds(form?[key])).ToHashSet();
        if (!includeHistory) return ids;
        foreach (var log in detail.Logs)
        {
            if (log.Action != ApprovalAction.FormEdit || string.IsNullOrEmpty(log.Details) || JsonNode.Parse(log.Details) is not JsonArray changes) continue;
            foreach (var change in changes.OfType<JsonObject>())
            {
                if (change["field"] is not JsonValue value || !value.TryGetValue<string>(out var key) || !keys.Contains(key)) continue;
                ids.UnionWith(FileIds(change["before"]));
                ids.UnionWith(FileIds(change["after"]));
            }
        }
        return ids;
    }

    private static IEnumerable<Guid> FileIds(JsonNode? value)
    {
        if (value is not JsonArray files) yield break;
        foreach (var item in files.OfType<JsonObject>())
            if (item["id"] is JsonValue id && id.TryGetValue<string>(out var text) && Guid.TryParse(text, out var fileId))
                yield return fileId;
    }

    /// <summary>
    /// 校验表单中引用的附件并补全元数据。可使用本人未绑定实例且未绑定其他草稿的上传，或保留本实例同一字段中的原引用。
    /// 本实例已绑定附件新增到某字段时，须出现在操作者当前可见的附件字段中。类型须在字段允许范围内。
    /// <paramref name="lenient"/>（草稿）时丢弃无效的文件项，否则抛出字段错误。
    /// 返回规范化后的数据与引用的文件（已跟踪，可修改绑定）。
    /// </summary>
    internal async Task<(string Json, List<ApprovalFile> Files)> ResolveAsync(FormSchema schema, string json, string userId,
        Guid? instanceId, Guid? draftId, bool lenient, CancellationToken ct, string? previousFormJson = null)
    {
        var fields = schema.Fields.Where(f => f.Type == FormFieldType.File).ToList();
        if (fields.Count == 0 || JsonNode.Parse(json) is not JsonObject form) return (json, []);

        var ids = fields.SelectMany(f => FileIds(form[f.Key]))
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var found = ids.Count == 0 ? [] : await _db.Set<ApprovalFile>().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var inaccessible = new HashSet<Guid>();
        if (instanceId is { } current)
        {
            // History can grant download access. A new field reference needs a currently visible source.
            var before = previousFormJson is null ? null : JsonNode.Parse(previousFormJson) as JsonObject;
            inaccessible.UnionWith(fields.SelectMany(f => FileIds(form[f.Key]).Except(FileIds(before?[f.Key])))
                .Where(id => found.TryGetValue(id, out var file) && file.InstanceId == current));
            if (inaccessible.Count > 0 && await query.GetInstanceForViewerAsync(current, new(userId, false), ct) is { } detail)
                inaccessible.ExceptWith(VisibleFileIds(schema, detail, includeHistory: false));
        }

        var errors = new Dictionary<string, string[]>();
        var used = new List<ApprovalFile>();
        foreach (var field in fields)
        {
            var items = new JsonArray();
            var problems = new List<string>();
            foreach (var item in form[field.Key] as JsonArray ?? [])
            {
                var id = Guid.TryParse(item?["id"]?.GetValue<string>(), out var g) ? g : Guid.Empty;
                var problem = !found.TryGetValue(id, out var file) || !Usable(file, userId, instanceId, draftId) || inaccessible.Contains(id)
                    ? "附件不存在或不可用"
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
        (
        (instanceId is not null && file.InstanceId == instanceId)
        || (file.UploadedBy == userId && file.InstanceId is null && (file.DraftId is null || file.DraftId == draftId)));

    private static bool Accepts(FormField field, string fileName) =>
        field.Accept.Count == 0 || field.Accept.Contains(Path.GetExtension(fileName).TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    /// <summary>草稿引用的文件绑定到草稿，不再引用的解除绑定；存储生命周期由宿主管理。</summary>
    internal async Task BindToDraftAsync(Guid draftId, IReadOnlyCollection<ApprovalFile> files, CancellationToken ct)
    {
        using var authority = _bridge.WriteMetadata();
        foreach (var file in await _db.Set<ApprovalFile>().Where(f => f.DraftId == draftId).ToListAsync(ct))
            if (!files.Contains(file)) file.DraftId = null;
        foreach (var file in files) file.DraftId = draftId;
    }

    /// <summary>
    /// 当前引用绑定到实例。已移除的文件保留原实例归属，以支持可见历史和阻止跨实例复用。
    /// 从草稿提交时，未引用的草稿文件解除草稿绑定。这里仅修改事务内的元数据。
    /// </summary>
    internal async Task BindToInstanceAsync(Guid instanceId, IReadOnlyCollection<ApprovalFile> files, Guid? fromDraftId, CancellationToken ct)
    {
        using var authority = _bridge.WriteMetadata();
        if (fromDraftId is { } draftId) await BindToDraftAsync(draftId, [], ct);
        foreach (var file in files)
        {
            file.InstanceId = instanceId;
            file.DraftId = null;
        }
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
            if (extension.Length >= MaxFileNameLength) throw new ApprovalException("文件扩展名过长");
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }
        return name.Length == 0 ? "file" : name;
    }
}
