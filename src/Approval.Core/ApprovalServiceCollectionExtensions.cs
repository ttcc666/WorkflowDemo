using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Background;
using Approval.Core.Conditions;
using Approval.Core.Engine;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Resolvers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Approval.Core;

public static class ApprovalServiceCollectionExtensions
{
    /// <summary>
    /// 注册审批框架。TContext 为宿主 DbContext，需在 OnModelCreating 中调用 <c>modelBuilder.ConfigureApproval()</c>。
    /// </summary>
    public static ApprovalBuilder AddApprovalWorkflow<TContext>(this IServiceCollection services, Action<ApprovalOptions>? configure = null)
        where TContext : DbContext
    {
        var options = new ApprovalOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConditionEvaluator, DynamicExpressoConditionEvaluator>();
        services.AddScoped(sp => new ApprovalDbContextAccessor(sp.GetRequiredService<TContext>()));

        services.AddSingleton<IApproverResolver, UserApproverResolver>();
        services.AddSingleton<IApproverResolver, RoleApproverResolver>();
        services.AddSingleton<IApproverResolver, FormFieldApproverResolver>();
        services.AddSingleton<IApproverResolver, InitiatorApproverResolver>();

        // 内置动作：HTTP 请求、脚本
        services.AddHttpClient(HttpActionHandler.HttpClientName);
        services.AddHttpClient(ChannelJson.HttpClientName);
        services.AddScoped<IApprovalActionHandler, HttpActionHandler>();
        services.AddScoped<IApprovalActionHandler, ScriptActionHandler>();

        services.AddScoped<ApprovalEngine>();
        services.AddScoped<ApprovalQueryService>();
        services.AddScoped<ApprovalDefinitionService>();
        services.AddScoped<ApprovalMessageService>();
        services.AddScoped<ApprovalOutboxService>();

        // 后台任务：注册为单例以便宿主 / 测试直接调用 DispatchOnceAsync、ScanOnceAsync
        services.AddSingleton<ApprovalOutboxSignal>();
        services.AddSingleton<ApprovalOutboxDispatcher>();
        services.AddSingleton<ApprovalTimeoutScanner>();
        if (options.EnableBackgroundServices)
        {
            services.AddHostedService(sp => sp.GetRequiredService<ApprovalOutboxDispatcher>());
            services.AddHostedService(sp => sp.GetRequiredService<ApprovalTimeoutScanner>());
        }

        return new ApprovalBuilder(services);
    }
}

public sealed class ApprovalBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>注册宿主的用户/角色提供者（必需）。</summary>
    public ApprovalBuilder AddIdentityProvider<T>() where T : class, IApprovalIdentityProvider
    {
        Services.AddScoped<IApprovalIdentityProvider, T>();
        return this;
    }

    /// <summary>注册用户目录（用户选择器、角色列表），可选。</summary>
    public ApprovalBuilder AddUserDirectory<T>() where T : class, IApprovalUserDirectory
    {
        Services.AddScoped<IApprovalUserDirectory, T>();
        return this;
    }

    /// <summary>
    /// 启用表单审批：设置了 <c>settings.startable</c> 的流程可在发起中心直接填写表单发起，
    /// 业务类型为 <paramref name="businessType"/>，表单数据即单据。
    /// </summary>
    public ApprovalBuilder AddFormApprovals(string businessType = "Form")
    {
        var options = new ApprovalFormOptions { BusinessType = businessType };
        Services.AddSingleton(options);
        Services.AddScoped<ApprovalFormService>();
        Services.AddScoped<IApprovalBusinessHandler>(_ => new FormApprovalHandler(options));
        return this;
    }

    /// <summary>注册业务回调，每个 BusinessType 一个。</summary>
    public ApprovalBuilder AddBusinessHandler<T>() where T : class, IApprovalBusinessHandler
    {
        Services.AddScoped<IApprovalBusinessHandler, T>();
        return this;
    }

    /// <summary>注册自定义审批人解析器；Type 与内置解析器同名时覆盖内置实现。</summary>
    public ApprovalBuilder AddApproverResolver<T>() where T : class, IApproverResolver
    {
        Services.AddScoped<IApproverResolver, T>();
        return this;
    }

    /// <summary>注册自定义动作（设计器中自动出现）；Type 与内置动作同名时覆盖内置实现。</summary>
    public ApprovalBuilder AddActionHandler<T>() where T : class, IApprovalActionHandler
    {
        Services.AddScoped<IApprovalActionHandler, T>();
        return this;
    }

    /// <summary>注册自定义通知渠道。</summary>
    public ApprovalBuilder AddChannel<T>() where T : class, IApprovalChannel
    {
        Services.AddScoped<IApprovalChannel, T>();
        return this;
    }

    /// <summary>注册用户联系方式提供者（邮件地址、企业微信 / 钉钉 UserId）。</summary>
    public ApprovalBuilder AddContactProvider<T>() where T : class, IApprovalContactProvider
    {
        Services.AddScoped<IApprovalContactProvider, T>();
        return this;
    }

    public ApprovalBuilder AddWebhookChannel(Action<WebhookChannelOptions> configure)
    {
        var options = Configure(configure);
        Services.AddScoped<IApprovalChannel>(sp => new WebhookChannel(options, sp.GetRequiredService<IHttpClientFactory>()));
        return this;
    }

    public ApprovalBuilder AddWeComRobotChannel(Action<WeComRobotChannelOptions> configure)
    {
        var options = Configure(configure);
        Services.AddScoped<IApprovalChannel>(sp =>
            new WeComRobotChannel(options, sp.GetRequiredService<IHttpClientFactory>(), sp.GetService<IApprovalContactProvider>()));
        return this;
    }

    public ApprovalBuilder AddDingTalkRobotChannel(Action<DingTalkRobotChannelOptions> configure)
    {
        var options = Configure(configure);
        Services.AddScoped<IApprovalChannel>(sp => new DingTalkRobotChannel(options, sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetService<IApprovalContactProvider>()));
        return this;
    }

    public ApprovalBuilder AddEmailChannel(Action<EmailChannelOptions> configure)
    {
        var options = Configure(configure);
        Services.AddScoped<IApprovalChannel>(sp =>
            new EmailChannel(options, sp.GetRequiredService<ILogger<EmailChannel>>(), sp.GetService<IApprovalContactProvider>()));
        return this;
    }

    /// <summary>旧版通知接口：提交后同步调用、失败只记录日志。新代码建议使用通知渠道（可靠投递）。</summary>
    public ApprovalBuilder AddNotifier<T>() where T : class, IApprovalNotifier
    {
        Services.AddScoped<IApprovalNotifier, T>();
        return this;
    }

    private static T Configure<T>(Action<T> configure) where T : new()
    {
        var options = new T();
        configure(options);
        return options;
    }
}
