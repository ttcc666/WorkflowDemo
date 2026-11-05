using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>可选定义快照的校验，由拥有快照的模块提供。</summary>
public interface IApprovalDefinitionExtension
{
    IReadOnlySet<string>? UserFields(FlowDefinition definition);

    void Validate(FlowDefinition definition, List<string> errors);

    IReadOnlyList<string> Warnings(FlowDefinition definition);

    /// <summary>只读预演的输入规范化与校验；不得绑定附件、保存数据或执行业务回调。</summary>
    string NormalizeSimulationData(FlowDefinition definition, string dataJson);
}

/// <summary>可选模块或宿主按参与节点投影业务 JSON 与审计数据。</summary>
public interface IApprovalDataProjection
{
    ApprovalDataView Project(FlowDefinition definition, ApprovalDataProjectionContext context, ApprovalAccess access);
}

public sealed record ApprovalDataView(string DataJson, IReadOnlyList<ApprovalLogContext> Logs, IReadOnlyList<string> HiddenFields);

/// <summary>可选模块解析其拥有的资源，后续仍执行审批实例查询范围和能力检查。</summary>
public interface IApprovalResourceResolver
{
    Task<ApprovalResource> ResolveAsync(ApprovalResource resource, CancellationToken ct);
}

/// <summary>仅用于由 EF 自动递增版本的扩展实体。</summary>

public interface IApprovalTrackedRevision : IApprovalEntity
{ long ConcurrencyStamp { get; set; } }
