using System.Net;
using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Certificate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Certificate.Api.Tests;

public sealed class CertificateRecoveryTests
{
    [Fact]
    public async Task Host_restart_preserves_pending_stage_and_completes_the_same_application()
    {
        using var directory = new TestDirectory();
        Guid instance;
        await using (var first = await CertificateHost.StartAsync(directory.DirectoryPath))
        {
            instance = await first.InstanceIdAsync(await first.CreateAsync());
            await first.ApproveAsync(instance, "dave");
        }
        await using var restarted = await CertificateHost.StartAsync(directory.DirectoryPath);
        Assert.Equal("quality", (await restarted.GetAsync($"/api/approval/instances/{instance}")).GetProperty("currentNodeId").GetString());
        await restarted.ApproveAsync(instance, "quinn");
        await restarted.ApproveAsync(instance, "leo");
        Assert.Equal(1, await restarted.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(1, await restarted.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    [Fact]
    public async Task Failed_delivery_retries_without_repeating_certificate_effect()
    {
        await using var host = await CertificateHost.StartAsync();
        await EnableWebhookAsync(host);
        host.Http.Failures = 1;
        await host.RegisterAsync();
        var queued = await host.DbAsync(db => db.Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync());

        await DispatchAsync(host);
        var first = await host.DbAsync(db => db.Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync());
        Assert.Equal(OutboxStatus.Pending, first.Status);
        Assert.Equal(1, first.Attempts);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync(host);

        var result = await host.DbAsync(db => db.Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync());
        Assert.Equal(OutboxStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, host.Http.Calls.Count);
        Assert.All(host.Http.Calls, call => Assert.Equal(queued.Id.ToString(), call.Key));
        Assert.Equal(1, await host.DbAsync(db => db.Certificates.CountAsync()));
    }

    [Fact]
    public async Task Persisted_delivery_lease_is_reclaimed_after_restart_only_when_expired()
    {
        using var directory = new TestDirectory();
        Guid messageId;
        await using (var first = await CertificateHost.StartAsync(directory.DirectoryPath))
        {
            await EnableWebhookAsync(first);
            await first.RegisterAsync();
            messageId = await first.DbAsync(async db =>
            {
                var message = await db.Set<ApprovalOutboxMessage>().SingleAsync();
                message.Attempts = 1;
                message.ConcurrencyStamp++;
                message.NextAttemptAt = first.Clock.GetUtcNow().UtcDateTime.AddMinutes(5);
                await db.SaveChangesAsync();
                return message.Id;
            });
        }
        await using var restarted = await CertificateHost.StartAsync(directory.DirectoryPath);
        restarted.Clock.Advance(TimeSpan.FromMinutes(4));
        await DispatchAsync(restarted);
        Assert.Empty(restarted.Http.Calls);
        restarted.Clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync(restarted);
        Assert.Equal(messageId.ToString(), Assert.Single(restarted.Http.Calls).Key);
        var result = await restarted.DbAsync(db => db.Set<ApprovalOutboxMessage>().AsNoTracking().SingleAsync());
        Assert.Equal(OutboxStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(1, await restarted.DbAsync(db => db.Certificates.CountAsync()));
    }

    [Fact]
    public async Task Online_sqlite_backup_restores_application_links_and_pending_work()
    {
        using var destination = new TestDirectory();
        await using var host = await CertificateHost.StartAsync();
        var request = await host.CreateAsync();
        var instance = await host.InstanceIdAsync(request);
        await host.ApproveAsync(instance, "dave");
        var backup = await host.RunAsync(sp => sp.GetRequiredService<CertificateDatabase>()
            .BackupAsync(Path.Combine(destination.DirectoryPath, "pilot.db")));
        Assert.Equal(1, backup.Requests);
        Assert.Equal(0, backup.Certificates);
        await using var restored = await CertificateHost.StartAsync(destination.DirectoryPath);
        Assert.Equal(instance, await restored.InstanceIdAsync(request));
        await restored.ApproveAsync(instance, "quinn");
        await restored.ApproveAsync(instance, "leo");
        Assert.Equal(1, await restored.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(0, await host.DbAsync(db => db.Certificates.CountAsync()));
    }

    [Fact]
    public async Task Backup_refuses_to_overwrite_source_or_existing_destination()
    {
        using var destination = new TestDirectory();
        await using var host = await CertificateHost.StartAsync();
        await host.CreateAsync();
        var occupied = Path.Combine(destination.DirectoryPath, "occupied.db");
        await File.WriteAllTextAsync(occupied, "keep-me");
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(sp => sp.GetRequiredService<CertificateDatabase>().BackupAsync(host.DatabasePath)));
        await Assert.ThrowsAsync<IOException>(() => host.RunAsync(sp => sp.GetRequiredService<CertificateDatabase>().BackupAsync(occupied)));
        Assert.Equal("keep-me", await File.ReadAllTextAsync(occupied));
        Assert.Equal(1, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    private static async Task EnableWebhookAsync(CertificateHost host)
    {
        var flow = FlowDefinition.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Flows", "certificate.create.json")));
        flow.Notifications.Single(n => n.Events.Contains(NotifyEvent.Completed)).Channels.Add("Webhook");
        using var document = JsonDocument.Parse(flow.ToJson());
        using var response = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/certificate.create", "admin", document.RootElement);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<int> DispatchAsync(CertificateHost host) =>
        host.ProcessDueAsync();
}

public sealed class TestDirectory : IDisposable
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "workflow-certificate-tests"));
    public string DirectoryPath { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(DirectoryPath);
    public void Dispose()
    {
        if (!Path.GetFullPath(DirectoryPath).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("非法测试清理路径");
        Directory.Delete(DirectoryPath, recursive: true);
    }
}
