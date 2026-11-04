using Approval.Forms;
using Approval.Forms.Runtime;
using Approval.AspNetCore;
using Approval.AspNetCore.Endpoints;
using Approval.AspNetCore.Contracts;
using Approval.Core;
using Approval.Core.Engine;
using Approval.Forms.Attachments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Approval.Forms.AspNetCore.Endpoints;

/// <summary>表单审批：发起中心、发起页、草稿、提交、附件（需 <c>AddApprovalForms()</c>）。</summary>
internal static class FormEndpoints
{
    public const string FileNameHeader = "X-File-Name";

    public static void Map(RouteGroupBuilder group, ApprovalFormOptions options)
    {
        // 上传附件：请求体为文件内容，文件名放在请求头 X-File-Name（URL 编码）。
        // 必须带自定义请求头：浏览器跨站请求带自定义头会触发 CORS 预检，因此无法被跨站伪造上传
        group.MapPost("/files", async (HttpContext http, ApprovalFileService files, CancellationToken ct) =>
        {
            if (http.Request.Headers[FileNameHeader].ToString() is not { Length: > 0 } name)
                throw new ApprovalException($"缺少文件名（请求头 {FileNameHeader}）");
            if (http.Request.ContentLength > options.Files.MaxFileSizeBytes)
                throw new ApprovalException($"文件不能超过 {options.Files.MaxFileSizeMb} MB");
            var file = await files.UploadAsync(http.GetApprovalUserId(), Uri.UnescapeDataString(name), http.Request.Body, ct);
            return TypedResults.Ok(new ApprovalFileResponse(file.Id, file.FileName, file.Size, file.ContentType));
        }).WithMetadata(new RequestSizeLimitAttribute(options.Files.MaxFileSizeBytes + 64 * 1024));

        // 下载附件：上传者，或能查看所属审批且附件不在隐藏字段中；一律作为附件下载，禁止浏览器嗅探类型
        group.MapGet("/files/{id:guid}", async (Guid id, HttpContext http, ApprovalFileService files, CancellationToken ct) =>
        {
            var (file, content) = await files.OpenAsync(id, await http.GetApprovalViewerAsync(ct), ct)
                                  ?? throw new ApprovalException("附件不存在或无权下载") { Code = ApprovalErrorCode.NotFound };
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return TypedResults.File(content, file.ContentType, file.FileName);
        }).Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");

        group.MapGet("/forms/startable", (HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.ListStartableAsync(http.GetApprovalUserId(), ct));

        // 草稿：只能读写自己的；他人的草稿与不存在一样返回 404
        group.MapGet("/forms/drafts", (HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.ListDraftsAsync(http.GetApprovalUserId(), ct));

        group.MapGet("/forms/drafts/{id:guid}", (Guid id, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.GetDraftAsync(http.GetApprovalUserId(), id, ct));

        group.MapDelete("/forms/drafts/{id:guid}", async (Guid id, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
        {
            await forms.DeleteDraftAsync(http.GetApprovalUserId(), id, ct);
            return TypedResults.NoContent();
        });

        group.MapPut("/forms/{key}/draft", async (string key, SaveDraftRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            var draft = await forms.SaveDraftAsync(http.GetApprovalUserId(), key, body.DraftId, body.FormData, body.Comment, ct);
            return TypedResults.Ok(new ApprovalDraftSavedResponse(draft.Id, draft.UpdatedAt));
        });

        group.MapGet("/forms/{key}", (string key, HttpContext http, ApprovalFormService forms, CancellationToken ct) =>
            forms.GetStartFormAsync(key, http.GetApprovalUserId(), ct));

        // 校验失败返回 400 + errors（字段路径 → 错误），前端回显到对应表单项；带 draftId 时提交成功后删除草稿
        group.MapPost("/forms/{key}/start", async (string key, StartFormRequest body, HttpContext http, ApprovalFormService forms,
            CancellationToken ct) =>
        {
            var instance = await forms.StartAsync(key, http.GetApprovalUserId(), body.FormData, body.Comment, body.DraftId, ct);
            return TypedResults.Ok(new ApprovalStartedResponse(instance.Id));
        });
    }
}
