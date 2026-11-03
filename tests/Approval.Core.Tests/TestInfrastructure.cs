using Approval.Forms;
using System.Net;
using Approval.Core.Abstractions;
using Approval.Core.Background;
using Approval.Core.Domain;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Approval.Core.Tests;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    { base.OnConfiguring(options); options.UseApproval(); }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureApproval().ConfigureApprovalForms();
        modelBuilder.Entity<TestRecord>().HasIndex(x => x.Name).IsUnique();
    }
}

/// <summary>宿主业务表（动作通过 DbContext 写入，用于验证失败时的回滚）。</summary>
public sealed class TestRecord
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}

/// <summary>用户 → 角色的内存映射。</summary>
public sealed class FakeIdentityProvider : IApprovalIdentityProvider
{
    public Dictionary<string, HashSet<string>> UserRoles { get; } = [];

    public void Add(string userId, params string[] roles)
    {
        if (!UserRoles.TryGetValue(userId, out var set)) UserRoles[userId] = set = [];
        set.UnionWith(roles);
    }

    public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken ct) =>
        Task.FromResult(UserRoles.TryGetValue(userId, out var roles) && roles.Contains(role));

    public Task<IReadOnlyList<string>> GetUserIdsInRoleAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(UserRoles.Where(kv => kv.Value.Contains(role)).Select(kv => kv.Key).Order().ToList());

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(UserRoles.TryGetValue(userId, out var roles) ? roles.ToList() : []);
}

/// <summary>内存组织架构：部门树（负责人）与直属上级。</summary>
public sealed class FakeOrganization : IApprovalOrganizationProvider
{
    public Dictionary<string, (string Name, string? Parent, string[] Leaders)> Departments { get; } = [];
    public Dictionary<string, string> UserDepartments { get; } = [];
    public Dictionary<string, string> Managers { get; } = [];

    public void AddDepartment(string id, string? parent, params string[] leaders) => Departments[id] = (id, parent, leaders);

    /// <summary>用户所在部门与直属上级。</summary>
    public void AddUser(string userId, string? department, string? manager = null)
    {
        if (department is not null) UserDepartments[userId] = department;
        if (manager is not null) Managers[userId] = manager;
    }

    private ApprovalDepartment? Department(string? id) =>
        id is not null && Departments.TryGetValue(id, out var d) ? new ApprovalDepartment(id, d.Name, d.Parent) : null;

    public Task<ApprovalDepartment?> GetDepartmentAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Department(UserDepartments.GetValueOrDefault(userId)));

    public Task<IReadOnlyList<string>> GetDepartmentLeadersAsync(string departmentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Departments.TryGetValue(departmentId, out var d) ? d.Leaders : []);

    public Task<ApprovalDepartment?> GetParentDepartmentAsync(string departmentId, CancellationToken ct) =>
        Task.FromResult(Departments.TryGetValue(departmentId, out var d) ? Department(d.Parent) : null);

    public Task<string?> GetManagerAsync(string userId, CancellationToken ct) => Task.FromResult(Managers.GetValueOrDefault(userId));
}

public sealed class RecordingBusinessHandler : IApprovalBusinessHandler
{
    public const string Type = "Certificate";

    public List<(string Event, string BusinessKey)> Calls { get; } = [];

    /// <summary>设置后，审批通过回调抛出异常，用于验证事务回滚。</summary>
    public bool FailOnApproved { get; set; }

    public string BusinessType => Type;

    public Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct)
    {
        if (FailOnApproved) throw new InvalidOperationException("业务回调失败");
        Calls.Add(("Approved", instance.BusinessKey));
        return Task.CompletedTask;
    }

    public Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct)
    {
        Calls.Add(("Rejected", instance.BusinessKey));
        return Task.CompletedTask;
    }

    public Task OnWithdrawnAsync(ApprovalInstance instance, CancellationToken ct)
    {
        Calls.Add(("Withdrawn", instance.BusinessKey));
        return Task.CompletedTask;
    }
}

