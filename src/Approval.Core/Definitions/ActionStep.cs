using System.Text.Json;
using System.Text.Json.Serialization;

namespace Approval.Core.Definitions;

/// <summary>动作的执行时机。</summary>
public enum ActionExecutionMode
{
    /// <summary>在当前审批事务中执行。</summary>
    Sync,

    /// <summary>在当前审批事务提交后投递。</summary>
    Async
}

/// <summary>步骤失败时采用的策略。</summary>
public enum ActionFailurePolicy
{
    /// <summary>失败使当前审批事务回滚。</summary>
    Fail,

    /// <summary>记录失败并继续执行，不保存输出。</summary>
    Continue,

    /// <summary>由 Outbox 处理投递重试。</summary>
    Retry
}

/// <summary>Action 节点中的一个有序步骤。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActionStep
{
    /// <summary>节点内稳定且唯一的步骤 ID。</summary>
    public required string Id { get; init; }

    /// <summary>设计器中显示的名称。</summary>
    public string? Name { get; init; }

    /// <summary>宿主注册表中的 Handler 类型标识。</summary>
    public required string Type { get; init; }

    /// <summary>同步执行或提交后投递。</summary>
    public ActionExecutionMode Mode { get; init; }

    /// <summary>同步成功后可供下游读取的输出键。</summary>
    public string? OutputKey { get; init; }

    /// <summary>由 Handler 解释的配置版本。</summary>
    public int ConfigVersion { get; init; } = 1;

    /// <summary>由 Handler 校验的配置对象。</summary>
    public required JsonElement Config { get; init; }

    /// <summary>失败时采用的策略。</summary>
    public ActionFailurePolicy FailurePolicy { get; init; }
}
