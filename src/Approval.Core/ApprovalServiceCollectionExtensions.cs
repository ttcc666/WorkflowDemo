using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Approval.Core.Background;
using Approval.Core.Conditions;
using Approval.Core.Engine;
using Approval.Core.Engine.Extensions;
using Approval.Core.Hosting;
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
    /// 注册审批框架。使用宿主已有 DbContext；在 EF options 调用 UseApproval()，在模型中调用 ConfigureApproval()。
    /// </summary>
    public static ApprovalBuilder AddApprovalWorkflow<TContext>(this IServiceCollection services, Action<ApprovalOptions>? configure = null)
        where TContext : DbContext
    {
        var options = new ApprovalOptions();
        configure?.Invoke(options);
        options.Statistics.Validate();
        services.AddSingleton(options);
        services.ConfigureDbContext<TContext>(o => o.UseApproval());
        if (options.Outbox.PollInterval <= TimeSpan.Zero || options.TimeoutScanInterval <= TimeSpan.Zero
            || options.Outbox.BaseRetryDelay <= TimeSpan.Zero || options.Outbox.MaxRetryDelay < options.Outbox.BaseRetryDelay
            || options.Outbox.BatchSize < 1 || options.Outbox.MaxAttempts < 1)
            throw new InvalidOperationException("后台轮询间隔、重试间隔、批量和最大投递次数必须有效");
        if (options.Background.AssignmentSampleSize is < 1 or > 100)
            throw new InvalidOperationException("诊断扫描预算必须在 1–100 之间");
        if (options.PermissionTimeout <= TimeSpan.Zero || options.PermissionTimeout > TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("权限超时须大于零且不超过一分钟");
        RegisterPersistence<TContext>(services);
        RegisterAuthorization<TContext>(services, options);
        RegisterRuntime(services, options);
        RegisterQuery(services);
        RegisterBackground(services, options);
        return new ApprovalBuilder(services);
    }

    private static void RegisterPersistence<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IApprovalHostContext, ApprovalHostContext<TContext>>();
        services.AddScoped<IApprovalCommandExecutor, ApprovalCommandExecutor<TContext>>();
        services.AddScoped<IApprovalUnitOfWork>(sp => sp.GetRequiredService<ApprovalDbContextAccessor>().UnitOfWork);
        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            ApprovalPersistence.EnsureInstalled(db);
            ApprovalPersistence.State(db).SystemActor = sp.GetRequiredService<IApprovalActorContext>().IsSystem;
            return new ApprovalDbContextAccessor(db, sp.GetRequiredService<ApprovalAuthorizationService>(), sp.GetRequiredService<IApprovalCommandExecutor>(), sp.GetRequiredService<IApprovalActorContext>());
        });
    }

    private static void RegisterAuthorization<TContext>(IServiceCollection services, ApprovalOptions options)
        where TContext : DbContext
    {
        services.TryAddScoped<IApprovalPermissionProvider, DefaultApprovalPermissionProvider>();
        services.TryAddScoped<IApprovalQueryScope, DefaultApprovalQueryScope>();
        services.AddScoped<IApprovalAuthorization>(sp => new DefaultApprovalAuthorization(
            sp.GetRequiredService<TContext>(), sp.GetRequiredService<IApprovalActorContext>(),
            sp.GetRequiredService<IApprovalPermissionProvider>(), sp.GetRequiredService<IApprovalQueryScope>(),
            options, sp.GetServices<IApprovalResourceResolver>()));
        services.AddScoped(sp => new ApprovalAuthorizationService(sp.GetRequiredService<IApprovalAuthorization>()));
        services.TryAddScoped<ApprovalExecutionContext>();
        services.TryAddScoped<IApprovalActorContext>(sp => sp.GetRequiredService<ApprovalExecutionContext>());
        services.TryAddSingleton<IApprovalScopeFactory, ApprovalScopeFactory>();
        services.TryAddScoped<IApprovalAccessPolicy, DefaultApprovalAccessPolicy>();
    }

    private static void RegisterRuntime(IServiceCollection services, ApprovalOptions options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConditionEvaluator, DynamicExpressoConditionEvaluator>();
        services.AddSingleton<IApproverResolver, UserApproverResolver>();
        services.AddSingleton<IApproverResolver, RoleApproverResolver>();
        services.AddSingleton<IApproverResolver, FormFieldApproverResolver>();
        services.AddSingleton<IApproverResolver, InitiatorApproverResolver>();
        services.AddScoped(sp => new ApprovalExtensionCatalog(
            () => sp.GetServices<IApproverResolver>(), () => sp.GetServices<IApprovalBusinessHandler>(), () => options.NotificationTargets,
            () => sp.GetServices<IApprovalActionHandler>()));
        services.AddScoped(sp => new ApprovalEngine(
            sp.GetRequiredService<ApprovalDbContextAccessor>(), sp.GetRequiredService<IApprovalIdentityProvider>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(), sp.GetRequiredService<IConditionEvaluator>(),
            sp.GetRequiredService<ApprovalOptions>(), sp.GetRequiredService<ApprovalOutboxSignal>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<ApprovalEngine>>(),
            sp.GetService<IApprovalUserDirectory>(), () => sp.GetRequiredService<IApprovalAccessPolicy>(), sp.GetService<IApprovalDefinitionExtension>(),
            sp.GetService<IApprovalLifecycleHandler>(), sp.GetService<IApprovalUserStatusProvider>()));
        services.AddScoped(sp => new ApprovalSimulationService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(), sp.GetRequiredService<ApprovalDefinitionService>(),
            sp.GetRequiredService<ApprovalExtensionCatalog>(), sp.GetRequiredService<IApprovalIdentityProvider>(), options,
            sp.GetRequiredService<IConditionEvaluator>(), sp.GetService<IApprovalUserDirectory>(), sp.GetService<IApprovalUserStatusProvider>()));
        services.AddScoped(sp => new ApprovalDefinitionService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(), sp.GetRequiredService<ApprovalExtensionCatalog>(),
            sp.GetRequiredService<TimeProvider>(), options, sp.GetService<IApprovalDefinitionExtension>()));
    }

    private static void RegisterQuery(IServiceCollection services)
    {
        services.AddScoped<ApprovalQueryService>();
        services.AddScoped<ApprovalMessageService>();
        services.AddScoped<ApprovalCarbonCopyService>();
        services.AddScoped<ApprovalStatisticsService>();
        services.AddScoped<Approval.Core.Diagnostics.ApprovalDiagnosticsService>();
    }

    private static void RegisterBackground(IServiceCollection services, ApprovalOptions options)
    {
        services.AddScoped(sp => new ApprovalOutboxService(
            sp.GetRequiredService<ApprovalDbContextAccessor>(),
            sp.GetRequiredService<ApprovalOptions>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<ApprovalOutboxService>>(),
            sp.GetRequiredService<IApprovalScopeFactory>(), sp.GetRequiredService<ApprovalOutboxSignal>(),
            options.Outbox.EnableDispatcher ? sp.GetRequiredService<IApprovalEventHandler>() : null,
            sp.GetRequiredService<ApprovalExtensionCatalog>(), () => sp.GetRequiredService<IApprovalAccessPolicy>()));
        services.AddSingleton<Approval.Core.Diagnostics.ApprovalBackgroundState>();
        services.AddSingleton<ApprovalBackgroundRunner>();
        // 后台任务：注册为单例以便宿主 / 测试直接调用 DispatchOnceAsync、ScanOnceAsync
        services.AddSingleton<ApprovalOutboxSignal>();
        services.AddSingleton<ApprovalHostValidation>();
        services.AddHostedService(sp => sp.GetRequiredService<ApprovalHostValidation>());
        services.AddSingleton(sp => new ApprovalOutboxDispatcher(sp.GetRequiredService<ApprovalBackgroundRunner>()) { ValidateHost = sp.GetRequiredService<ApprovalHostValidation>().ValidateAsync, Options = options });
        services.AddSingleton<ApprovalTimeoutScanner>();
        if (options.EnableBackgroundServices)
        {
            services.AddHostedService<ApprovalWorker>();
        }
    }
}

