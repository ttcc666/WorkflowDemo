using System.Net;
using System.Text.Json;
using Approval.Core.Domain;

namespace Approval.AspNetCore.Tests;

public sealed class MaintenanceEndpointTests
{
    [Fact]
    public async Task ReadinessIsAnonymousBoolean_WhileDiagnosticsAndMaintenanceRequireIndependentRights()
    {
        await using var host = await ApiHost.StartAsync(readiness: true);
        using var readiness = await host.Client.GetAsync("/health/approval");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var body = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "ready" }, body.EnumerateObject().Select(p => p.Name)); Assert.True(body.GetProperty("ready").GetBoolean());
        using var denied = await host.SendAsync(HttpMethod.Get, "/api/approval/diagnostics", "alice"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var diagnostic = await host.GetJsonAsync("/api/approval/diagnostics", "admin"); Assert.True(diagnostic.GetProperty("ready").GetBoolean());
        Assert.DoesNotContain("connection", diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var preview = await host.GetJsonAsync("/api/approval/maintenance/preview?target=Instances&mode=Archive", "admin"); Assert.False(preview.GetProperty("enabled").GetBoolean());
        using var create = await host.SendAsync(HttpMethod.Post, "/api/approval/maintenance/jobs", "admin", new { target = MaintenanceTarget.Instances, mode = MaintenanceMode.Archive, policyHash = preview.GetProperty("policyHash").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        using var anonymous = await host.Client.GetAsync("/api/approval/archives"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
