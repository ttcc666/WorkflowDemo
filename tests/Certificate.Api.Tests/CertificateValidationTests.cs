using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class CertificateValidationTests
{
    [Theory]
    [InlineData("number", "certNo")]
    [InlineData("name", "name")]
    [InlineData("holder", "holder")]
    [InlineData("control", "name")]
    [InlineData("length", "name")]
    [InlineData("type", "certType")]
    [InlineData("issue", "issueDate")]
    [InlineData("expiry", "expireDate")]
    [InlineData("order", "expireDate")]
    [InlineData("expired", "expireDate")]
    public async Task Invalid_registration_is_rejected_without_partial_rows(string scenario, string field)
    {
        await using var host = await CertificateHost.StartAsync();
        var valid = CertificateHost.ValidData();
        var data = scenario switch
        {
            "number" => valid with { CertNo = "  " },
            "name" => valid with { Name = " " },
            "holder" => valid with { Holder = "" },
            "control" => valid with { Name = "a\u0000b" },
            "length" => valid with { Name = new string('n', 201) },
            "type" => valid with { CertType = "Unknown" },
            "issue" => valid with { IssueDate = default },
            "expiry" => valid with { ExpireDate = default },
            "order" => valid with { ExpireDate = valid.IssueDate },
            _ => valid with { ExpireDate = new DateOnly(2026, 10, 31) }
        };

        using var response = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: data);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Validation", problem.GetProperty("code").GetString());
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty(field).EnumerateArray());
        Assert.Equal(0, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
        Assert.Equal(0, await host.DbAsync(db => db.CertificateNumberReservations.CountAsync()));
        Assert.Equal(0, await host.DbAsync(db => db.Set<Approval.Core.Domain.ApprovalInstance>().CountAsync()));
    }

    [Fact]
    public async Task Normalization_preserves_display_value_and_accepts_exact_length_boundary()
    {
        await using var host = await CertificateHost.StartAsync();
        var number = "reg-" + new string('a', 96);
        var data = CertificateHost.ValidData(" " + number + " ") with { Name = " " + new string('n', 200) + " ", Holder = " 企业 " };
        var id = await host.CreateAsync(data);
        var row = await host.DbAsync(db => db.CertificateRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(number, row.Snapshot.CertNo);
        Assert.Equal(number.ToUpperInvariant(), row.NormalizedCertNo);
        Assert.Equal(new string('n', 200), row.Snapshot.Name);
        Assert.Equal("企业", row.Snapshot.Holder);
    }

    [Fact]
    public async Task Missing_submission_key_is_rejected()
    {
        await using var host = await CertificateHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/certificates") { Content = JsonContent.Create(CertificateHost.ValidData()) };
        request.Headers.Add("X-User-Id", "alice");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
    }

    [Fact]
    public async Task Expiry_uses_business_timezone_and_is_rechecked_before_final_effect()
    {
        await using var host = await CertificateHost.StartAsync();
        var request = await host.CreateAsync(CertificateHost.ValidData() with { ExpireDate = new DateOnly(2026, 11, 1) });
        var instance = await host.InstanceIdAsync(request);
        await host.ApproveAsync(instance, "dave");
        await host.ApproveAsync(instance, "quinn");
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 1, 16, 0, 0, TimeSpan.Zero)); // 上海已到 11 月 2 日
        var task = await host.TaskAsync(instance, "leo");

        using var response = await host.SendAsync(HttpMethod.Post, $"/api/approval/tasks/{task}/approve", "leo", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.DbAsync(db => db.Certificates.CountAsync()));
        Assert.Equal(RequestStatus.Reviewing, await host.DbAsync(db => db.CertificateRequests.Where(r => r.Id == request).Select(r => r.Status).SingleAsync()));
        Assert.Equal(task, await host.TaskAsync(instance, "leo"));
    }
}
