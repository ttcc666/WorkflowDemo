using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Hosting;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void CompositionRoot_ValidatesScopesAndSharesOnlyTheHostContextWithinEachScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddSingleton<IApprovalIdentityProvider>(new FakeIdentityProvider());
        services.AddSingleton<IApprovalUserStatusProvider, FixtureUserStatus>();
        services.AddApprovalWorkflow<TestDbContext>(o => { o.EnableBackgroundServices = false; o.Outbox.EnableDispatcher = false; });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ApprovalEngine>());
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        first.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(new("alice"), []);
        second.ServiceProvider.GetRequiredService<ApprovalExecutionContext>().InitializeTrusted(new("bob"), []);
        var firstEngine = first.ServiceProvider.GetRequiredService<ApprovalEngine>();
        Assert.Same(firstEngine, first.ServiceProvider.GetRequiredService<ApprovalEngine>());
        Assert.NotSame(firstEngine, second.ServiceProvider.GetRequiredService<ApprovalEngine>());
        var firstDb = first.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Same(firstDb, first.ServiceProvider.GetRequiredService<IApprovalHostContext>().Context);
        Assert.Same(firstDb, first.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().Context);
        Assert.NotSame(firstDb, second.ServiceProvider.GetRequiredService<TestDbContext>());
        Assert.Same(first.ServiceProvider.GetRequiredService<IApprovalUnitOfWork>(), first.ServiceProvider.GetRequiredService<ApprovalDbContextAccessor>().UnitOfWork);
        Assert.Same(first.ServiceProvider.GetRequiredService<ApprovalOutboxSignal>(), second.ServiceProvider.GetRequiredService<ApprovalOutboxSignal>());
        _ = first.ServiceProvider.GetRequiredService<ApprovalDefinitionService>();
        _ = first.ServiceProvider.GetRequiredService<ApprovalQueryService>();
        _ = first.ServiceProvider.GetRequiredService<ApprovalOutboxService>();
    }
}
