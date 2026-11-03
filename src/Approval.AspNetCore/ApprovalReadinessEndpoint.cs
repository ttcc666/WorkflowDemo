using Approval.Core.Abstractions;
using Approval.Core.Diagnostics;
using Approval.Core.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.AspNetCore;

public sealed record ApprovalReadiness(bool Ready);
public static class ApprovalReadinessEndpoint
{
    /// <summary>可选匿名探针，只返回整体 Ready；不读取当前 HTTP 主体，不公开企业或数据库细节。</summary>
    public static IEndpointConventionBuilder MapApprovalReadiness(this IEndpointRouteBuilder app, string path = "/health/approval") =>
        app.MapGet(path, async (IServiceScopeFactory scopes, IApprovalTenantScopeFactory factory, CancellationToken ct) =>
        {
            var ready = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                await using var scope = scopes.CreateAsyncScope();
                var tenant = (await scope.ServiceProvider.GetRequiredService<IApprovalTenantCatalog>().GetTenantsAsync(token.Token)).FirstOrDefault(t => t.Enabled);
                if (tenant is not null)
                {
                    await using var tenantScope = await factory.CreateAsync(tenant.TenantId, token.Token);
                    ready = await ApprovalDiagnosticsService.ReadinessAsync(tenantScope.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context, token.Token);
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            return Results.Json(new ApprovalReadiness(ready), statusCode: ready ? 200 : 503);
        }).AllowAnonymous().WithTags("ApprovalHealth").Produces<ApprovalReadiness>().Produces<ApprovalReadiness>(503);
}
