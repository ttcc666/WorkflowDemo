using Approval.Core.Background;
using Microsoft.Extensions.Hosting;
using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class BackgroundRegistrationTests
{
    [Fact]
    public async Task SingleWorker_RetriesOutboxAndScansTimeoutsAtTheirOwnIntervals()
    {
        await using var host = new TestHost(o => o.TimeoutScanInterval = TimeSpan.FromMinutes(2));
        host.Identity.Add("alice"); host.Identity.Add("bob");
        host.Channel.FailTimes = 1;
        await host.PublishAsync("worker", """
            {"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"review"},
              "review":{"type":"Approval","approvers":[{"type":"User","value":"bob"}],"next":"end",
                "timeout":{"minutes":1},"notifications":[{"events":["Enter"],"recipients":[{"type":"Assignees"}],"channels":["Test"]}]},
              "end":{"type":"End"}}}
            """);
        var instance = await host.StartAsync("worker", "one", "alice");
        Task Tick() => host.RunAsync(sp => Assert.IsType<ApprovalWorker>(Assert.Single(sp.GetServices<IHostedService>())).RunOnceAsync());
        await Tick();
        Assert.Equal(OutboxStatus.Pending, Assert.Single(await host.NotificationOutboxAsync()).Status);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await Tick();
        Assert.Equal(OutboxStatus.Succeeded, Assert.Single(await host.NotificationOutboxAsync()).Status);
        Assert.Equal(0, Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks).RemindCount);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await Tick();
        Assert.Equal(1, Assert.Single((await host.GetInstanceAsync(instance.Id)).Tasks).RemindCount);
        Assert.Single(await host.MessagesAsync("bob"));
        Assert.Equal(2, host.Channel.Attempts);
    }

    [Fact]
    public async Task RegistersExactlyOneHostedWorker_AndCanDisableIt()
    {
        await using var enabled = new TestHost();
        await enabled.RunAsync(sp =>
        {
            Assert.IsType<ApprovalWorker>(Assert.Single(sp.GetServices<IHostedService>()));
            return Task.CompletedTask;
        });
        await using var disabled = new TestHost(o => o.EnableBackgroundServices = false);
        await disabled.RunAsync(sp =>
        {
            Assert.Empty(sp.GetServices<IHostedService>());
            Assert.NotNull(sp.GetRequiredService<ApprovalOutboxDispatcher>());
            Assert.NotNull(sp.GetRequiredService<ApprovalTimeoutScanner>());
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveBackgroundIntervals(int seconds)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddApprovalWorkflow<TestDbContext>(o =>
        {
            o.Outbox.PollInterval = TimeSpan.FromSeconds(seconds);
        }));
        Assert.Throws<InvalidOperationException>(() => services.AddApprovalWorkflow<TestDbContext>(o =>
        {
            o.TimeoutScanInterval = TimeSpan.FromSeconds(seconds);
        }));
    }
}
