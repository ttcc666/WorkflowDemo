using Approval.Core;
using Approval.AspNetCore.Endpoints;
using Approval.Core.Abstractions;
using Approval.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Approval.AspNetCore;

internal sealed record ApprovalHttpCommandResult(int Status, JsonElement? Value);

internal static class ApprovalHttpCommands
{
    public const string Header = "Idempotency-Key";

    internal static string? OperationId(HttpContext http)
    {
        if (http.Request.Headers[Header].Count > 1) throw new ApprovalException("只能提供一个 Idempotency-Key") { Code = ApprovalErrorCode.OperationIdRequired };
        return http.Request.Headers[Header].FirstOrDefault();
    }

    internal static bool Managed(HttpContext http, ApprovalPermissionMetadata metadata) =>
        HttpMethods.IsPost(http.Request.Method) || HttpMethods.IsPut(http.Request.Method) || HttpMethods.IsDelete(http.Request.Method)
            ? metadata.Operation != "POST " + DefinitionTransferEndpoints.ValidateRoute
                && !metadata.Permissions.Any(p => p is ApprovalPermissions.DefinitionValidate or ApprovalPermissions.DefinitionSimulate or ApprovalPermissions.FileUpload or ApprovalPermissions.TaskReassignBulk)
            : false;

    public static void Add(RouteGroupBuilder group)
    {
        group.AddEndpointFilterFactory((factory, next) => async context =>
        {
            var http = context.HttpContext;
            var metadata = http.GetEndpoint()!.Metadata.GetMetadata<ApprovalPermissionMetadata>()!;
            if (!Managed(http, metadata)) return await next(context);
            var services = http.RequestServices;
            var isService = services.GetRequiredService<IServiceProviderIsService>();
            var parameters = factory.MethodInfo.GetParameters();
            var original = context.Arguments.ToArray();
            var arguments = new Dictionary<string, object?>();
            for (var i = 0; i < parameters.Length; i++)
                if (parameters[i].ParameterType != typeof(HttpContext) && parameters[i].ParameterType != typeof(CancellationToken)
                    && !isService.IsService(parameters[i].ParameterType)) arguments[parameters[i].Name!] = original[i];
            JsonElement? raw = null;
            if (metadata.Permissions.Contains(ApprovalPermissions.DefinitionPublish) && !arguments.ContainsKey("request"))
            {
                http.Request.EnableBuffering();
                using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
                var text = await reader.ReadToEndAsync(http.RequestAborted); http.Request.Body.Position = 0;
                try { raw = JsonSerializer.Deserialize<JsonElement>(text); }
                catch (JsonException ex) { throw new ApprovalException("流程定义 JSON 无效", ex) { Code = ApprovalErrorCode.InvalidDefinition }; }
            }
            var key = OperationId(http);
            var resource = ApprovalEndpointPermissions.Resource(http, metadata);
            var permission = metadata.Permissions[0];
            foreach (var candidate in metadata.Permissions.Count > 1 ? metadata.Permissions : [])
                if (await services.GetRequiredService<ApprovalAuthorizationService>().CanAsync(candidate, resource, http.RequestAborted)) { permission = candidate; break; }
            try
            {
                var result = await services.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
                    new("http." + metadata.Permissions[0] + "." + http.Request.Method, JsonSerializer.Serialize(new { metadata.Operation, resource }), key,
                        new { arguments, raw, query = http.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToArray()) }, permission, resource),
                    async (sp, token) =>
                    {
                        http.RequestServices = sp;
                        try
                        {
                            for (var i = 0; i < parameters.Length; i++)
                                if (parameters[i].ParameterType == typeof(CancellationToken)) context.Arguments[i] = token;
                                else if (isService.IsService(parameters[i].ParameterType)) context.Arguments[i] = sp.GetRequiredService(parameters[i].ParameterType);
                            if (raw is not null) http.Request.Body.Position = 0;
                            var value = await next(context);
                            var status = (value as IStatusCodeHttpResult)?.StatusCode ?? 200;
                            if (status >= 400) throw new Rejected(value);
                            var body = value is IValueHttpResult httpValue ? httpValue.Value : value is IResult ? null : value;
                            var json = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
                            return new ApprovalHttpCommandResult(status, body is null ? null : JsonSerializer.SerializeToElement(body, body.GetType(), json));
                        }
                        finally
                        {
                            // Authorization on the next attempt must not use this attempt's disposed scope.
                            http.RequestServices = services;
                            for (var i = 0; i < original.Length; i++) context.Arguments[i] = original[i];
                        }
                    }, http.RequestAborted);
                return result.Status == 204 ? Results.NoContent() : Results.Json(result.Value, statusCode: result.Status);
            }
            catch (Rejected rejected) { return rejected.Result; }
        });
    }

    private sealed class Rejected(object? result) : Exception
    { public object? Result { get; } = result; }
}