public sealed class RecordingChannel : ITestNotificationTarget
{
    public const string ChannelName = "Test";

    public List<ApprovalNotificationRequest> Sent { get; } = [];
    public int Attempts { get; private set; }
    public int FailTimes { get; set; }

    public string Name => ChannelName;
    public string DisplayName => "测试渠道";

    public Task SendAsync(ApprovalNotificationRequest message, CancellationToken ct)
    {
        Attempts++;
        if (Attempts <= FailTimes) throw new HttpRequestException("渠道暂时不可用");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>替代真实网络的 HTTP 处理器：记录请求，按 Respond 返回响应。</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        return Respond(request);
    }
}

/// <summary>每个测试一个独立的 SQLite 内存库。</summary>
public sealed class TestHost : IAsyncDisposable
{
    private sealed class StorageContext(DbContextOptions<StorageContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { model.ConfigureApproval().ConfigureApprovalForms(); model.Entity<TestRecord>().HasIndex(x => x.Name).IsUnique(); }
    }
    // Explicit offline storage fixture for corrupt/legacy rows. Never used by service/security scenarios.
    public async Task SeedStorageAsync(Func<DbContext, Task> seed)
    {
        await using var db = new StorageContext(new DbContextOptionsBuilder<StorageContext>().UseSqlite(_connection).Options);
        await seed(db);
    }
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public FakeIdentityProvider Identity { get; } = new();
    public FakeOrganization Organization { get; } = new();
    public RecordingBusinessHandler Handler { get; } = new();
    public RecordingEvents Events { get; } = new();
    public RecordingChannel Channel { get; } = new();
    public StubHttpHandler Http { get; } = new();

    /// <summary>可控时钟，用于超时与重试测试。</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public TestHost(Action<ApprovalOptions>? configure = null, Action<ApprovalBuilder>? build = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IApprovalIdentityProvider>(Identity);
        services.AddSingleton<IApprovalUserStatusProvider, FixtureUserStatus>();
        services.AddSingleton(Organization);
        services.AddSingleton<IApprovalBusinessHandler>(Handler);
        services.AddSingleton<IApprovalPermissionProvider>(new FixturePermissionProvider(Identity));
        services.AddSingleton(Events);
        services.AddScoped<IApprovalEventHandler, FixtureEventHandler>();
        services.AddSingleton<ITestNotificationTarget>(Channel);
        services.AddSingleton<TimeProvider>(Clock);
        var builder = services.AddApprovalWorkflow<TestDbContext>(o =>
        {
            configure?.Invoke(o);
        });
        services.AddSingleton<Approval.Core.Abstractions.IApprovalDefinitionExtension, Approval.Forms.Schema.FormDefinitionValidator>();
        services.AddSingleton<Approval.Core.Abstractions.IApprovalDataProjection, Approval.Forms.Runtime.FormDataProjection>();
        build?.Invoke(builder);

