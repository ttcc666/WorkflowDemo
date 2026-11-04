using Approval.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Approval.AspNetCore;

/// <summary>把 <see cref="ApprovalException"/> 转换为 ProblemDetails：<c>title</c> 为可展示的提示，<c>code</c> 为错误码。</summary>
public static class ApprovalProblems
{
    public static int StatusCodeOf(ApprovalErrorCode code) => code switch
    {
        ApprovalErrorCode.NotFound => StatusCodes.Status404NotFound,
        ApprovalErrorCode.Forbidden => StatusCodes.Status403Forbidden,
        ApprovalErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        ApprovalErrorCode.Conflict => StatusCodes.Status409Conflict,
        ApprovalErrorCode.AuthorizationUnavailable => StatusCodes.Status503ServiceUnavailable,
        ApprovalErrorCode.CommitOutcomeUnknown => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemDetails Create(ApprovalException ex)
    {
        var status = StatusCodeOf(ex.Code);
        ProblemDetails problem = ex is ApprovalValidationException v
            // 字段级错误：errors = { "字段": ["错误"] }，前端据此在表单项下显示
            ? new HttpValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = ex.Message;
        problem.Extensions["code"] = ex.Code.ToString();
        return problem;
    }

    public static IResult ToResult(ApprovalException ex)
    {
        var problem = Create(ex);
        return problem is HttpValidationProblemDetails v
            ? TypedResults.ValidationProblem(v.Errors, title: v.Title, extensions: v.Extensions)
            : TypedResults.Problem(problem);
    }
}

/// <summary>宿主自有接口抛出的 <see cref="ApprovalException"/> 也统一转换为 ProblemDetails（需 <c>app.UseExceptionHandler()</c>）。</summary>
public sealed class ApprovalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApprovalException ex) return false;

        var problem = ApprovalProblems.Create(ex);
        http.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
