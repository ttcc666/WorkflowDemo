using Approval.Forms;
using Approval.AspNetCore;
using Approval.Core.Abstractions;
using Approval.Forms.Runtime;
using Approval.Forms.AspNetCore.Endpoints;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Approval.Forms.AspNetCore;
public static class ApprovalFormsAspNetCoreExtensions
{
    public static IServiceCollection AddApprovalFormsAspNetCore(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApprovalEndpointModule, FormEndpointModule>());
        services.TryAddScoped<IApprovalTaskDataAdapter, FormTaskDataAdapter>();
        return services;
    }
}
internal sealed class FormEndpointModule(ApprovalFormOptions options) : IApprovalEndpointModule
{
    public string BusinessType => options.BusinessType;
    public void Map(RouteGroupBuilder group) => FormEndpoints.Map(group, options);
}
internal sealed class FormTaskDataAdapter(ApprovalFormService forms) : IApprovalTaskDataAdapter
{
    public Task<IReadOnlyList<string>> GetEditableFieldsAsync(ApprovalTaskContext task, CancellationToken ct) => forms.GetEditableFieldsAsync(task, ct);
    public Task ResubmitAsync(Guid id, string user, JsonElement? data, string? comment, CancellationToken ct) => forms.ResubmitAsync(id, user, data, comment, ct);
    public Task ApproveAsync(Guid id, string user, JsonElement data, string? comment, CancellationToken ct) => forms.ApproveAsync(id, user, data, comment, ct);
}
