using System.Text.Json.Serialization;
using Approval.AspNetCore.Endpoints;
using Approval.Core;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                context.HttpContext.GetApprovalUserId();
                return await next(context);
            }
            catch (ApprovalException ex)
            {
                return ApprovalProblems.ToResult(ex);
            }
        });

        // 表单审批为可选能力：未调用 AddFormApprovals() 时不映射相关接口（避免把未注册的服务参数推断为请求体）
        var formsEnabled = app.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(ApprovalFormService)) == true;
        var formBusinessType = formsEnabled ? app.ServiceProvider.GetService<ApprovalFormOptions>()?.BusinessType : null;

        InstanceEndpoints.Map(group);
        TaskEndpoints.Map(group, formsEnabled);
        DefinitionEndpoints.Map(group);
        MessageEndpoints.Map(group, options);
        DirectoryEndpoints.Map(group, formBusinessType);
        if (formsEnabled) FormEndpoints.Map(group);
        return group;
    }
}
