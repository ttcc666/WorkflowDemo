using Approval.Core.Domain;
using Approval.Core.Engine;
using System.Text.Json;

namespace Approval.AspNetCore.Endpoints;

public sealed record CommentRequest(string? Comment);

/// <summary>同意；<see cref="FormData"/> 仅表单审批可传，只能包含任务所在节点的可编辑字段。</summary>
public sealed record ApproveRequest(string? Comment, JsonElement? FormData);

public sealed record TransferRequest(string ToUserId, string? Comment);

/// <summary>指定 <see cref="TargetNodeId"/> 时退回到该节点（流程继续），否则驳回并结束流程。</summary>
public sealed record RejectRequest(string? Comment, string? TargetNodeId);

public sealed record AddSignRequest(string[] UserIds, AddSignMode Mode, string? Comment);

/// <summary>重新提交；<see cref="FormData"/> 仅表单审批可修改。</summary>
public sealed record ResubmitRequest(string? Comment, JsonElement? FormData);

/// <summary>发起表单审批；<see cref="DraftId"/> 不为空时提交成功后删除该草稿。</summary>
public sealed record StartFormRequest(JsonElement FormData, string? Comment, Guid? DraftId = null);

/// <summary>保存草稿；<see cref="DraftId"/> 为空时新建。</summary>
public sealed record SaveDraftRequest(Guid? DraftId, JsonElement FormData, string? Comment);

/// <summary>终止流程等需要填写原因的管理操作。</summary>
public sealed record ReasonRequest(string? Reason);

/// <summary>改派（单个待办或某人的全部待办）。</summary>
public sealed record ReassignRequest(string ToUserId, string? Reason);

/// <summary>待办列表的查询字符串：page（从 1 开始）、pageSize（默认 20，最大 100）、keyword、definitionKey、overdueOnly。</summary>
public sealed record TodoListRequest(int? Page, int? PageSize, string? Keyword, string? DefinitionKey, bool? OverdueOnly,
    string? BusinessType = null, string? BusinessKey = null, string? InitiatorId = null, string? ApproverId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, InstanceStatus? Status = null, ApprovalSort? Sort = null)
{
    public PageRequest ToPage() => new(Page, PageSize);

    public TodoQuery ToQuery() => new(Keyword, DefinitionKey, OverdueOnly ?? false, BusinessType, BusinessKey, InitiatorId,
        ApproverId, From?.UtcDateTime, To?.UtcDateTime, Status, Sort ?? ApprovalSort.Oldest);
}

/// <summary>流程列表的查询字符串：分页 + keyword、status、definitionKey、from / to（发起时间，ISO 8601）、initiatorId。</summary>
public sealed record InstanceListRequest(
    int? Page,
    int? PageSize,
    string? Keyword,
    InstanceStatus? Status,
    string? DefinitionKey,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? InitiatorId, string? BusinessType = null, string? BusinessKey = null, string? ApproverId = null,
    ApprovalSort? Sort = null)
{
    public PageRequest ToPage() => new(Page, PageSize);

    public InstanceQuery ToQuery() => new(Keyword, Status, DefinitionKey, From?.UtcDateTime, To?.UtcDateTime, InitiatorId,
        BusinessType, BusinessKey, ApproverId, Sort ?? ApprovalSort.Newest);
}
