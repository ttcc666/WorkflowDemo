using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Infrastructure;

public sealed class CertificateDatabaseExceptionHandler(ILogger<CertificateDatabaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var sqlite = exception as SqliteException ?? (exception as DbUpdateException)?.InnerException as SqliteException;
        if (sqlite?.SqliteErrorCode is not (5 or 6)) return false;
        logger.LogWarning(exception, "注册证数据库暂时繁忙，TraceId {TraceId}", context.TraceIdentifier);
        context.Response.Headers.RetryAfter = "1";
        await Results.Problem(statusCode: 503, title: "数据库暂时繁忙，请使用原提交标识重试",
            extensions: new Dictionary<string, object?> { ["code"] = "DatabaseBusy" }).ExecuteAsync(context);
        return true;
    }
}