        var targets = services.Where(d => d.ServiceType == typeof(ITestNotificationTarget) && d.ImplementationInstance is ITestNotificationTarget)
            .Select(d => (ITestNotificationTarget)d.ImplementationInstance!).ToList();
        // Metadata is declared by this host, and actual routing stays in its event handler.
        {
            var options = (ApprovalOptions)services.Last(d => d.ServiceType == typeof(ApprovalOptions)).ImplementationInstance!;
            foreach (var target in targets) options.NotificationTargets.Add(new(target.Name, target.DisplayName));
        }
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
    }

    /// <summary>模拟一次独立的 HTTP 请求（新的 DI 作用域和 DbContext）。</summary>
    public AsyncServiceScope NewScope(bool authorize = false)
    {
        var scope = _provider.CreateAsyncScope();
        if (!authorize)
        {
            scope.ServiceProvider.GetRequiredService<Approval.Core.Hosting.ApprovalExecutionContext>().InitializeTrusted(new("test-fixture"), []);
            scope.ServiceProvider.GetRequiredService<Approval.Core.Persistence.ApprovalDbContextAccessor>().Authorization = Approval.Tests.Infrastructure.FlowFixtureAuthorization.Instance;
        }
        return scope;
    }

    public async Task<T> RunAsAsync<T>(string actor, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = NewScope(authorize: true);
        scope.ServiceProvider.GetRequiredService<Approval.Core.Hosting.ApprovalExecutionContext>().InitializeTrusted(new(actor), []);
        return await action(scope.ServiceProvider);
    }
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = NewScope();
        return await action(scope.ServiceProvider);
    }

    public Task RunAsync(Func<IServiceProvider, Task> action) =>
        RunAsync<object?>(async sp => { await action(sp); return null; });

    public async Task PublishAsync(string key, string flowJson)
    {
        if (!_provider.GetRequiredService<ApprovalOptions>().RequireOperationId)
        {
            await RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().PublishAsync(key, key, flowJson));
            return;
        }
        Identity.Add("fixture-publisher", "Admin");
        await RunAsAsync("fixture-publisher", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("fixture.publish", key, Guid.NewGuid().ToString("N"), flowJson, ApprovalPermissions.DefinitionPublish),
            (attempt, ct) => attempt.GetRequiredService<ApprovalDefinitionService>().PublishAsync(key, key, flowJson, ct)));
    }

    public Task<ApprovalInstance> StartAsync(string key, string businessKey, string initiator, object? form = null) =>
        RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(
            new StartApprovalRequest(key, RecordingBusinessHandler.Type, businessKey, initiator) { FormData = form, Title = $"证书 {businessKey}" }));

    public Task<ApprovalInstance> GetInstanceAsync(Guid id) =>
        RunAsync(async sp => (await sp.GetRequiredService<ApprovalQueryService>().GetInstanceAsync(id))!);

    public async Task<List<TodoItem>> TodoAsync(string userId) =>
        (await RunAsync(sp => sp.GetRequiredService<ApprovalQueryService>().GetTodoAsync(userId, page: new PageRequest(1, PageRequest.MaxPageSize))))
        .Items.ToList();

    /// <summary>取当前用户的唯一待办并同意。</summary>
    public async Task ApproveMyTaskAsync(string userId, string? comment = null)
    {
        var todo = Assert.Single(await TodoAsync(userId));
        await RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(todo.TaskId, userId, comment));
    }

    public Task ApproveAsync(Guid taskId, string userId) =>
        RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().ApproveAsync(taskId, userId));

    public Task<List<ApprovalMessage>> MessagesAsync(string? userId = null) =>
        RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalMessage>()
            .Where(m => userId == null || m.UserId == userId).OrderBy(m => m.CreatedAt).ToListAsync());

    public Task<List<ApprovalOutboxMessage>> OutboxAsync() =>
        RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalOutboxMessage>().OrderBy(m => m.CreatedAt).ToListAsync());

    public async Task<List<ApprovalOutboxMessage>> NotificationOutboxAsync() => (await OutboxAsync())
        .Where(m => m.Kind == OutboxKind.Notification || m.PayloadJson.Contains("\"eventType\":\"NotificationRequested\"", StringComparison.Ordinal)).ToList();

    public Task<List<ApprovalLog>> LogsAsync(Guid instanceId) =>
        RunAsync(sp => sp.GetRequiredService<TestDbContext>().Set<ApprovalLog>()
            .Where(l => l.InstanceId == instanceId).OrderBy(l => l.Sequence).ToListAsync());

    public Task DispatchOutboxAsync() => _provider.GetRequiredService<ApprovalOutboxDispatcher>().DispatchOnceAsync();

    public Task<int> ScanTimeoutsAsync() => _provider.GetRequiredService<ApprovalTimeoutScanner>().ScanOnceAsync();

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

// 流程算法夹具接受测试中声明的任意用户；宿主状态拒绝由专门的状态适配器测试覆盖。
internal sealed class FixtureUserStatus : IApprovalUserStatusProvider
{
    public Task<bool> IsActiveAsync(string userId, CancellationToken ct) => Task.FromResult(!string.IsNullOrWhiteSpace(userId));
}
