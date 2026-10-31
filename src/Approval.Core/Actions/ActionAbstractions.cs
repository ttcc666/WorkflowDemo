using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Core.Domain;
using Approval.Core.Templates;

namespace Approval.Core.Actions;

public enum ActionParameterKind { Text, TextArea, Code, Number, Select, Boolean }

/// <summary>动作参数描述，设计器据此自动生成参数表单。</summary>
public sealed record ActionParameter(string Name, string Label, ActionParameterKind Kind = ActionParameterKind.Text)
{
    public bool Required { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public string? Help { get; init; }

    /// <summary>
    /// 参数值中的占位符（如 <c>{form.amount}</c>）如何替换；为空表示不做模板替换。
    /// 代码类参数（脚本）必须为空，避免把表单数据拼进代码造成注入。
    /// </summary>
    public TemplateEncoding? Template { get; init; } = TemplateEncoding.Raw;
}

/// <summary>动作描述：类型标识、显示名、参数，以及支持的执行方式。</summary>
public sealed record ApprovalActionDescriptor(string Type, string DisplayName, IReadOnlyList<ActionParameter> Parameters)
{
    public string? Description { get; init; }
    public bool SupportsSync { get; init; } = true;
    public bool SupportsAsync { get; init; } = true;
}

public sealed class ApprovalActionContext
{
    public required ApprovalInstance Instance { get; init; }
    public required string NodeId { get; init; }
    public required string StepName { get; init; }

    /// <summary>已完成模板替换的参数（不区分大小写）。</summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    public required IReadOnlyDictionary<string, object?> FormData { get; init; }

    /// <summary>是否由后台任务异步执行。</summary>
    public bool IsAsync { get; init; }

    /// <summary>幂等键：异步执行时为 Outbox 消息 ID（重试时不变），同步执行时为本次执行生成的新 ID。</summary>
    public Guid IdempotencyKey { get; init; } = Guid.NewGuid();

    public string? Get(string name) =>
        Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed class ApprovalActionResult
{
    public static ApprovalActionResult Empty { get; } = new();

    /// <summary>动作输出，写入步骤配置的 OutputVariable。</summary>
    public JsonNode? Output { get; init; }

    /// <summary>要写回表单的字段（同步动作），供后续条件分支与模板使用。</summary>
    public JsonObject? FormUpdates { get; init; }

    /// <summary>附加到审批轨迹的说明，如脚本日志、HTTP 状态码。</summary>
    public string? Message { get; init; }

    public static ApprovalActionResult FromOutput(object? output, string? message = null) => new()
    {
        Output = output as JsonNode ?? JsonSerializer.SerializeToNode(output, JsonSerializerOptions.Web),
        Message = message
    };
}

/// <summary>
/// 可在流程中使用的动作。宿主通过 <c>AddActionHandler&lt;T&gt;()</c> 注册（Scoped，可注入宿主的 DbContext 等服务），
/// 设计器根据 <see cref="Descriptor"/> 自动列出动作并生成参数表单。
/// 异步执行可能重试，实现应保证幂等（可使用 <see cref="ApprovalActionContext.IdempotencyKey"/>）。
/// </summary>
public interface IApprovalActionHandler
{
    ApprovalActionDescriptor Descriptor { get; }

    Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct);

    /// <summary>发布前校验参数（如脚本语法），返回错误信息。</summary>
    IEnumerable<string> ValidateParameters(IReadOnlyDictionary<string, string> parameters) => [];
}