public sealed class ApprovalBuilder(IServiceCollection services)
{
    /// <summary>启用复用站内信和现有 Outbox 通知渠道的显式通知动作。</summary>
    public ApprovalBuilder AddNotificationActions() => AddActionHandler<Actions.NotificationActionHandler>();

    /// <summary>注册固定的参数化 SQL 查询。工厂必须返回新的独立连接，并使用仅有读取权限的数据库身份。</summary>
    public ApprovalBuilder AddSqlActionQuery(ApprovalSqlActionQuery query, Func<IServiceProvider, System.Data.Common.DbConnection> connectionFactory)
    {
        if (!Engine.Extensions.ApprovalActionValidation.IsId(query.DataSource) ||
            !Engine.Extensions.ApprovalActionValidation.IsId(query.QueryId) || string.IsNullOrWhiteSpace(query.CommandText) ||
            query.MaxRows is < 1 or > 500 || query.TimeoutSeconds is < 1 or > 15 || query.Parameters.Count > 32)
            throw new ArgumentException("SQL 查询注册无效", nameof(query));
        Services.AddSingleton(new Actions.SqlActionRegistration(query with
        {
            Parameters = query.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
        }, connectionFactory));
        return AddActionHandler<Actions.SqlActionHandler>();
    }

    /// <summary>启用 HTTP 动作，允许流程配置引用宿主登记的精确 HTTPS 目标。</summary>
    public ApprovalBuilder AddHttpActions(params ApprovalHttpActionEndpoint[] endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (!Engine.Extensions.ApprovalActionValidation.IsId(endpoint.Name) || !Actions.PublicHttpTransport.IsAllowedAddress(endpoint.Address))
                throw new ArgumentException("HTTP 动作目标必须为带有效标识的公开 HTTPS URL", nameof(endpoints));
            Services.AddSingleton(endpoint with { Methods = endpoint.Methods.ToHashSet(StringComparer.Ordinal) });
        }
        Services.AddHttpClient(Actions.HttpActionHandler.ClientName)
            .ConfigurePrimaryHttpMessageHandler(Actions.PublicHttpTransport.CreateHandler).RemoveAllLoggers();
        return AddActionHandler<Actions.HttpActionHandler>();
    }

    /// <summary>注册可在正式运行和预演中执行的受限 JavaScript 与 Mapping 动作。</summary>
    public ApprovalBuilder AddCalculationActions()
    {
        AddActionHandler<Actions.JavaScriptActionHandler>();
        return AddActionHandler<Actions.MappingActionHandler>();
    }

    /// <summary>注册 Scoped 动作处理器。同一实现重复注册时复用，多个实现声明同一类型标识时拒绝。</summary>
    public ApprovalBuilder AddActionHandler<T>() where T : class, IApprovalActionHandler
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IApprovalActionHandler, T>());
        return this;
    }

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

    /// <summary>注册统一的宿主审批事件处理器；事件与审批同事务写入 Outbox。</summary>
    public ApprovalBuilder AddEventHandler<T>() where T : class, IApprovalEventHandler
    {
        Services.Replace(ServiceDescriptor.Scoped<IApprovalEventHandler, T>());
        return this;
    }
}
