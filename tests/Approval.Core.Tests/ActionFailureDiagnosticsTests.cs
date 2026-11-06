using System.Collections.Concurrent;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Tests;

public sealed class ActionFailureDiagnosticsTests
{
    private const string Secret = "password=never-write-this-to-an-action-log";

    [Theory]
    [InlineData("WriteThenFail", "Fail", "InvalidOperationException")]
    [InlineData("Pure", "Continue", "TimeoutException")]
    public async Task Sync_action_reports_structured_failure_and_preserves_failure_policy(string type, string policy, string exceptionType)
    {
        var logs = new ActionLogs();
        await using var host = Host(logs);
        await host.PublishAsync("action", Flow(type, "Sync", policy));
        if (policy == "Fail")
        {
            var error = await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("action", "PO-1", "alice"));
            Assert.DoesNotContain(Secret, error.ToString());
            Assert.Empty(await host.OutboxAsync());
            await host.RunAsync(async sp => Assert.False(await sp.GetRequiredService<TestDbContext>().Set<ApprovalInstance>().AnyAsync()));
        }
        else
        {
            var instance = await host.StartAsync("action", "PO-1", "alice");
            Assert.Equal(InstanceStatus.Approved, instance.Status);
            var audit = Assert.Single(await host.LogsAsync(instance.Id), l => l.Action == ApprovalAction.ActionExecuted);
            using var details = JsonDocument.Parse(audit.Details!);
            Assert.Equal("Failed", details.RootElement.GetProperty("status").GetString());
            Assert.DoesNotContain(Secret, audit.Details);
        }
        await AssertNoHostWrites(host);
        var log = AssertDiagnostic(logs, type, "Sync", "ExecutionFailed", exceptionType);
        Assert.Null(log["OutboxId"]);
    }

    [Theory]
    [InlineData("WriteThenFail", "ActionExecutionFailed", "InvalidOperationException")]
    [InlineData("SaveFails", "ActionDeliveryFailed", "DbUpdateException")]
    public async Task Async_action_diagnoses_handler_or_save_failure_and_rolls_back_host_data(string type, string code, string exceptionType)
    {
        var logs = new ActionLogs();
        await using var host = Host(logs);
        await host.PublishAsync("action", Flow(type, "Async", "Retry"));
        var instance = await host.StartAsync("action", "PO-2", "alice");
        var queued = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");

        await host.DispatchOutboxAsync();

        var failed = Assert.Single(await host.OutboxAsync(), m => m.Id == queued.Id);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal($"WorkflowActionDeliveryException: {code}", failed.LastError);
        await AssertNoHostWrites(host);
        Assert.Equal(InstanceStatus.Approved, (await host.GetInstanceAsync(instance.Id)).Status);
        var log = AssertDiagnostic(logs, type, "Async", code, exceptionType);
        Assert.Equal(instance.Id, log["InstanceId"]);
        Assert.Equal(queued.Id, log["OutboxId"]);
        Assert.Equal(queued.Id, log["ExecutionId"]);
    }

    [Theory]
    [InlineData("Sync")]
    [InlineData("Async")]
    public async Task Caller_cancellation_does_not_become_an_action_failure(string mode)
    {
        using var cancellation = new CancellationTokenSource();
        var logs = new ActionLogs();
        await using var host = Host(logs, cancellation);
        await host.PublishAsync("action", Flow("Cancel", mode, mode == "Async" ? "Retry" : "Fail"));
        if (mode == "Sync")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>()
                .StartAsync(new("action", RecordingBusinessHandler.Type, "PO-cancel", "alice"), cancellation.Token)));
            Assert.Empty(await host.OutboxAsync());
        }
        else
        {
            await host.StartAsync("action", "PO-cancel", "alice");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RunAsync(sp =>
                sp.GetRequiredService<ApprovalOutboxService>().ProcessDueAsync(cancellation.Token)));
            var action = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction");
            Assert.Equal(OutboxStatus.Pending, action.Status);
            Assert.Equal(0, action.Attempts);
            Assert.Null(action.LastError);
        }
        Assert.Empty(logs.Entries);
        await AssertNoHostWrites(host);
    }

    [Fact]
    public async Task Continue_requires_handler_to_declare_no_side_effects()
    {
        await using var host = Host(new());
        var error = await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("action", Flow("WriteThenFail", "Sync", "Continue")));
        Assert.Equal(ApprovalErrorCode.InvalidDefinition, error.Code);
        Assert.Contains("动作不支持该失败策略", error.Message);
    }

    [Fact]
    public async Task Corrupt_action_snapshot_fails_safely_without_calling_the_handler()
    {
        var logs = new ActionLogs();
        await using var host = Host(logs);
        await host.PublishAsync("action", Flow("WriteThenFail", "Async", "Retry"));
        await host.StartAsync("action", "PO-corrupt", "alice");
        var id = Assert.Single(await host.OutboxAsync(), m => m.Target == "WorkflowAction").Id;
        await host.SeedStorageAsync(async db =>
        {
            var row = await db.Set<ApprovalOutboxMessage>().SingleAsync(m => m.Id == id);
            row.PayloadJson = JsonSerializer.Serialize(new { version = Secret });
            await db.SaveChangesAsync();
        });

        await host.DispatchOutboxAsync();

        var failed = Assert.Single(await host.OutboxAsync(), m => m.Id == id);
        Assert.Equal("WorkflowActionDeliveryException: InvalidActionSnapshot", failed.LastError);
        var log = Assert.Single(logs.Entries);
        Assert.Equal("InvalidActionSnapshot", log.Fields["ErrorCode"]);
        Assert.Equal(id, log.Fields["OutboxId"]);
        Assert.Contains("JsonException", Assert.IsType<string>(log.Fields["ExceptionType"]));
        Assert.DoesNotContain(Secret, log.Formatted);
        await AssertNoHostWrites(host);
    }

    private static TestHost Host(ActionLogs logs, CancellationTokenSource? cancellation = null) => new(build: b =>
    {
        b.Services.AddSingleton<ILoggerProvider>(logs);
        b.AddActionHandler<WriteThenFail>().AddActionHandler<Pure>().AddActionHandler<SaveFails>();
        if (cancellation is null) return;
        b.Services.AddSingleton(cancellation);
        b.AddActionHandler<Cancel>();
    });

    private static string Flow(string type, string mode, string policy) => $$$"""
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"action"},
          "action":{"type":"Action","next":"end","actions":[
            {"id":"diagnose","type":"{{{type}}}","mode":"{{{mode}}}","failurePolicy":"{{{policy}}}","config":{}}
          ]},"end":{"type":"End"}
        }}
        """;

    private static Task AssertNoHostWrites(TestHost host) => host.RunAsync(async sp =>
        Assert.False(await sp.GetRequiredService<TestDbContext>().Set<TestRecord>().AnyAsync()));

    private static IReadOnlyDictionary<string, object?> AssertDiagnostic(ActionLogs logs, string handler, string mode, string code, string exceptionType)
    {
        var entry = Assert.Single(logs.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(Secret, entry.Formatted);
        Assert.Equal("diagnose", entry.Fields["ActionId"]);
        Assert.Equal("action", entry.Fields["NodeId"]);
        Assert.Equal(handler, entry.Fields["HandlerType"]);
        Assert.Equal(mode, entry.Fields["Mode"]!.ToString());
        Assert.Equal(code, entry.Fields["ErrorCode"]);
        Assert.Contains(exceptionType, Assert.IsType<string>(entry.Fields["ExceptionType"]));
        var trace = Assert.IsType<string>(entry.Fields["ExceptionStack"]);
        Assert.Contains(" at ", trace);
        if (handler == "WriteThenFail") Assert.Contains("ArgumentException", trace);
        Assert.NotEqual(Guid.Empty, Assert.IsType<Guid>(entry.Fields["ExecutionId"]));
        return entry.Fields;
    }

    private abstract class Handler(string type, bool pure = false) : IApprovalActionHandler
    {
        public ApprovalActionDescriptor Descriptor { get; } = new(type, type, "Test", [ActionExecutionMode.Sync, ActionExecutionMode.Async], AllowsContinue: pure);
        public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => [];
        public abstract Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct);
    }

    private sealed class WriteThenFail(TestDbContext db) : Handler("WriteThenFail")
    {
        public override Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            db.Add(new TestRecord { Id = context.ExecutionId, Name = Secret });
            throw new InvalidOperationException(Secret, new ArgumentException(Secret));
        }
    }

    private sealed class Pure() : Handler("Pure", pure: true)
    {
        public override Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct) =>
            throw new TimeoutException(Secret);
    }

    private sealed class SaveFails(TestDbContext db) : Handler("SaveFails")
    {
        public override Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            db.Add(new TestRecord { Id = context.ExecutionId, Name = Secret, ParentId = Guid.NewGuid() });
            return Task.FromResult(new ApprovalActionResult(true));
        }
    }

    private sealed class Cancel(CancellationTokenSource source) : Handler("Cancel")
    {
        public override Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
        {
            source.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ApprovalActionResult(true));
        }
    }

    private sealed class ActionLogs : ILoggerProvider, ILogger
    {
        internal ConcurrentQueue<(Exception? Exception, string Formatted, IReadOnlyDictionary<string, object?> Fields)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields && fields.Any(p => p.Key == "ExceptionStack"))
                Entries.Enqueue((exception, formatter(state, exception), fields.ToDictionary()));
        }
        public void Dispose() { }
    }
}
