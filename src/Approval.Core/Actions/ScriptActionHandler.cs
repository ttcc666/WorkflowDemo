using System.Text.Json.Nodes;
using Jint;
using Jint.Native.Json;
using Jint.Runtime;
using JsEngine = Jint.Engine;

namespace Approval.Core.Actions;

/// <summary>
/// 内置动作：执行 JavaScript 脚本（Jint 沙箱，ES2023 语法）。
/// 脚本中可用 <c>form</c>（表单数据，修改会写回表单）、<c>instance</c>（实例信息）、<c>log(...)</c>；
/// <c>return</c> 的值作为输出写入 OutputVariable。
/// 沙箱不开放 .NET 互操作，并限制执行时间、内存、语句数和递归深度，不能访问文件、网络。
/// </summary>
public sealed class ScriptActionHandler(ApprovalOptions options) : IApprovalActionHandler
{
    public const string ActionType = "Script";
    private const int MaxLogs = 50;

    // 脚本放在函数体中执行以支持 return；函数头与脚本第一行同行，报错行号与用户脚本一致
    private const string Prelude = """
        "use strict";
        var __logs = [];
        function log() {
          if (__logs.length >= 50) return;
          __logs.push(Array.prototype.map.call(arguments, function (a) { return typeof a === 'string' ? a : JSON.stringify(a); }).join(' '));
        }
        """;

    public ApprovalActionDescriptor Descriptor { get; } = new(ActionType, "脚本",
    [
        new ActionParameter("script", "脚本", ActionParameterKind.Code)
        {
            Required = true,
            Template = null, // 代码不做模板替换，表单数据通过 form 变量访问
            Help = "可读写 form（表单数据），只读 instance；return 的值写入输出变量。例：form.riskLevel = form.amount > 10000 ? 'high' : 'low'"
        }
    ])
    {
        Description = "在沙箱中执行 JavaScript，常用于计算字段、数据校验，结果可用于后续条件分支",
        SupportsAsync = false
    };

    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, CancellationToken ct)
    {
        var script = context.Get("script") ?? throw new InvalidOperationException("未配置脚本");
        var instanceInfo = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = context.Instance.Id,
            title = context.Instance.Title,
            initiatorId = context.Instance.InitiatorId,
            businessType = context.Instance.BusinessType,
            businessKey = context.Instance.BusinessKey,
            flow = context.Instance.DefinitionKey,
            nodeId = context.NodeId
        });

        using var engine = CreateEngine(ct);
        try
        {
            var parser = new JsonParser(engine);
            engine.SetValue("form", parser.Parse(context.Instance.FormDataJson));
            engine.SetValue("instance", parser.Parse(instanceInfo));
            engine.Execute(Prelude);
            engine.Execute("var __result = (function (form, instance, log) { " + script + "\n})(form, instance, log);");
            var json = engine.Evaluate("JSON.stringify({ result: __result === undefined ? null : __result, form: form, logs: __logs })")
                .AsString();

            var output = JsonNode.Parse(json)!.AsObject();
            var logs = output["logs"]!.AsArray().Select(l => l!.GetValue<string>()).Take(MaxLogs).ToList();
            return Task.FromResult(new ApprovalActionResult
            {
                Output = output["result"]?.DeepClone(),
                FormUpdates = output["form"] as JsonObject is { } form ? (JsonObject)form.DeepClone() : null,
                Message = logs.Count == 0 ? null : "日志：" + string.Join("；", logs)
            });
        }
        catch (Exception ex) when (Describe(ex) is { } message)
        {
            throw new InvalidOperationException(message, ex);
        }
    }

    public IEnumerable<string> ValidateParameters(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("script", out var script) || string.IsNullOrWhiteSpace(script)) yield break;

        string? error = null;
        try
        {
            JsEngine.PrepareScript("(function (form, instance, log) { " + script + "\n})", strict: true);
        }
        catch (Exception ex)
        {
            error = $"脚本语法错误：{ex.Message}";
        }
        if (error is not null) yield return error;
    }

    private JsEngine CreateEngine(CancellationToken ct) => new(o => o
        .Strict()
        .TimeoutInterval(options.Script.Timeout)
        .LimitMemory(options.Script.MemoryLimitBytes)
        .MaxStatements(options.Script.MaxStatements)
        .LimitRecursion(options.Script.MaxRecursionDepth)
        .CancellationToken(ct));

    private static string? Describe(Exception ex) => ex switch
    {
        TimeoutException => "脚本执行超时",
        MemoryLimitExceededException => "脚本内存占用超限",
        StatementsCountOverflowException => "脚本执行语句数超限（是否存在死循环？）",
        RecursionDepthOverflowException => "脚本递归层级过深",
        JavaScriptException js => $"脚本错误：{js.Message}（第 {js.Location.Start.Line} 行）",
        ExecutionCanceledException => null,
        _ when ex.GetType().Namespace?.StartsWith("Jint", StringComparison.Ordinal) == true
               || ex.GetType().Namespace?.StartsWith("Acornima", StringComparison.Ordinal) == true => $"脚本错误：{ex.Message}",
        _ => null
    };
}
