using Approval.AspNetCore;
using Certificate.Api.Infrastructure;

namespace Certificate.Api.Endpoints;

public static class CertificateOperationsEndpoints
{
    public static void MapCertificateOperations(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        app.MapGet("/health/ready", async (CertificateDatabase database, ILogger<CertificateDatabase> logger, CancellationToken ct) =>
        {
            try
            {
                var result = await database.CheckAsync(ct);
                return result.Ready ? Results.Ok(new { status = "ready" })
                    : Results.Json(new { status = "not-ready" }, statusCode: 503);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "注册证数据库就绪检查失败");
                return Results.Json(new { status = "not-ready" }, statusCode: 503);
            }
        });
        app.MapGet("/api/approval/admin/certificates/diagnostics", async (HttpContext http, CertificateDiagnostics diagnostics,
            CancellationToken ct) =>
        {
            http.GetApprovalUserId();
            return Results.Ok(await diagnostics.GetAsync(ct));
        });
    }
}
