using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Conditions;
using Approval.Core.Engine;
using Approval.Core.Engine.Extensions;
using Approval.Core.Files;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Resolvers;
using Approval.Core.Hosting;
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
        services.AddSingleton(options);
        if (options.Outbox.PollInterval <= TimeSpan.Zero || options.TimeoutScanInterval <= TimeSpan.Zero
            || options.Outbox.BaseRetryDelay <= TimeSpan.Zero || options.Outbox.MaxRetryDelay < options.Outbox.BaseRetryDelay
            || options.Outbox.BatchSize < 1 || options.Outbox.MaxAttempts < 1)
            throw new InvalidOperationException("后台轮询间隔、重试间隔、批量和最大投递次数必须有效");
        if (options.Lifecycle.BatchSize is < 1 or > 100 || options.Lifecycle.MaxBatchesPerRun is < 1 or > 10
            || options.Background.AssignmentSampleSize is < 1 or > 100
            || options.Lifecycle.PollInterval <= TimeSpan.Zero)
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
        services.AddScoped<IApprovalAuthorization>(sp => new DefaultApprovalAuthorization(sp.GetRequiredService<TContext>(), sp.GetRequiredService<IApprovalActorContext>(), sp.GetRequiredService<IApprovalPermissionProvider>(), sp.GetRequiredService<IApprovalQueryScope>(), options, sp.GetRequiredService<IApprovalArchiveQueryScope>()));
        services.AddScoped(sp => new ApprovalAuthorizationService(sp.GetRequiredService<IApprovalAuthorization>()));
        services.TryAddScoped<ApprovalExecutionContext>();
        services.TryAddScoped<IApprovalActorContext>(sp => sp.GetRequiredService<ApprovalExecutionContext>());
        services.TryAddSingleton<IApprovalScopeFactory, ApprovalScopeFactory>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConditionEvaluator, DynamicExpressoConditionEvaluator>();
        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            db.TransactionState.RequireOperationId = options.RequireOperationId;
            db.TransactionState.SystemActor = sp.GetRequiredService<IApprovalActorContext>().IsSystem;
            return new ApprovalDbContextAccessor(db, sp.GetRequiredService<ApprovalAuthorizationService>(), sp.GetRequiredService<IApprovalCommandExecutor>(), sp.GetRequiredService<IApprovalActorContext>());
        });

        services.AddSingleton<IApproverResolver, UserApproverResolver>();
        services.AddSingleton<IApproverResolver, RoleApproverResolver>();
        services.AddSingleton<IApproverResolver, FormFieldApproverResolver>();
        services.AddSingleton<IApproverResolver, InitiatorApproverResolver>();

        services.AddScoped(sp => new ApprovalExtensionCatalog(
            () => sp.GetServices<IApproverResolver>(),
            () => sp.GetServices<IApprovalBusinessHandler>(),
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
            sp.GetService<IApprovalUserDirectory>())
        {
            EventsEnabled = sp.GetService<IApprovalEventHandler>() is not null,
            Lifecycle = sp.GetService<IApprovalLifecycleHandler>(),
            UserStatus = sp.GetService<IApprovalUserStatusProvider>()
        });
        services.AddScoped<ApprovalQueryService>();
        services.TryAddScoped<IApprovalAccessPolicy, DefaultApprovalAccessPolicy>();
        services.AddScoped(sp => new ApprovalDefinitionService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ApprovalOptions>()));
        services.AddScoped<ApprovalMessageService>();
        services.AddScoped<ApprovalCarbonCopyService>();
        services.AddScoped(sp => new ApprovalOutboxService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetRequiredService<ApprovalOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ApprovalOutboxService>>(),
            sp.GetRequiredService<IApprovalScopeFactory>(),
            sp.GetRequiredService<ApprovalOutboxSignal>())
        {
            EventHandler = sp.GetService<IApprovalEventHandler>()
        });
        services.AddScoped<ApprovalStatisticsService>();
        services.AddScoped<Approval.Core.Diagnostics.ApprovalDiagnosticsService>();
        services.AddScoped<Approval.Core.Maintenance.ApprovalLifecycleService>();
        services.AddSingleton<Approval.Core.Maintenance.ApprovalMaintenanceGate>();
        services.AddSingleton<Approval.Core.Diagnostics.ApprovalBackgroundState>();
        services.AddSingleton<ApprovalBackgroundRunner>();
        services.AddSingleton<ApprovalMaintenanceWorker>();

        // 后台任务：注册为单例以便宿主 / 测试直接调用 DispatchOnceAsync、ScanOnceAsync
        services.AddSingleton<ApprovalOutboxSignal>();
        services.AddSingleton<ApprovalOutboxDispatcher>();
        services.AddSingleton<ApprovalTimeoutScanner>();
        if (options.EnableBackgroundServices)
        {
            services.AddHostedService<ApprovalWorker>();
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

    /// <summary>注册宿主本地开始回调；只负责审批事务中的校验/准备，不由流程图编排。</summary>
    public ApprovalBuilder AddLifecycleHandler<T>() where T : class, IApprovalLifecycleHandler
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalLifecycleHandler, T>());
        return this;
    }

    /// <summary>注册自定义通知渠道。</summary>
    public ApprovalBuilder AddChannel<T>() where T : class, IApprovalChannel
    {
        Services.AddScoped<IApprovalChannel, T>();
        return this;
    }

    /// <summary>注册统一的宿主审批事件处理器；事件与审批同事务写入 Outbox。</summary>
    public ApprovalBuilder AddEventHandler<T>() where T : class, IApprovalEventHandler
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalEventHandler, T>());
        return this;
    }

    /// <summary>进程内提交后通知：失败只记录日志。需要可靠投递时使用宿主事件或通知渠道。</summary>
    public ApprovalBuilder AddNotifier<T>() where T : class, IApprovalNotifier
    {
        Services.AddScoped<IApprovalNotifier, T>();
        return this;
    }

}
