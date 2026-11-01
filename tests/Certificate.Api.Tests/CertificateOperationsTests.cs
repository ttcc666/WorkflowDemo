using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Certificate.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Certificate.Api.Tests;

public sealed class CertificateOperationsTests
{
    [Fact]
    public async Task Database_busy_is_retryable_service_unavailable_not_business_conflict()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.DirectoryPath, "pilot.db");
        await using var host = await CertificateHost.StartAsync(directory.DirectoryPath, configuration: new()
        {
            ["ConnectionStrings:Default"] = $"Data Source={path};Pooling=False;Default Timeout=1"
        });
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false))
        {
            using var response = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: CertificateHost.ValidData());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", response.Headers.GetValues("Retry-After").Single());
            Assert.Contains("DatabaseBusy", await response.Content.ReadAsStringAsync());
            transaction.Rollback();
        }
        Assert.Equal(0, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    [Fact]
    public async Task Readiness_detects_missing_edit_lock_without_disclosing_database_details()
    {
        await using var host = await CertificateHost.StartAsync();
        var certificate = await host.RegisterAsync();
        using var submission = await host.SendAsync(HttpMethod.Put, $"/api/certificates/{certificate}", body: CertificateHost.ValidData() with { Name = "变更" });
        Assert.Equal(HttpStatusCode.Accepted, submission.StatusCode);
        await host.DbAsync(async db =>
        {
            (await db.Certificates.SingleAsync()).PendingRequestId = null;
            return await db.SaveChangesAsync();
        });
        using var readiness = await host.SendAsync(HttpMethod.Get, "/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.DoesNotContain(host.DatabasePath, await readiness.Content.ReadAsStringAsync());
        var diagnostics = await host.GetAsync("/api/approval/admin/certificates/diagnostics", "admin");
        Assert.Contains(diagnostics.GetProperty("database").GetProperty("problems").EnumerateArray(), p => p.GetString() == "活动申请缺少对应证书编辑锁");
    }

    [Fact]
    public async Task Monitor_reports_changed_staffing_incident_without_repeating_unchanged_alert()
    {
        var logger = new RecordingLogger<CertificateMonitor>();
        await using var host = await CertificateHost.StartAsync(configure: services => services.AddSingleton<ILogger<CertificateMonitor>>(logger));
        await host.CreateAsync();
        await host.SetLeadersAsync("rd", "missing-user");
        async Task<CertificateDiagnosticsResult> Scan() => await host.RunAsync(sp => sp.GetRequiredService<CertificateMonitor>().ScanOnceAsync());
        Assert.Single((await Scan()).UnstaffedTasks);
        Assert.Single((await Scan()).UnstaffedTasks);
        Assert.Single(logger.Levels, level => level == LogLevel.Warning);
        await host.SetLeadersAsync("rd", "dave");
        Assert.Empty((await Scan()).UnstaffedTasks);
        await host.SetLeadersAsync("rd", "missing-user");
        await Scan();
        Assert.Equal(2, logger.Levels.Count(level => level == LogLevel.Warning));
    }

    [Fact]
    public async Task Startup_preserves_published_versions_and_explicit_publish_is_idempotent()
    {
        using var directory = new TestDirectory();
        Guid originalInstance;
        await using (var host = await CertificateHost.StartAsync(directory.DirectoryPath))
        {
            var flow = FlowDefinition.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Flows", "certificate.create.json")));
            flow.Nodes["dept"].Name = "定制初审";
            using var document = JsonDocument.Parse(flow.ToJson());
            using var response = await host.SendAsync(HttpMethod.Post, "/api/approval/definitions/certificate.create?name=custom", "admin", document.RootElement);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            originalInstance = await host.InstanceIdAsync(await host.CreateAsync());
            Assert.Equal(2, (await host.GetAsync($"/api/approval/instances/{originalInstance}")).GetProperty("definitionVersion").GetInt32());
        }
        await using var restarted = await CertificateHost.StartAsync(directory.DirectoryPath);
        Assert.Equal(2, (await restarted.GetAsync("/api/approval/definitions/certificate.create", "admin")).GetProperty("version").GetInt32());
        Assert.Equal(1, await restarted.PublishFilesAsync(replace: true));
        Assert.Equal(0, await restarted.PublishFilesAsync(replace: true));
        var pending = await restarted.DbAsync(db => db.Set<ApprovalTask>().Where(t => t.InstanceId == originalInstance).SingleAsync());
        Assert.Equal("定制初审", pending.NodeName);
        await restarted.CompleteAsync(originalInstance);
        var next = await restarted.InstanceIdAsync(await restarted.CreateAsync(CertificateHost.ValidData("REG-NEXT")));
        Assert.Equal(3, (await restarted.GetAsync($"/api/approval/instances/{next}")).GetProperty("definitionVersion").GetInt32());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Enqueue(logLevel);
    }
}
