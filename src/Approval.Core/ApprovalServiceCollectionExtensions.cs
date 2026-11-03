using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Background;
using Approval.Core.Conditions;
using Approval.Core.Engine;
using Approval.Core.Engine.Extensions;
using Approval.Core.Files;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Resolvers;
using Approval.Core.Tenancy;
using Approval.Core.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Approval.Core;

public static class ApprovalServiceCollectionExtensions
{
    /// <summary>
    /// 注册审批框架。TContext 继承 ApprovalDbContext，需在 OnModelCreating 中调用 <c>modelBuilder.ConfigureApproval(this)</c>。
    /// </summary>
    public static ApprovalBuilder AddApprovalWorkflow<TContext>(this IServiceCollection services, Action<ApprovalOptions>? configure = null)
        where TContext : ApprovalDbContext
    {
        var options = new ApprovalOptions();
        configure?.Invoke(options);
        if (options.MultiTenant && options.FixedTenantId is not null) throw new InvalidOperationException("多租户模式不能设置 FixedTenantId");
        if (!options.MultiTenant) ApprovalTenantIds.Validate(options.FixedTenantId ?? throw new InvalidOperationException("请显式配置 FixedTenantId，或启用 MultiTenant"));
        services.AddSingleton(options);
        if (options.Lifecycle.BatchSize is < 1 or > 100 || options.Lifecycle.MaxBatchesPerRun is < 1 or > 10
            || options.Background.MaxTenantsPerSweep is < 1 or > 100 || options.Background.AssignmentSampleSize is < 1 or > 100
            || options.Background.TenantBudget <= TimeSpan.Zero || options.Background.TenantBudget > TimeSpan.FromMinutes(1)
            || options.Lifecycle.LeaseDuration <= TimeSpan.Zero || options.Lifecycle.PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("维护批量、扫描预算或租约配置无效");
        foreach (var target in Enum.GetValues<Approval.Core.Domain.MaintenanceTarget>())
        foreach (var mode in Enum.GetValues<Approval.Core.Domain.MaintenanceMode>())
            if (options.Lifecycle.Retention(target, mode) is { } retention && (retention <= TimeSpan.Zero || retention > TimeSpan.FromDays(36500)))
                throw new InvalidOperationException("保留期须大于零且不超过 100 年");
        if (options.PermissionTimeout <= TimeSpan.Zero || options.PermissionTimeout > TimeSpan.FromMinutes(1)) throw new InvalidOperationException("权限超时须大于零且不超过一分钟");
        services.TryAddScoped<IApprovalPermissionProvider, DefaultApprovalPermissionProvider>();
        services.TryAddScoped<IApprovalQueryScope, DefaultApprovalQueryScope>();
        services.TryAddScoped<IApprovalArchiveQueryScope, DefaultApprovalArchiveQueryScope>();
        services.AddScoped<IApprovalCommandExecutor, ApprovalCommandExecutor<TContext>>();
        services.AddScoped<IApprovalUnitOfWork>(sp => sp.GetRequiredService<ApprovalDbContextAccessor>().UnitOfWork);
        services.AddScoped<IApprovalAuthorization>(sp => new DefaultApprovalAuthorization(sp.GetRequiredService<TContext>(), sp.GetRequiredService<IApprovalTenantContext>(), sp.GetRequiredService<IApprovalActorContext>(), sp.GetRequiredService<IApprovalPermissionProvider>(), sp.GetRequiredService<IApprovalQueryScope>(), options, sp.GetRequiredService<IApprovalArchiveQueryScope>()));
        services.AddScoped(sp => new ApprovalAuthorizationService(sp.GetRequiredService<IApprovalAuthorization>()));
        services.TryAddScoped<ApprovalExecutionContext>();
        services.TryAddScoped<IApprovalTenantContext>(sp => sp.GetRequiredService<ApprovalExecutionContext>());
        services.TryAddScoped<IApprovalActorContext>(sp => sp.GetRequiredService<ApprovalExecutionContext>());
        services.TryAddScoped<IApprovalTenantCatalog, FixedApprovalTenantCatalog>();
        services.TryAddSingleton<IApprovalTenantScopeFactory, ApprovalTenantScopeFactory>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConditionEvaluator, DynamicExpressoConditionEvaluator>();
        services.AddScoped(sp =>
        {
            if (options.MultiTenant)
            {
                var adapters = new[] { typeof(IApprovalIdentityProvider), typeof(IApprovalUserDirectory), typeof(IApprovalOrganizationProvider), typeof(IApprovalContactProvider) };
                if (services.Any(d => adapters.Contains(d.ServiceType) && d.Lifetime == ServiceLifetime.Singleton))
                    throw new InvalidOperationException("多租户身份/目录/组织/联系方式适配器必须按租户作用域注册");
            }
            var db = sp.GetRequiredService<TContext>();
            db.TransactionState.RequireOperationId = options.RequireOperationId;
            db.TransactionState.SystemActor = sp.GetRequiredService<IApprovalActorContext>().IsSystem;
            if (db.TenantId != sp.GetRequiredService<IApprovalTenantContext>().TenantId)
                throw new InvalidOperationException("DbContext 租户与当前审批作用域不一致");
            return new ApprovalDbContextAccessor(db, sp.GetService<IApprovalTenantSettingsProvider>(), sp.GetRequiredService<ApprovalAuthorizationService>(), sp.GetRequiredService<IApprovalCommandExecutor>(), sp.GetRequiredService<IApprovalActorContext>());
        });

        services.AddSingleton<IApproverResolver, UserApproverResolver>();
        services.AddSingleton<IApproverResolver, RoleApproverResolver>();
        services.AddSingleton<IApproverResolver, FormFieldApproverResolver>();
        services.AddSingleton<IApproverResolver, InitiatorApproverResolver>();

        // 内置动作：HTTP 请求、脚本
        services.AddHttpClient(HttpActionHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler((handler, _) =>
            {
                // 白名单只允许显式请求的地址；禁止传输层自动跳转，保留宿主的代理等配置。
                switch (handler)
                {
                    case SocketsHttpHandler sockets:
                        sockets.AllowAutoRedirect = false;
                        break;
                    case HttpClientHandler http:
                        http.AllowAutoRedirect = false;
                        break;
                }
            });
        services.AddHttpClient(ChannelJson.HttpClientName);
        services.AddScoped<IApprovalActionHandler, HttpActionHandler>();
        services.AddScoped<IApprovalActionHandler, ScriptActionHandler>();

        services.AddScoped(sp => new ApprovalExtensionCatalog(
            () => sp.GetServices<IApproverResolver>(),
            () => sp.GetServices<IApprovalBusinessHandler>(),
            () => sp.GetServices<IApprovalActionHandler>(),
            () => sp.GetServices<IApprovalChannel>()));
        services.AddScoped(sp => new ApprovalEngine(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<IApprovalIdentityProvider>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetServices<IApprovalNotifier>(),
            sp.GetRequiredService<IConditionEvaluator>(),
            sp.GetRequiredService<ApprovalOptions>(),
            sp.GetRequiredService<ApprovalOutboxSignal>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ApprovalEngine>>(),
            sp.GetService<IApprovalUserDirectory>()));
        services.AddScoped<ApprovalQueryService>();
        services.TryAddScoped<IApprovalAccessPolicy, DefaultApprovalAccessPolicy>();
        services.AddScoped(sp => new ApprovalDefinitionService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ApprovalOptions>()));
        services.AddScoped<ApprovalMessageService>();
        services.AddScoped(sp => new ApprovalOutboxService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetRequiredService<ApprovalOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ApprovalOutboxService>>(),
            sp.GetRequiredService<IApprovalTenantScopeFactory>()));
        services.AddScoped<ApprovalStatisticsService>();
        services.AddScoped<Approval.Core.Diagnostics.ApprovalDiagnosticsService>();
        services.AddScoped<Approval.Core.Maintenance.ApprovalLifecycleService>();
        services.AddSingleton<Approval.Core.Diagnostics.ApprovalBackgroundState>();
        services.AddSingleton<ApprovalTenantSweeper>();
        services.AddSingleton<ApprovalMaintenanceWorker>();

        // 后台任务：注册为单例以便宿主 / 测试直接调用 DispatchOnceAsync、ScanOnceAsync
        services.AddSingleton<ApprovalOutboxSignal>();
        services.AddSingleton<ApprovalOutboxDispatcher>();
        services.AddSingleton<ApprovalTimeoutScanner>();
        if (options.EnableBackgroundServices)
        {
            services.AddHostedService(sp => sp.GetRequiredService<ApprovalOutboxDispatcher>());
            services.AddHostedService(sp => sp.GetRequiredService<ApprovalTimeoutScanner>());
            services.AddHostedService(sp => sp.GetRequiredService<ApprovalMaintenanceWorker>());
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

    /// <summary>替换审批的查看规则（默认 <see cref="DefaultApprovalAccessPolicy"/>，可继承后追加规则）。</summary>
    public ApprovalBuilder AddAccessPolicy<T>() where T : class, IApprovalAccessPolicy
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalAccessPolicy, T>());
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
        // 附件：表单的附件字段使用；默认本地磁盘存储，可用 AddFileStore<T>() 替换
        Services.TryAddScoped<IApprovalFileStore, LocalApprovalFileStore>();
        Services.AddScoped<ApprovalFileService>();
        Services.AddScoped<IApprovalBusinessHandler>(_ => new FormApprovalHandler(options));
        return this;
    }

