using System.Text;
using System.Text.Json;
using Acornima.Ast;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Engine.Extensions;
using Jint;
using Jint.Native;

namespace Approval.Core.Actions;

internal sealed class JavaScriptActionHandler : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    private const int MaxInputBytes = 512 * 1024;
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(500);
    private static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>("""
        {"type":"object","required":["script"],"additionalProperties":false,
         "properties":{"script":{"type":"string","minLength":1,"maxLength":16000}}}
        """);

    public ApprovalActionDescriptor Descriptor { get; } = new("JavaScript", "JavaScript", "受限的 JSON 计算",
        [ActionExecutionMode.Sync], ConfigSchema: Schema, AllowsContinue: true);

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config)
    {
        if (version != 1 || config.EnumerateObject().Count() != 1 || !config.TryGetProperty("script", out var source) ||
            source.ValueKind != JsonValueKind.String || source.GetString()!.Length is 0 or > 16000)
            return ["JavaScript v1 要求 1–16000 字符的 script"];
        try
        {
            var prepared = Jint.Engine.PrepareScript(source.GetString()!, strict: true,
                options: new ScriptPreparationOptions { StaticAnalysis = false });
            return prepared.Program is { } program && program.Body.OfType<FunctionDeclaration>().Count(function =>
                function.Id?.Name == "execute" && !function.Async && !function.Generator) == 1
                ? [] : ["JavaScript 需要一个同步 function execute(context)"];
        }
        catch (Exception) { return ["JavaScript 语法无效"]; }
    }

    public Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var input = ActionVariables.Snapshot(context).GetRawText();
        if (Encoding.UTF8.GetByteCount(input) > MaxInputBytes) return Task.FromResult(new ApprovalActionResult(false));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Deadline);
        var engine = new Jint.Engine(options =>
        {
            options.Strict();
            options.Interop.Enabled = false;
            options.Host.StringCompilationAllowed = false;
            options.Constraints.MaxRecursionDepth = 32;
            options.Constraints.StackOverflowGuard = true;
            options.Constraints.MaxArraySize = 8192;
            options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(50);
            options.Constraints.PromiseTimeout = TimeSpan.FromMilliseconds(100);
            options.MaxStatements(25_000).LimitMemory(8_000_000).TimeoutInterval(Deadline).CancellationToken(timeout.Token);
        });
        engine.SetValue("__workflowInput", new JsString(input));
        engine.Execute("""
            const __workflowContext = JSON.parse(__workflowInput);
            const __workflowStringify = (() => {
                const stringify = JSON.stringify;
                const Map = WeakMap;
                const get = Function.call.bind(Map.prototype.get);
                const set = Function.call.bind(Map.prototype.set);
                const BudgetError = RangeError;
                return result => {
                    const depths = new Map();
                    let remaining = 16384;
                    return stringify(result, function (key, value) {
                        const depth = (get(depths, this) || 0) + 1;
                        remaining -= key.length + (typeof value === 'string' ? value.length : 1);
                        if (depth > 16 || remaining < 0) throw new BudgetError('Output budget');
                        if (value !== null && typeof value === 'object') set(depths, value, depth);
                        return value;
                    });
                };
            })();
            """);
        engine.Execute(config.GetProperty("script").GetString()!);
        var output = engine.Evaluate("""
            (() => {
                const result = execute(__workflowContext);
                return result !== null && typeof result === 'object' && !Array.isArray(result)
                    && !(result instanceof Promise) ? __workflowStringify(result) : null;
            })()
            """);
        ct.ThrowIfCancellationRequested();
        if (!output.IsString()) return Task.FromResult(new ApprovalActionResult(false));
        var json = output.AsString();
        if (Encoding.UTF8.GetByteCount(json) > ApprovalActionValidation.MaxOutputBytes)
            return Task.FromResult(new ApprovalActionResult(false));
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        return Task.FromResult(document.RootElement.ValueKind == JsonValueKind.Object
            ? new ApprovalActionResult(true, document.RootElement.Clone()) : new(false));
    }

    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) =>
        ExecuteAsync(context, config, ct);
}
