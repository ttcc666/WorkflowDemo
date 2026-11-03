using System.Net;
using System.Text.Json;

namespace Approval.AspNetCore.Tests;

public sealed class DiagnosticsEndpointTests
{
    [Fact]
    public async Task ReadinessIsAnonymousBoolean_DiagnosticsRequireRights_AndMaintenanceRoutesAreAbsent()
    {
        await using var host = await ApiHost.StartAsync(readiness: true);
        using var readiness = await host.Client.GetAsync("/health/approval");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var body = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "ready" }, body.EnumerateObject().Select(p => p.Name)); Assert.True(body.GetProperty("ready").GetBoolean());
        using var denied = await host.SendAsync(HttpMethod.Get, "/api/approval/diagnostics", "alice"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var diagnostic = await host.GetJsonAsync("/api/approval/diagnostics", "admin"); Assert.True(diagnostic.GetProperty("ready").GetBoolean());
        Assert.DoesNotContain("connection", diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { "/api/approval/maintenance/preview", "/api/approval/maintenance/jobs", "/api/approval/archives" })
        { using var response = await host.SendAsync(HttpMethod.Get, path, "admin"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }
}