    /// <summary>替换附件存储（默认 <see cref="LocalApprovalFileStore"/>），如对象存储。</summary>
    public ApprovalBuilder AddFileStore<T>() where T : class, IApprovalFileStore
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalFileStore, T>());
        return this;
    }

    /// <summary>替换权限服务；每次调用重新判定，默认不缓存授权结果。</summary>
    public ApprovalBuilder AddPermissionProvider<T>() where T : class, IApprovalPermissionProvider
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalPermissionProvider, T>());
        return this;
    }

    /// <summary>收窄实例查询范围，在数据库分页、统计和 count 前应用。</summary>
    public ApprovalBuilder AddQueryScope<T>() where T : class, IApprovalQueryScope
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalQueryScope, T>());
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

    /// <summary>
    /// 注册组织架构（部门、负责人、汇报关系），并启用审批人类型 DeptLeader（部门负责人）、Manager（第 N 级上级）、
    /// ManagerChain（逐级审批）。宿主已自行注册 T（如与身份提供者共用的单例）时沿用其生命周期。
    /// </summary>
    public ApprovalBuilder AddOrganizationProvider<T>() where T : class, IApprovalOrganizationProvider
    {
        Services.TryAddScoped<T>();
        Services.AddScoped<IApprovalOrganizationProvider>(sp => sp.GetRequiredService<T>());
        Services.AddScoped<IApproverResolver, DeptLeaderApproverResolver>();
        Services.AddScoped<IApproverResolver, ManagerApproverResolver>();
        Services.AddScoped<IApproverResolver, ManagerChainApproverResolver>();
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
