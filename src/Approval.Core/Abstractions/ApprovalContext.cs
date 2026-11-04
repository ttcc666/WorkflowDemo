namespace Approval.Core.Abstractions;

public interface IApprovalActorContext
{ string ActorId { get; } bool IsSystem { get; } }

public sealed record ApprovalExecutionIdentity(string? ActorId, bool IsSystem = false);

public interface IApprovalContextResolver
{ ApprovalExecutionIdentity Resolve(); }

public interface IApprovalScopeFactory
{
    Task<Approval.Core.Hosting.ApprovalScope> CreateAsync(CancellationToken ct = default);
}

public sealed record ApprovalDisplaySettings(TimeZoneInfo DisplayTimeZone, string? InstanceUrlFormat,
    IReadOnlyDictionary<string, string> GlobalVariables);
