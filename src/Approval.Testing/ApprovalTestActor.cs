using Approval.Core.Abstractions;

namespace Approval.Testing;

/// <summary>Controls the current test actor across asynchronous calls and fresh command scopes.</summary>
public sealed class ApprovalTestActor : IApprovalContextResolver
{
    private readonly AsyncLocal<ActorScope?> _current = new();

    public IDisposable Use(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var scope = new ActorScope(_current, actorId, _current.Value);
        _current.Value = scope;
        return scope;
    }

    public ApprovalExecutionIdentity Resolve() => new(_current.Value?.ActorId);

    private sealed class ActorScope(AsyncLocal<ActorScope?> current, string actorId, ActorScope? previous) : IDisposable
    {
        public string ActorId { get; } = actorId;

        public void Dispose()
        {
            if (ReferenceEquals(current.Value, this)) current.Value = previous;
            else if (!ReferenceEquals(current.Value, previous)) throw new InvalidOperationException("Dispose test actors in reverse order.");
        }
    }
}
