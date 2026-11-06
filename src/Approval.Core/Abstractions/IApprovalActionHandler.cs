using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Abstractions;

/// <summary>注册在当前审批作用域的动作。同步动作可暂存宿主业务数据，由审批命令统一保存。</summary>
public interface IApprovalActionHandler
{
    /// <summary>类型、版本和支持的执行模式。</summary>
    ApprovalActionDescriptor Descriptor { get; }

    /// <summary>检查定义配置，返回可向设计人员公开的错误信息。此方法应为只读。</summary>
    IReadOnlyList<string> ValidateConfig(int version, JsonElement config);

    /// <summary>检查配置与执行模式是否兼容。</summary>
    IReadOnlyList<string> ValidateConfig(int version, ActionExecutionMode mode, JsonElement config) =>
        ValidateConfig(version, config);

    /// <summary>运行一个动作。同步实现不得自行保存、提交、重入审批或调用产生外部副作用的服务。</summary>
    Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct);
}

/// <summary>设计器可使用的动作类型描述。</summary>
/// <param name="Type">注册表中的稳定类型 ID。</param>
/// <param name="DisplayName">设计器显示名称。</param>
/// <param name="Description">动作的用途说明。</param>
/// <param name="SupportedModes">该动作支持的执行模式。</param>
/// <param name="ConfigVersion">创建新步骤时使用的配置版本。</param>
/// <param name="ConfigSchema">可选的配置 JSON Schema。</param>
/// <param name="AllowsContinue">处理器承诺没有数据库写入或外部副作用，可以在失败时继续。</param>
public sealed record ApprovalActionDescriptor(string Type, string DisplayName, string Description,
    IReadOnlyList<ActionExecutionMode> SupportedModes, int ConfigVersion = 1, JsonElement? ConfigSchema = null,
    bool AllowsContinue = false);

/// <summary>动作的只读执行快照。表单与输出只包含 JSON 值。</summary>
/// <param name="InstanceId">流程实例 ID。</param>
/// <param name="DefinitionKey">绑定的流程定义标识。</param>
/// <param name="DefinitionVersion">实例绑定的发布版本。</param>
/// <param name="BusinessType">宿主业务类型。</param>
/// <param name="BusinessKey">宿主业务标识。</param>
/// <param name="InitiatorId">发起人。</param>
/// <param name="OperatorId">触发本次推进的可信操作人。</param>
/// <param name="NodeId">当前节点 ID。</param>
/// <param name="NodeName">当前节点名称。</param>
/// <param name="ActionId">当前步骤 ID。</param>
/// <param name="Round">实例的退回轮次。</param>
/// <param name="Title">实例标题。</param>
/// <param name="Status">动作开始时的实例状态。</param>
/// <param name="ExecutionId">步骤执行 ID。用于关联诊断与幂等。</param>
/// <param name="ConfigVersion">步骤绑定的配置版本。</param>
/// <param name="Form">规范化的只读表单快照。</param>
/// <param name="Outputs">当前路径可读取的同步输出。</param>
/// <param name="Mode">同步执行或提交后投递。</param>
public sealed record ApprovalActionContext(Guid InstanceId, string DefinitionKey, int DefinitionVersion, string BusinessType,
    string BusinessKey, string InitiatorId, string OperatorId, string NodeId, string NodeName, string ActionId, int Round,
    string? Title, InstanceStatus Status, Guid ExecutionId, int ConfigVersion, JsonElement Form, JsonElement Outputs,
    ActionExecutionMode Mode = ActionExecutionMode.Sync)
{
    internal Engine.Runtime.ApprovalActionRuntime? Runtime { get; init; }

    /// <summary>按受限 JSON 路径读取当前动作可见的变量。</summary>
    public bool TryResolveVariable(string path, out JsonElement value)
    {
        value = default;
        return Actions.ActionVariables.IsSource(path) &&
            Actions.ActionVariables.TryResolve(Actions.ActionVariables.Snapshot(this), path, out value);
    }

    /// <summary>替换配置 JSON 值中的变量引用，单值引用保留原始 JSON 类型。</summary>
    public JsonElement BindParameters(JsonElement config) =>
        Actions.ActionVariables.Bind(config, Actions.ActionVariables.Snapshot(this));
}

/// <summary>宿主显式提供的只读预演。不得访问外部系统、写入数据库或调用正式业务动作。</summary>
public interface IApprovalActionPreviewHandler
{
    /// <summary>预演值可否作为模拟流程的输出。请求计划仅展示时返回 false。</summary>
    bool ProvidesOutput => true;

    /// <summary>以合成上下文计算预演输出。</summary>
    Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct);
}

/// <summary>步骤结果。输出只允许包含适合持久化的业务数据。</summary>
/// <param name="Succeeded">步骤是否执行成功。</param>
/// <param name="Output">可选的 JSON 输出。</param>
public sealed record ApprovalActionResult(bool Succeeded, JsonElement? Output = null);
