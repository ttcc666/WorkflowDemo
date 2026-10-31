using System.Text.Json;
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
