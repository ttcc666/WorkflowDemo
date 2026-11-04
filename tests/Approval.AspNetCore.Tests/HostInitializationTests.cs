using System.Net;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Approval.AspNetCore.Tests;

public sealed class HostInitializationTests
{
    private sealed class Events : IApprovalEventHandler
    {
        public List<ApprovalEvent> Delivered { get; } = [];
        public Task HandleAsync(ApprovalEvent value, CancellationToken ct) { Delivered.Add(value); return Task.CompletedTask; }
    }
    [Fact]
    public async Task EnabledDispatcherWithoutHandler_FailsApplicationStartup()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiHost.StartAsync(workflow: o => o.Outbox.EnableDispatcher = true));
        Assert.Contains("IApprovalEventHandler", error.Message);
        Assert.Contains("EnableDispatcher", error.Message);
    }
    [Fact]
    public async Task RegisteredHandlerThatCannotBeConstructed_FailsStartup()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiHost.StartAsync(
            build: b => b.Services.AddScoped<IApprovalEventHandler>(_ => throw new InvalidOperationException("invalid host handler")),
            workflow: o => o.Outbox.EnableDispatcher = true));
        Assert.Equal("invalid host handler", error.Message);
    }
    [Fact]
    public async Task MissingIdentityProvider_FailsStartupInsteadOfWaitingForCommands()
    {
        var error = await Record.ExceptionAsync(() => ApiHost.StartAsync(build: b => b.Services.RemoveAll<IApprovalIdentityProvider>()));
        Assert.NotNull(error);
        var causes = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];
        Assert.All(causes, cause => Assert.IsType<InvalidOperationException>(cause));
        Assert.Contains(causes, cause => cause.Message.Contains(nameof(IApprovalIdentityProvider), StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitDispatcherConfiguration_PreservesOutbox_AndDeliversOnlyWhenEnabled(bool enabled)
    {
        var events = new Events();
        await using var host = await ApiHost.StartAsync(build: b => { if (enabled) b.Services.AddSingleton<IApprovalEventHandler>(events); },
            workflow: o => o.Outbox.EnableDispatcher = enabled);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/api/approval/me", "alice")).StatusCode);
        Assert.Empty(events.Delivered);
        const string flow = """
            {"schemaVersion":1,"startNodeId":"start","nodes":{"start":{"type":"Start","next":"review"},
            "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end"},"end":{"type":"End"}}}
            """;
        await host.PublishAsync("startup", flow);
        await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(new("startup", DocHandler.Type, "one", "alice")));
        await host.RunAsync(async sp => { await sp.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync(); return true; });
        await host.RunAsync(async sp =>
        {
            var messages = await sp.GetRequiredService<ApiDbContext>().Set<ApprovalOutboxMessage>().ToListAsync();
            Assert.NotEmpty(messages);
            Assert.All(messages, m => Assert.Equal(enabled ? OutboxStatus.Succeeded : OutboxStatus.Pending, m.Status));
            if (enabled) Assert.Equal(messages.Count, events.Delivered.Count); else Assert.Empty(events.Delivered);
            return true;
        });
    }
}
