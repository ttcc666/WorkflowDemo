using Approval.AspNetCore.Endpoints;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;

namespace Approval.AspNetCore;

public static class ApprovalAspNetCoreExtensions
{
    /// <summary>
    /// 注册审批接口所需服务：当前用户（默认读取 Claims）、ProblemDetails 与异常映射、字符串枚举。
    /// 需同时调用 <c>AddApprovalWorkflow&lt;TContext&gt;()</c>；宿主自定义当前用户时注册 <see cref="IApprovalCurrentUser"/> 即可覆盖默认实现。
    /// </summary>
    public static IServiceCollection AddApprovalAspNetCore(this IServiceCollection services, Action<ApprovalAspNetCoreOptions>? configure = null)
    {
        var options = new ApprovalAspNetCoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ApprovalEndpointValidationService>());
        services.TryAddScoped<IApprovalContextResolver, ClaimsApprovalContextResolver>();
        if (services.Any(d => d.ServiceType == typeof(IApprovalPermissionProvider) && d.ImplementationType == typeof(DefaultApprovalPermissionProvider)))
            services.Replace(ServiceDescriptor.Scoped<IApprovalPermissionProvider, ApprovalPolicyPermissionProvider>());
        else services.TryAddScoped<IApprovalPermissionProvider, ApprovalPolicyPermissionProvider>();

        services.TryAddSingleton<IApprovalCurrentUser, ClaimsApprovalCurrentUser>();
        services.AddProblemDetails();
        services.AddExceptionHandler<ApprovalExceptionHandler>();

        if (options.UseStringEnums)
        {
            services.ConfigureHttpJsonOptions(o =>
            {
                if (!o.SerializerOptions.Converters.Any(c => c is JsonStringEnumConverter))
                    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        }

        return services;
    }

    /// <summary>
    /// 映射全部通用审批接口（默认前缀 <c>/api/approval</c>）。所有接口要求已识别当前用户（否则 401），
    /// 审批异常在本组内直接转换为 ProblemDetails，无需依赖宿主的异常处理中间件。
    /// 返回路由组，宿主可继续追加 <c>RequireAuthorization()</c>、<c>RequireCors()</c> 等约定。
    /// </summary>
    public static RouteGroupBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app, string prefix = "/api/approval")
    {
        var options = app.ServiceProvider.GetService<ApprovalAspNetCoreOptions>()
            ?? throw new InvalidOperationException("请先调用 services.AddApprovalAspNetCore()");

        var group = app.MapGroup(prefix).WithTags("Approval");
        ((IEndpointConventionBuilder)group).Add(builder =>
        {
            if (builder is not RouteEndpointBuilder route) return;
            var path = route.RoutePattern.RawText ?? throw new InvalidOperationException("缺少审批路由");
            var relative = path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
            var methods = builder.Metadata.OfType<HttpMethodMetadata>().Single().HttpMethods;
            if (methods.Count != 1) throw new InvalidOperationException("审批端点必须声明单一 HTTP 方法");
            builder.Metadata.Add(ApprovalEndpointPermissions.For(methods[0], relative));
        });
        foreach (var status in new[] { 400, 401, 403, 404, 409, 410, 503 })
            group.WithMetadata(new ProducesResponseTypeMetadata(status, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), ["application/problem+json"]));
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                context.HttpContext.GetApprovalUserId();
                var services = context.HttpContext.RequestServices;
                var metadata = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ApprovalPermissionMetadata>() ?? throw new InvalidOperationException("审批端点缺少权限元数据");
                var authorization = services.GetRequiredService<ApprovalAuthorizationService>();
                var keyedCommand = ApprovalHttpCommands.Managed(context.HttpContext, metadata) && context.HttpContext.Request.Headers.ContainsKey(ApprovalHttpCommands.Header);
                var allowed = false;
                foreach (var permission in keyedCommand ? [] : metadata.Permissions)
                    if (await authorization.CanAsync(permission, ApprovalEndpointPermissions.Resource(context.HttpContext, metadata), context.HttpContext.RequestAborted)) { allowed = true; break; }
                if (!allowed && !keyedCommand)
                    throw new ApprovalException("没有执行此审批操作的权限") { Code = ApprovalErrorCode.Forbidden };
                return await next(context);
            }
            catch (ApprovalException ex)
            {
                return ApprovalProblems.ToResult(ex);
            }
        });
        ApprovalHttpCommands.Add(group);

        var modules = app.ServiceProvider.GetServices<IApprovalEndpointModule>().ToArray();
        var formBusinessType = modules.FirstOrDefault()?.BusinessType;
        InstanceEndpoints.Map(group);
        TaskEndpoints.Map(group);
        DefinitionEndpoints.Map(group);
        MessageEndpoints.Map(group, options);
        DirectoryEndpoints.Map(group, formBusinessType);
        AdminEndpoints.Map(group);
        group.MapGet("/diagnostics", (Approval.Core.Diagnostics.ApprovalDiagnosticsService service, CancellationToken ct) => service.CheckAsync(ct));
        foreach (var module in modules) module.Map(group);
        return group;
    }
}
