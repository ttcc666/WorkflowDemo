using System.Diagnostics;
using Approval.Core.Definitions;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine.Runtime;

internal static class ApprovalActionDiagnostics
{
    internal static void Failure(ILogger logger, Guid? instanceId, string? nodeId, string? actionId,
        Guid? executionId, Guid? outboxId, string? handlerType, ActionExecutionMode mode, string code, Exception? exception)
    {
        if (!logger.IsEnabled(LogLevel.Warning)) return;
        var traces = new List<string>();
        if (exception is not null) Capture(exception, traces);
        logger.LogWarning("审批动作失败。InstanceId={InstanceId} NodeId={NodeId} ActionId={ActionId} ExecutionId={ExecutionId} OutboxId={OutboxId} HandlerType={HandlerType} Mode={Mode} ErrorCode={ErrorCode} ExceptionType={ExceptionType} ExceptionStack={ExceptionStack}",
            instanceId, nodeId, actionId, executionId, outboxId, handlerType, mode, code, exception?.GetType().FullName,
            traces.Count == 0 ? null : string.Join(Environment.NewLine, traces));
    }

    private static void Capture(Exception exception, List<string> traces)
    {
        if (traces.Count >= 8) return;
        var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames().Take(24).Select(frame => frame.GetMethod());
        traces.Add(exception.GetType().FullName + Environment.NewLine +
            string.Join(Environment.NewLine, frames.Select(method => $"  at {method?.DeclaringType?.FullName}.{method?.Name}")));
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (traces.Count >= 8) break;
                Capture(inner, traces);
            }
        }
        else if (exception.InnerException is { } inner) Capture(inner, traces);
    }
}
