using System.Text.Json;
using Approval.Core.Domain;
using Approval.Core.Engine;

namespace Approval.AspNetCore.Endpoints;

public sealed record CommentRequest(string? Comment);

public sealed record TransferRequest(string ToUserId, string? Comment);

/// <summary>指定 <see cref="TargetNodeId"/> 时退回到该节点（流程继续），否则驳回并结束流程。</summary>
public sealed record RejectRequest(string? Comment, string? TargetNodeId);

public sealed record AddSignRequest(string[] UserIds, AddSignMode Mode, string? Comment);

/// <summary>重新提交；<see cref="FormData"/> 仅表单审批可修改。</summary>
public sealed record ResubmitRequest(string? Comment, JsonElement? FormData);

public sealed record StartFormRequest(JsonElement FormData, string? Comment);

/// <summary>待办列表的查询字符串：page（从 1 开始）、pageSize（默认 20，最大 100）、keyword、definitionKey、overdueOnly。</summary>
public sealed record TodoListRequest(int? Page, int? PageSize, string? Keyword, string? DefinitionKey, bool? OverdueOnly)
{
    public PageRequest ToPage() => new(Page, PageSize);

    public TodoQuery ToQuery() => new(Keyword, DefinitionKey, OverdueOnly ?? false);
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
    string? InitiatorId)
{
    public PageRequest ToPage() => new(Page, PageSize);

    public InstanceQuery ToQuery() => new(Keyword, Status, DefinitionKey, From?.UtcDateTime, To?.UtcDateTime, InitiatorId);
}
