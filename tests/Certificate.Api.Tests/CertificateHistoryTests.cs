using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class CertificateHistoryTests
{
    [Fact]
    public async Task Historical_before_and_after_survive_later_certificate_updates()
    {
        await using var host = await CertificateHost.StartAsync();
        var certificate = await host.RegisterAsync();
        var updateKey = Guid.NewGuid().ToString();
        var first = await UpdateAsync(host, certificate, "版本二", updateKey);
        await host.CompleteAsync(await host.InstanceIdAsync(first));
        var second = await UpdateAsync(host, certificate, "版本三");
        await host.CompleteAsync(await host.InstanceIdAsync(second));

        var current = await host.GetAsync($"/api/certificates/{certificate}");
        var history = await host.GetAsync($"/api/certificate-requests/{first}");
        Assert.Equal("版本三", current.GetProperty("name").GetString());
        Assert.True(history.GetProperty("request").GetProperty("hasHistoricalBaseline").GetBoolean());
        var difference = Assert.Single(history.GetProperty("changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "Name");
        Assert.Equal("注册证测试资料", difference.GetProperty("before").GetString());
        Assert.Equal("版本二", difference.GetProperty("after").GetString());
        Assert.Equal("注册证测试资料", history.GetProperty("request").GetProperty("beforeSnapshot").GetProperty("name").GetString());
        Assert.Equal(first, await UpdateAsync(host, certificate, "版本二", updateKey));
        Assert.Equal("版本三", (await host.GetAsync($"/api/certificates/{certificate}")).GetProperty("name").GetString());
        Assert.Equal(3, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    [Theory]
    [InlineData("reject", "Rejected")]
    [InlineData("withdraw", "Withdrawn")]
    [InlineData("terminate", "Terminated")]
    public async Task Failed_registration_releases_only_its_own_number_and_reapplication_starts_over(string action, string expected)
    {
        await using var host = await CertificateHost.StartAsync();
        var request = await host.CreateAsync();
        var instance = await host.InstanceIdAsync(request);
        using var response = action switch
        {
            "reject" => await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{await host.TaskAsync(instance, "dave")}/reject", "dave", new { comment = "材料不全" }),
            "withdraw" => await host.SendAsync(HttpMethod.Post, $"/api/approval/instances/{instance}/withdraw", "alice", new { comment = "撤回修改" }),
            _ => await host.SendAsync(HttpMethod.Post, $"/api/approval/admin/instances/{instance}/terminate", "admin", new { reason = "终止核查" })
        };
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await host.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(0, await host.DbAsync(db => db.CertificateNumberReservations.CountAsync()));
        var previous = (await host.GetAsync($"/api/certificate-requests/{request}")).GetProperty("request");
        Assert.Equal(expected, previous.GetProperty("status").GetString());
        Assert.True(previous.GetProperty("canReapply").GetBoolean());
        var data = CertificateHost.ValidData();
        using var next = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: new
        {
            data.CertNo,
            Name = "补正后的资料",
            data.Holder,
            data.CertType,
            data.IssueDate,
            data.ExpireDate,
            sourceRequestId = request
        });
        Assert.Equal(HttpStatusCode.Accepted, next.StatusCode);
        var id = (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        var detail = await host.GetAsync($"/api/certificate-requests/{id}");
        Assert.NotEqual(request, id);
        Assert.Equal(request, detail.GetProperty("request").GetProperty("sourceRequestId").GetGuid());
        Assert.Equal("dept", detail.GetProperty("approval").GetProperty("currentNodeId").GetString());
        Assert.Equal(2, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    [Fact]
    public async Task Final_approval_retries_do_not_duplicate_business_effects()
    {
        await using var host = await CertificateHost.StartAsync();
        var instance = await host.InstanceIdAsync(await host.CreateAsync());
        await host.ApproveAsync(instance, "dave");
        await host.ApproveAsync(instance, "quinn");
        var task = await host.TaskAsync(instance, "leo");
        await host.ApproveAsync(instance, "leo");
        using var replay = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "leo", new { });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(1, await host.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(1, await host.DbAsync(db => db.CertificateNumberReservations.CountAsync()));
    }

    [Fact]
    public async Task Revocation_requires_all_final_signers_and_replay_keeps_number_reserved()
    {
        await using var host = await CertificateHost.StartAsync();
        var certificate = await host.RegisterAsync();
        var key = Guid.NewGuid().ToString();
        using var response = await host.SendAsync(HttpMethod.Post, $"/api/certificates/{certificate}/revoke",
            body: new { reason = "登记资料停用" }, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        var instance = await host.InstanceIdAsync(request);
        await host.ApproveAsync(instance, "quinn");
        await host.ApproveAsync(instance, "sam");
        await host.ApproveAsync(instance, "leo");
        Assert.Equal("Active", (await host.GetAsync($"/api/certificates/{certificate}")).GetProperty("status").GetString());
        await host.ApproveAsync(instance, "lily");
        Assert.Equal("Revoked", (await host.GetAsync($"/api/certificates/{certificate}")).GetProperty("effectiveStatus").GetString());
        using var replay = await host.SendAsync(HttpMethod.Post, $"/api/certificates/{certificate}/revoke",
            body: new { reason = "登记资料停用" }, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(request, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid());
        Assert.Equal(2, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
        Assert.Equal(certificate, await host.DbAsync(db => db.CertificateNumberReservations.Select(n => n.CertificateId).SingleAsync()));
        using var duplicate = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: CertificateHost.ValidData());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task<Guid> UpdateAsync(CertificateHost host, Guid certificate, string name, string? key = null)
    {
        using var response = await host.SendAsync(HttpMethod.Put, $"/api/certificates/{certificate}", body: CertificateHost.ValidData() with { Name = name }, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
    }
}
