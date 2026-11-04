using System.Net;
using Certificate.Api.Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Certificate.Api.Tests;

public sealed class CertificateBusinessTests
{
    [Fact]
    public async Task Three_reviews_are_required_before_real_certificate_is_created()
    {
        await using var host = await CertificateHost.StartAsync();
        var requestId = await host.CreateAsync();
        var instanceId = await host.InstanceIdAsync(requestId);

        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<AppDbContext>().Certificates.CountAsync()));
        await host.ApproveAsync(instanceId, "dave");
        await host.ApproveAsync(instanceId, "quinn");
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<AppDbContext>().Certificates.CountAsync()));
        await host.ApproveAsync(instanceId, "leo");

        var certificate = await host.RunAsync(sp => sp.GetRequiredService<AppDbContext>().Certificates.AsNoTracking().SingleAsync());
        Assert.Equal(CertificateHost.ValidData(), CertificateData.From(certificate));
        Assert.Equal(CertificateStatus.Active, certificate.Status);
        Assert.Null(certificate.PendingRequestId);
        var detail = await host.GetAsync($"/api/certificate-requests/{requestId}");
        Assert.Equal("Approved", detail.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal("Approved", detail.GetProperty("approval").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Anonymous_certificate_list_is_rejected()
    {
        await using var host = await CertificateHost.StartAsync();
        using var response = await host.SendAsync(HttpMethod.Get, "/api/certificates", user: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_user_cannot_read_request_snapshot()
    {
        await using var host = await CertificateHost.StartAsync();
        var requestId = await host.CreateAsync();
        using var response = await host.SendAsync(HttpMethod.Get, $"/api/certificate-requests/{requestId}", "bob");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("示例企业", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Blank_certificate_number_is_rejected_without_persisting_application()
    {
        await using var host = await CertificateHost.StartAsync();
        using var response = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: CertificateHost.ValidData("   "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.RunAsync(sp => sp.GetRequiredService<AppDbContext>().CertificateRequests.CountAsync()));
    }
}
