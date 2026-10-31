using System.Text;
using System.Text.Json;
using Approval.Core.Domain;
using Approval.Core.Forms;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>草稿列表项。</summary>
public sealed record DraftSummary(Guid Id, string DefinitionKey, string DefinitionName, string? Comment, DateTime CreatedAt, DateTime UpdatedAt);

/// <param name="FormDataJson">按最新表单宽松规范化后的数据。</param>
/// <param name="Adjusted">流程已更新、草稿数据按最新表单做了调整（字段增删、类型变化）。</param>
public sealed record DraftDetail(
    Guid Id,
    string DefinitionKey,
    string DefinitionName,
    string FormDataJson,
    string? Comment,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Adjusted);

// 草稿：保存时不校验必填，只按最新表单做结构规范化；每人数量与大小有上限；提交时随发起一起删除
public sealed partial class ApprovalFormService
{
    public const int MaxDraftsPerUser = 50;
    public const int MaxDraftBytes = 256 * 1024;

    /// <summary>保存草稿（draftId 为空时新建），返回保存后的草稿。</summary>
    public async Task<ApprovalDraft> SaveDraftAsync(string userId, string key, Guid? draftId, JsonElement formData, string? comment,
        CancellationToken ct = default)
    {
        var (definition, flow) = await LoadStartableAsync(key, userId, ct);
        var schema = flow.Form ?? new FormSchema();
        var json = FormDataValidator.NormalizeLenient(schema, formData);
        if (Encoding.UTF8.GetByteCount(json) > MaxDraftBytes) throw new ApprovalException($"草稿内容超过 {MaxDraftBytes / 1024} KB，无法保存");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (comment is { Length: > 500 }) comment = comment[..500];

        var now = clock.GetUtcNow().UtcDateTime;
        ApprovalDraft draft;
        if (draftId is { } id)
        {
            draft = await FindDraftAsync(userId, id, ct);
            if (!string.Equals(draft.DefinitionKey, definition.Key, StringComparison.Ordinal))
                throw new ApprovalException("草稿不属于该流程");
        }
        else
        {
            if (await _db.Set<ApprovalDraft>().CountAsync(d => d.UserId == userId, ct) >= MaxDraftsPerUser)
                throw new ApprovalException($"草稿最多保存 {MaxDraftsPerUser} 份，请先删除不需要的草稿");
            draft = new ApprovalDraft { Id = Guid.NewGuid(), UserId = userId, DefinitionKey = definition.Key, CreatedAt = now };
            _db.Set<ApprovalDraft>().Add(draft);
        }

        // 附件随草稿绑定：无效的（不存在、他人的、已用于其他申请的）直接丢弃
        var (resolved, attachments) = await files.ResolveAsync(schema, json, userId, null, draft.Id, true, ct);
        await files.BindToDraftAsync(draft.Id, attachments, ct);
        draft.FormDataJson = resolved;
        draft.Comment = comment;
        draft.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return draft;
    }

    /// <summary>我的草稿（最近修改的在前）；流程已停用发起的草稿也列出，打开时提示。</summary>
    public async Task<List<DraftSummary>> ListDraftsAsync(string userId, CancellationToken ct = default)
    {
        var drafts = await _db.Set<ApprovalDraft>().AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);
        var keys = drafts.Select(d => d.DefinitionKey).Distinct().ToList();
        var names = await LatestDefinitions().Where(d => keys.Contains(d.Key)).ToDictionaryAsync(d => d.Key, d => d.Name, ct);
        return drafts.Select(d => new DraftSummary(d.Id, d.DefinitionKey, names.GetValueOrDefault(d.DefinitionKey, d.DefinitionKey), d.Comment,
            d.CreatedAt, d.UpdatedAt)).ToList();
    }

    /// <summary>打开草稿：按最新版本的表单宽松规范化（保留可用的值）；只能打开自己的草稿。</summary>
    public async Task<DraftDetail> GetDraftAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var draft = await FindDraftAsync(userId, id, ct, tracking: false);
        var (definition, flow) = await LoadStartableAsync(draft.DefinitionKey, userId, ct);
        using var document = JsonDocument.Parse(draft.FormDataJson);
        var schema = flow.Form ?? new FormSchema();
        // 附件项按上传记录补全名称、大小（规范化只保留 ID）
        var (json, _) = await files.ResolveAsync(schema, FormDataValidator.NormalizeLenient(schema, document.RootElement), userId, null,
            draft.Id, lenient: true, ct);
        return new DraftDetail(draft.Id, draft.DefinitionKey, definition.Name, json, draft.Comment, draft.CreatedAt, draft.UpdatedAt,
            Adjusted: json != draft.FormDataJson);
    }

    public async Task DeleteDraftAsync(string userId, Guid id, CancellationToken ct = default)
    {
        _db.Set<ApprovalDraft>().Remove(await FindDraftAsync(userId, id, ct));
        // 草稿中的附件解除绑定，过期后清理
        await files.BindToDraftAsync(id, [], ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>他人的草稿与不存在一样返回 404。</summary>
    private async Task<ApprovalDraft> FindDraftAsync(string userId, Guid id, CancellationToken ct, bool tracking = true)
    {
        var query = tracking ? _db.Set<ApprovalDraft>() : _db.Set<ApprovalDraft>().AsNoTracking();
        return await query.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct)
               ?? throw new ApprovalException("草稿不存在") { Code = ApprovalErrorCode.NotFound };
    }
}
