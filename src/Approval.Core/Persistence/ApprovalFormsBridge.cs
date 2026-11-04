using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Domain;
using Approval.Core.Engine;

namespace Approval.Core.Persistence;

/// <summary>Forms 的固定协作入口；只授权 Forms 元数据与指定实例标题，不开放其它写所有者。</summary>
internal sealed class ApprovalFormsBridge(ApprovalDbContextAccessor accessor)
{
    private readonly ApprovalUnitOfWork _unit = accessor.UnitOfWork;
    private readonly IApprovalAuthorization _authorization = accessor.Authorization;

    internal Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        _unit.ExecuteCommandAsync(ApprovalWriteOwner.Forms, operation, ct);

    internal Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct) =>
        _unit.ExecuteCommandAsync(ApprovalWriteOwner.Forms, operation, ct);

    internal Guid NewId(string kind) => _unit.NewId(kind);

    internal Guid? PinnedDefinition(string key) => _unit.PinnedDefinition(key);

    internal void PinDefinition(string key, Guid id) => _unit.PinDefinition(key, id);

    internal IDisposable WriteMetadata() => _unit.Writes.Enter(ApprovalWriteOwner.Forms);

    internal IDisposable EditTitle(ApprovalInstance instance) => _unit.Writes.Enter(ApprovalWriteOwner.None, instance, "Title");

    internal IQueryable<ApprovalDefinition> StartDefinitions() => ApprovalDefinitionAvailability.ForNewStarts(accessor.Context);

    internal Task<ApprovalViewer> ViewerAsync(ApprovalViewer viewer, Guid? instanceId, CancellationToken ct) => _authorization.ViewerAsync(viewer, instanceId, ct);

    internal static ApprovalAuthorizationService Authorization(ApprovalDbContextAccessor accessor) => new(accessor.Authorization);

    internal static IApprovalIdentityProvider Identity(ApprovalDbContextAccessor accessor, IApprovalIdentityProvider provider) => ApprovalHostServices.Identity(accessor.UnitOfWork, provider);

    internal static IApprovalUserDirectory? Directory(ApprovalDbContextAccessor accessor, IApprovalUserDirectory? provider) => ApprovalHostServices.Directory(accessor.UnitOfWork, provider);

    internal static TimeProvider Clock(ApprovalDbContextAccessor accessor, TimeProvider provider) => ApprovalHostServices.Clock(accessor.UnitOfWork, provider);

    internal static void RegisterForms(Type draft, Type file) => ApprovalWriteAuthorization.RegisterForms(draft, file);
}
