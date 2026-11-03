using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.AspNetCore;
using Microsoft.AspNetCore.Http;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Certificate.Api.Tests;

public sealed class CertificateConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_submissions_have_one_database_winner(bool sameKey)
    {
        var barrier = new SubmissionBarrier();
        await using var host = await CertificateHost.StartAsync(configure: services =>
            services.AddScoped<IApprovalPermissionProvider>(sp => new BarrierPermissions(barrier, new ApprovalPolicyPermissionProvider(sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IApprovalIdentityProvider>(), sp.GetRequiredService<ApprovalAspNetCoreOptions>()))));
        var key = Guid.NewGuid().ToString();
        var data = CertificateHost.ValidData("reg-race");
        var second = sameKey ? data : data with { CertNo = " REG-RACE " };

        var responses = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, "/api/certificates", body: data, idempotencyKey: key),
            host.SendAsync(HttpMethod.Post, "/api/certificates", body: second, idempotencyKey: sameKey ? key : Guid.NewGuid().ToString()));
        try
        {
            Assert.Equal(2, barrier.Arrivals);
            Assert.Equal(sameKey ? 2 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
            Assert.Equal(sameKey ? 0 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
            Assert.Equal(1, await host.DbAsync(db => db.CertificateNumberReservations.CountAsync()));
            Assert.Equal(1, await host.DbAsync(db => db.Set<ApprovalInstance>().CountAsync()));
            var ids = new List<Guid>();
            foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Accepted))
                ids.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid());
            Assert.Single(ids.Distinct());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Retry_after_completion_replays_original_request_without_side_effects()
    {
        await using var host = await CertificateHost.StartAsync();
        var key = Guid.NewGuid().ToString();
        var id = await host.CreateAsync(key: key);
        await host.CompleteAsync(await host.InstanceIdAsync(id));
        var messages = await host.DbAsync(db => db.Set<ApprovalMessage>().CountAsync());
        host.Clock.SetUtcNow(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(id, await host.CreateAsync(key: key));
        Assert.Equal(1, await host.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(1, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
        Assert.Equal(messages, await host.DbAsync(db => db.Set<ApprovalMessage>().CountAsync()));
        var reservation = await host.DbAsync(db => db.CertificateNumberReservations.AsNoTracking().SingleAsync());
        Assert.Null(reservation.ActiveRequestId);
        Assert.Equal(await host.DbAsync(db => db.Certificates.Select(c => c.Id).SingleAsync()), reservation.CertificateId);
    }

    [Fact]
    public async Task Reusing_key_for_different_content_is_conflict()
    {
        await using var host = await CertificateHost.StartAsync();
        var key = Guid.NewGuid().ToString();
        var id = await host.CreateAsync(key: key);
        using var response = await host.SendAsync(HttpMethod.Post, "/api/certificates",
            body: CertificateHost.ValidData() with { Holder = "不同企业" }, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("示例企业", await host.DbAsync(db => db.CertificateRequests.Where(r => r.Id == id).Select(r => r.Snapshot.Holder).SingleAsync()));
    }

    [Fact]
    public async Task Concurrent_update_and_revoke_cannot_acquire_the_same_certificate_lock()
    {
        var barrier = new SubmissionBarrier { Enabled = false };
        await using var host = await CertificateHost.StartAsync(configure: services => services.AddScoped<IApprovalPermissionProvider>(sp => new BarrierPermissions(barrier, new ApprovalPolicyPermissionProvider(sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IApprovalIdentityProvider>(), sp.GetRequiredService<ApprovalAspNetCoreOptions>()))));
        var certificate = await host.RegisterAsync();
        barrier.Enabled = true;
        var responses = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, $"/api/certificates/{certificate}", body: CertificateHost.ValidData() with { Holder = "新企业" }),
            host.SendAsync(HttpMethod.Post, $"/api/certificates/{certificate}/revoke", body: new { reason = "申请注销" }));
        try
        {
            Assert.Equal(2, barrier.Arrivals);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await host.DbAsync(db => db.CertificateRequests.CountAsync(r => r.Status == RequestStatus.Reviewing)));
            var row = await host.DbAsync(db => db.Certificates.AsNoTracking().SingleAsync());
            Assert.Equal("示例企业", row.Holder);
            Assert.Equal(await host.DbAsync(db => db.CertificateRequests.Where(r => r.Status == RequestStatus.Reviewing).Select(r => r.Id).SingleAsync()), row.PendingRequestId);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Business_callback_failure_rolls_back_real_certificate_and_reservation_updates()
    {
        var fail = true;
        await using var host = await CertificateHost.StartAsync(configure: services => services.AddScoped<IApprovalBusinessHandler>(sp =>
            new FailingHandler(new CertificateApprovalHandler(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<CertificateDataValidator>()), () => fail)));
        var request = await host.CreateAsync();
        var instance = await host.InstanceIdAsync(request);
        await host.ApproveAsync(instance, "dave");
        await host.ApproveAsync(instance, "quinn");
        var task = await host.TaskAsync(instance, "leo");
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "leo", new { });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await host.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(request, await host.DbAsync(db => db.CertificateNumberReservations.Select(r => r.ActiveRequestId).SingleAsync()));
        Assert.Equal(RequestStatus.Reviewing, await host.DbAsync(db => db.CertificateRequests.Select(r => r.Status).SingleAsync()));
        Assert.Equal(task, await host.TaskAsync(instance, "leo"));
        fail = false;
        await host.ApproveAsync(instance, "leo");
        Assert.Equal(1, await host.DbAsync(db => db.Certificates.CountAsync()));
    }

    private sealed class FailingHandler(CertificateApprovalHandler real, Func<bool> fail) : IApprovalBusinessHandler
    {
        public string BusinessType => CertificateService.BusinessType;
        public async Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct)
        {
            await real.OnApprovedAsync(instance, ct);
            if (fail()) throw new InvalidOperationException("模拟业务回调写回后失败");
        }
        public Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct) => real.OnRejectedAsync(instance, ct);
    }
}

public sealed class SubmissionBarrier
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;
    public bool Enabled { get; set; } = true;
    public int Arrivals => Volatile.Read(ref _arrivals);
    public async Task EnterAsync(CancellationToken ct)
    {
        if (!Enabled || Volatile.Read(ref _arrivals) >= 2) return;
        if (Interlocked.Increment(ref _arrivals) == 2) _gate.TrySetResult();
        await _gate.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }
}
internal sealed class BarrierPermissions(SubmissionBarrier barrier, IApprovalPermissionProvider inner) : IApprovalPermissionProvider
{
    public async Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)
    {
        // Both intents arrive before their transaction starts. Identical keys intentionally execute the business delegate once.
        if (!request.Subject.IsSystem && request.Permission == ApprovalPermissions.InstanceStart) await barrier.EnterAsync(ct);
        return await inner.AuthorizeAsync(request, ct);
    }
}
