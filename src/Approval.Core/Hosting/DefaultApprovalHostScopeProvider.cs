using Approval.Core.Abstractions;

namespace Approval.Core.Hosting;

internal sealed class DefaultApprovalHostScopeProvider : IApprovalHostScopeProvider
{
    private static readonly HostScope Scope = new();

    public IApprovalHostScope Capture() => Scope;

    public Task<IReadOnlyList<IApprovalHostScope>> GetBackgroundScopesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<IApprovalHostScope>>([Scope]);
    }

    private sealed class HostScope : IApprovalHostScope, IDisposable
    {
        public string? PartitionKey => null;
        public IDisposable Enter() => this;
        public void Dispose() { }
    }
}
