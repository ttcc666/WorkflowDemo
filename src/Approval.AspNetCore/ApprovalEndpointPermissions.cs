using Approval.Core.Abstractions;
using Approval.AspNetCore.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Approval.AspNetCore;

public sealed record ApprovalPermissionMetadata(IReadOnlyList<string> Permissions, string? ResourceKind)
{
    public string Operation { get; init; } = string.Empty;
}

internal sealed class ApprovalEndpointValidationService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        // Minimal API conventions are lazy. Build the route table before accepting requests.
        _ = services.GetService<EndpointDataSource>()?.Endpoints.Count;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

internal static class ApprovalEndpointPermissions
{
    public static ApprovalPermissionMetadata For(string method, string path)
    {
        var permission = (method, path) switch
        {
            ("GET", "/me") => ApprovalPermissions.SessionRead,
            ("GET", "/diagnostics") => ApprovalPermissions.DiagnosticsRead,
            ("GET", "/directory/users" or "/directory/roles") => ApprovalPermissions.DirectoryRead,
            ("GET", "/meta" or "/flows") => ApprovalPermissions.DefinitionRead,
            ("GET", "/definitions" or "/definitions/{key}" or "/definitions/{key}/versions" or "/definitions/by-id/{id:guid}" or DefinitionTransferEndpoints.ExportRoute) => ApprovalPermissions.DefinitionReadSensitive,
            ("POST", "/definitions/simulate") => ApprovalPermissions.DefinitionSimulate,
            ("POST", "/definitions/validate") => ApprovalPermissions.DefinitionValidate,
            ("POST", "/definitions/{key}") => ApprovalPermissions.DefinitionPublish,
            ("GET", "/definitions/governance" or "/definitions/{key}/governance" or "/definitions/{key}/audit") => ApprovalPermissions.DefinitionReadSensitive,
            ("GET", "/definitions/categories") => ApprovalPermissions.DefinitionReadSensitive,
            ("PUT", "/definitions/{key}/draft") => ApprovalPermissions.DefinitionDesign,
            ("POST", "/definitions/{key}/copy" or DefinitionTransferEndpoints.ImportRoute or DefinitionTransferEndpoints.ValidateRoute) => ApprovalPermissions.DefinitionDesign,
            ("POST", "/definitions/{key}/publish-draft" or "/definitions/{key}/select-version") => ApprovalPermissions.DefinitionPublish,
            ("POST", "/definitions/{key}/enabled") => ApprovalPermissions.DefinitionDisable,
            ("GET", "/todo" or "/todo/count" or "/instances/{id:guid}/my-task" or "/tasks/{id:guid}/can-handle") => ApprovalPermissions.TaskRead,
            ("GET", "/done" or "/started" or "/cc" or "/instances" or "/instances/{id:guid}" or "/instances/{id:guid}/definition") => ApprovalPermissions.InstanceRead,
            ("GET", "/carbon-copies" or "/carbon-copies/{id:guid}" or "/instances/{id:guid}/carbon-copies") => ApprovalPermissions.InstanceRead,
            ("GET", "/carbon-copies/{id:guid}/recipients") => ApprovalPermissions.DirectoryRead,
            ("POST", "/carbon-copies/{id:guid}/read") => ApprovalPermissions.InstanceRead,
            ("POST", "/instances/{id:guid}/withdraw") => ApprovalPermissions.InstanceWithdraw,
            ("POST", "/tasks/{id:guid}/transfer") => ApprovalPermissions.TaskTransfer,
            ("POST", "/tasks/{id:guid}/add-sign") => ApprovalPermissions.TaskAddSign,
            ("POST", "/tasks/{id:guid}/resubmit") => ApprovalPermissions.TaskResubmit,
            ("GET", "/tasks/{id:guid}/return-targets") => ApprovalPermissions.TaskReturn,
            ("GET", "/messages" or "/messages/unread-count") => ApprovalPermissions.MessageRead,
            ("POST", "/messages/{id:guid}/read" or "/messages/read-all") => ApprovalPermissions.MessageWrite,
            ("GET", "/outbox" or "/outbox/{id:guid}/payload") => ApprovalPermissions.OutboxRead,
            ("POST", "/outbox/{id:guid}/retry") => ApprovalPermissions.OutboxRetry,
            ("GET", "/admin/instances") => ApprovalPermissions.InstanceReadAll,
            ("GET", "/admin/users/{userId}/tasks") => ApprovalPermissions.TaskReassignBulk,
            ("POST", "/admin/users/{userId}/reassign") => ApprovalPermissions.TaskReassignBulk,
            ("POST", "/admin/instances/{id:guid}/terminate") => ApprovalPermissions.InstanceTerminate,
            ("POST", "/admin/tasks/{id:guid}/reassign") => ApprovalPermissions.TaskReassign,
            ("GET", "/admin/statistics") => ApprovalPermissions.StatisticsRead,
            ("POST", "/files") => ApprovalPermissions.FileUpload,
            ("GET", "/files/{id:guid}") => ApprovalPermissions.FileRead,
            ("GET", "/forms/startable" or "/forms/{key}") => ApprovalPermissions.InstanceStart,
            ("POST", "/forms/{key}/start") => ApprovalPermissions.InstanceStart,
            ("GET", "/forms/drafts" or "/forms/drafts/{id:guid}") => ApprovalPermissions.DraftRead,
            ("PUT", "/forms/{key}/draft") => ApprovalPermissions.DraftWrite,
            ("DELETE", "/forms/drafts/{id:guid}") => ApprovalPermissions.DraftDelete,
            ("POST", "/tasks/{id:guid}/approve") => ApprovalPermissions.TaskApprove,
            ("POST", "/tasks/{id:guid}/reject") => ApprovalPermissions.TaskReject,
            _ => throw new InvalidOperationException($"审批端点缺少权限元数据：{method} {path}")
        };
        string? kind = path switch
        {
            "/definitions/by-id/{id:guid}" or DefinitionTransferEndpoints.ExportRoute => "definition",
            _ when path.Contains("/tasks/{id:guid}", StringComparison.Ordinal) => "task",
            _ when path.Contains("/instances/{id:guid}", StringComparison.Ordinal) => "instance",
            "/files/{id:guid}" => "file",
            "/forms/drafts/{id:guid}" => "draft",
            "/outbox/{id:guid}/retry" or "/outbox/{id:guid}/payload" => "outbox",
            "/messages/{id:guid}/read" => "message",
            _ => null
        };
        var codes = path == "/tasks/{id:guid}/approve" ? new[] { permission, ApprovalPermissions.TaskResubmit }
            : path == "/tasks/{id:guid}/reject" ? new[] { permission, ApprovalPermissions.TaskReturn } : new[] { permission };
        return new(codes, kind) { Operation = method + " " + path };
    }

    public static ApprovalResource Resource(HttpContext http, ApprovalPermissionMetadata metadata)
    {
        Guid? id = Guid.TryParse(Convert.ToString(http.Request.RouteValues["id"]), out var value) ? value : null;
        return new()
        {
            DefinitionKey = Convert.ToString(http.Request.RouteValues["key"]),
            DefinitionId = metadata.ResourceKind == "definition" ? id : null,
            InstanceId = metadata.ResourceKind == "instance" ? id : null,
            TaskId = metadata.ResourceKind == "task" ? id : null,
            FileId = metadata.ResourceKind == "file" ? id : null,
            DraftId = metadata.ResourceKind == "draft" ? id : null,
            OutboxId = metadata.ResourceKind == "outbox" ? id : null,
            MessageId = metadata.ResourceKind == "message" ? id : null,
            TargetUserId = Convert.ToString(http.Request.RouteValues["userId"])
        };
    }
}
