using Approval.Core;
using Approval.Forms.Attachments;
namespace Approval.Forms;

/// <summary>表单附件：本地存储目录、大小与扩展名限制、未绑定文件的保留时间。</summary>
public sealed class ApprovalFileOptions
{
    /// <summary>本地存储目录（<see cref="Attachments.LocalApprovalFileStore"/>）；相对路径相对于内容根目录。不要放在 wwwroot 下。</summary>
    public string RootPath { get; set; } = "App_Data/approval-files";

    public int MaxFileSizeMb { get; set; } = 20;

    /// <summary>允许上传的扩展名（不含点，不区分大小写）。</summary>
    public List<string> AllowedExtensions { get; set; } =
        ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "png", "jpg", "jpeg", "gif", "zip"];


    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}

