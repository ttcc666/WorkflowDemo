using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Approval.Core.Diagnostics;

public static class ApprovalInstrumentation
{
    public const string Name = "Approval.Core";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Metrics = new(Name);
    internal static readonly Counter<long> Commands = Metrics.CreateCounter<long>("approval.commands");
    internal static readonly Counter<long> Retries = Metrics.CreateCounter<long>("approval.retries");
    internal static readonly Counter<long> Conflicts = Metrics.CreateCounter<long>("approval.conflicts");
    internal static readonly Histogram<double> CommandDuration = Metrics.CreateHistogram<double>("approval.command.duration", "ms");
    internal static readonly Histogram<double> BackgroundDuration = Metrics.CreateHistogram<double>("approval.background.duration", "ms");
    internal static readonly Histogram<double> TransactionDuration = Metrics.CreateHistogram<double>("approval.transaction.duration", "ms");
    internal static IDisposable Command(string tenant, string kind, string? key)
    {
        Commands.Add(1);
        var activity = Activities.StartActivity("approval.command");
        activity?.SetTag("approval.tenant.id", tenant); activity?.SetTag("approval.command.kind", kind); activity?.SetTag("approval.operation.id", key);
        return new Observation(CommandDuration, activity);
    }
    internal static IDisposable Transaction() => new Observation(TransactionDuration, Activities.StartActivity("approval.transaction"));
    private sealed class Observation(Histogram<double> histogram, Activity? activity) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        public void Dispose() { histogram.Record(Stopwatch.GetElapsedTime(_started).TotalMilliseconds); activity?.Dispose(); }
    }
}
