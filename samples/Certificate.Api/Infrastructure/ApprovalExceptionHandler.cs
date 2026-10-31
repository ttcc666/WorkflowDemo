using Approval.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Certificate.Api.Infrastructure;

/// <summary>把审批业务异常统一转换为 ProblemDetails。</summary>
public sealed class ApprovalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApprovalException ex) return false;

        http.Response.StatusCode = ex.Code switch
        {
            ApprovalErrorCode.NotFound => StatusCodes.Status404NotFound,
            ApprovalErrorCode.Forbidden => StatusCodes.Status403Forbidden,
            ApprovalErrorCode.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = http.Response.StatusCode,
                Title = ex.Message,
                Extensions = { ["code"] = ex.Code.ToString() }
            }
        });
    }
}
