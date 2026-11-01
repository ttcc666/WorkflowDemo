using System.Net;
using Approval.Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class CertificateAuthorizationTests
{
    [Fact]
    public async Task Lists_and_history_do_not_leak_unrelated_certificates()
    {
        await using var host = await CertificateHost.StartAsync();
        var id = await host.RegisterAsync();
        var list = await host.GetAsync("/api/certificates", "bob");
        Assert.Equal(0, list.GetProperty("total").GetInt32());
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        foreach (var path in new[] { $"/api/certificates/{id}", $"/api/certificates/{id}/requests" })
        {
            using var response = await host.SendAsync(HttpMethod.Get, path, "bob");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(1, (await host.GetAsync("/api/certificates", "admin")).GetProperty("total").GetInt32());
        Assert.False((await host.GetAsync($"/api/certificates/{id}", "quinn")).GetProperty("canManage").GetBoolean());
        using var write = await host.SendAsync(HttpMethod.Put, $"/api/certificates/{id}", "quinn", CertificateHost.ValidData());
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Later_reviewer_cannot_read_or_approve_before_its_stage()
    {
        await using var host = await CertificateHost.StartAsync();
        var request = await host.CreateAsync();
        var instance = await host.InstanceIdAsync(request);
        var task = await host.TaskAsync(instance, "dave");
        using var read = await host.SendAsync(HttpMethod.Get, $"/api/certificate-requests/{request}", "quinn");
        using var approve = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "quinn", new { });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(task, await host.TaskAsync(instance, "dave"));
        await host.ApproveAsync(instance, "dave");
        Assert.Equal(request, (await host.GetAsync($"/api/certificate-requests/{request}", "quinn")).GetProperty("request").GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("same-person")]
    [InlineData("self")]
    [InlineData("unknown-user")]
    public async Task Staffing_preflight_prevents_unhandleable_applications(string scenario)
    {
        await using var host = await CertificateHost.StartAsync();
        if (scenario == "self") await host.SetLeadersAsync("rd", "alice");
        else if (scenario == "unknown-user") await host.SetLeadersAsync("rd", "not-a-directory-user");
        else
        {
            await host.SetRolesAsync("quinn", "Other");
            if (scenario == "same-person") await host.SetRolesAsync("dave", "DeptReviewer", "QualityManager");
        }
        using var response = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: CertificateHost.ValidData());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
        Assert.Equal(0, await host.DbAsync(db => db.CertificateNumberReservations.CountAsync()));
    }

    [Fact]
    public async Task Transfer_addsign_and_in_place_return_cannot_bypass_pilot_roles()
    {
        await using var host = await CertificateHost.StartAsync();
        var instance = await host.InstanceIdAsync(await host.CreateAsync());
        var task = await host.TaskAsync(instance, "dave");
        using var transfer = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/transfer", "dave", new { toUserId = "bob" });
        using var sign = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/add-sign", "dave", new { userIds = new[] { "bob" }, mode = "Parallel" });
        using var back = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/reject", "dave", new { comment = "补正", targetNodeId = "start" });
        Assert.Equal(HttpStatusCode.Forbidden, transfer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, back.StatusCode);
        Assert.Equal(task, await host.TaskAsync(instance, "dave"));
        Assert.Equal(1, await host.DbAsync(db => db.Set<ApprovalTask>().CountAsync()));
    }

    [Fact]
    public async Task Administrative_reassignment_requires_current_qualification_and_preserves_deadline()
    {
        await using var host = await CertificateHost.StartAsync();
        var instance = await host.InstanceIdAsync(await host.CreateAsync());
        var task = await host.TaskAsync(instance, "dave");
        var due = await host.DbAsync(db => db.Set<ApprovalTask>().Where(t => t.Id == task).Select(t => t.DueAt).SingleAsync());
        using var bad = await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/tasks/{task}/reassign", "admin", new { toUserId = "bob", reason = "代办" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await host.SetLeadersAsync("rd", "dan");
        using var stale = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "dave", new { });
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        var diagnostics = await host.GetAsync("/api/approval/admin/certificates/diagnostics", "admin");
        Assert.Single(diagnostics.GetProperty("unstaffedTasks").EnumerateArray());
        using var noReason = await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/tasks/{task}/reassign", "admin", new { toUserId = "dan" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        host.Clock.Advance(TimeSpan.FromHours(1));
        using var good = await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/tasks/{task}/reassign", "admin", new { toUserId = "dan", reason = "负责人调整" });
        Assert.True(good.IsSuccessStatusCode, await good.Content.ReadAsStringAsync());
        var reassigned = await host.TaskAsync(instance, "dan");
        Assert.Equal(due, await host.DbAsync(db => db.Set<ApprovalTask>().Where(t => t.Id == reassigned).Select(t => t.DueAt).SingleAsync()));
        await host.ApproveAsync(instance, "dan");
    }

    [Fact]
    public async Task One_person_cannot_finish_two_distinct_stages_even_after_gaining_the_role()
    {
        await using var host = await CertificateHost.StartAsync();
        var instance = await host.InstanceIdAsync(await host.CreateAsync());
        await host.ApproveAsync(instance, "dave");
        await host.SetRolesAsync("dave", "DeptReviewer", "QualityManager");
        var task = await host.TaskAsync(instance, "quinn");
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "dave", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var reassignment = await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/tasks/{task}/reassign", "admin", new { toUserId = "dave", reason = "重复履职" });
        Assert.Equal(HttpStatusCode.BadRequest, reassignment.StatusCode);
        Assert.Equal(task, await host.TaskAsync(instance, "quinn"));
    }

    [Fact]
    public async Task Demo_mode_is_loopback_only_and_diagnostics_are_admin_only()
    {
        await using var host = await CertificateHost.StartAsync();
        using var ordinary = await host.SendAsync(HttpMethod.Get, "/api/approval/admin/certificates/diagnostics", "alice");
        Assert.Equal(HttpStatusCode.Forbidden, ordinary.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/demo/users");
        request.Headers.Add("X-Test-Remote-IP", "192.0.2.10");
        using var remote = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, remote.StatusCode);
        Assert.Equal("ready", (await host.GetAsync("/health/ready")).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Production_cannot_start_with_demo_identity_or_without_real_authentication(bool demo)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        try
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Certificate:DemoMode"] = demo.ToString() });
            var error = Assert.Throws<InvalidOperationException>(() => builder.AddCertificateApplication());
            Assert.Contains(demo ? "演示身份" : "真实认证", error.Message);
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }
}
