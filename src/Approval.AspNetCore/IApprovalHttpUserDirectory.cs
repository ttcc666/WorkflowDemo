using Approval.Core.Abstractions;

namespace Approval.AspNetCore;

/// <summary>
/// 可选的 HTTP 人员目录，供宿主限制当前用户可搜索或回显的人员。
/// 引擎的审批资格检查仍使用 IApprovalUserDirectory，避免个人数据范围影响跨部门分派。
/// </summary>
public interface IApprovalHttpUserDirectory : IApprovalUserDirectory;
