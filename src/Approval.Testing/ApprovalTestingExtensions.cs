using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Approval.Testing;

public sealed class ApprovalTestingOptions
{
    public bool UseTestActor { get; set; } = true;
    public bool UseTestUsers { get; set; } = true;
    public bool UseTestPermissions { get; set; } = true;
    public bool UseTestEvents { get; set; } = true;
    public DateTimeOffset? InitialUtcTime { get; set; }
}

public static class ApprovalTestingExtensions
{
    /// <summary>Installs opted-in test doubles after the test host's regular registrations.</summary>
    public static ApprovalBuilder AddApprovalTesting(this ApprovalBuilder builder, Action<ApprovalTestingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ApprovalTestingOptions();
        configure?.Invoke(options);
        var services = builder.Services;
        if (options.UseTestActor) Replace<IApprovalContextResolver, ApprovalTestActor>(services);
        if (options.UseTestUsers)
        {
            Replace<IApprovalIdentityProvider, ApprovalTestUsers>(services);
            Replace<IApprovalUserStatusProvider, ApprovalTestUsers>(services);
            Replace<IApprovalUserDirectory, ApprovalTestUsers>(services);
        }
        if (options.UseTestPermissions) Replace<IApprovalPermissionProvider, ApprovalTestPermissions>(services);
        if (options.UseTestEvents) Replace<IApprovalEventHandler, ApprovalTestEvents>(services);
        if (options.InitialUtcTime is { } time)
        {
            services.TryAddSingleton(_ => new FakeTimeProvider(time.ToUniversalTime()));
            Replace<TimeProvider, FakeTimeProvider>(services);
        }
        return builder;
    }

    private static void Replace<TService, TTest>(IServiceCollection services)
        where TService : class where TTest : class, TService
    {
        services.TryAddSingleton<TTest>();
        services.RemoveAll<TService>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TTest>());
    }
}
