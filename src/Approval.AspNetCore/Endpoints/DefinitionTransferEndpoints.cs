using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core;
using Approval.Core.Definitions;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Approval.AspNetCore.Endpoints;

internal static class DefinitionTransferEndpoints
{
    internal const string ExportRoute = "/definitions/by-id/{id:guid}/export";
    internal const string ImportRoute = "/definitions/{key}/import";
    internal const string ValidateRoute = ImportRoute + "/validate";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(ExportRoute, async (Guid id, HttpContext http, ApprovalDefinitionService service, CancellationToken ct) =>
        {
            var version = await service.GetByIdAsync(id, ct);
            if (version is not { IsPublished: true }) return Results.NotFound();
            using var json = JsonDocument.Parse(version.DefinitionJson);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new DefinitionTransferPackage(DefinitionTransferPackage.CurrentPackageVersion,
                DefinitionTransferPackage.DefinitionKind, new(version.Key, version.Name, version.Version,
                    DateTime.SpecifyKind(version.CreatedAt, DateTimeKind.Utc)), json.RootElement.Clone()));
        }).Produces<DefinitionTransferPackage>().Produces(StatusCodes.Status404NotFound);

        group.MapPost(ValidateRoute, async (string key, ImportRequest request, ApprovalDefinitionService service, CancellationToken ct) =>
        {
            if (request.ExceedsLimit) return TooLarge();
            return Results.Ok(await service.ValidateImportAsync(key, request.Decode(), ct));
        }).Accepts<ImportRequest>("application/json").Produces<DefinitionTransferSource>().ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        group.MapPost(ImportRoute, async (string key, ImportRequest request, ApprovalDefinitionService service, CancellationToken ct) =>
        {
            if (request.ExceedsLimit) return TooLarge();
            return Results.Ok(await service.ImportDraftAsync(key, request.Decode(), ct));
        }).Accepts<ImportRequest>("application/json").Produces<DefinitionImportReceipt>().ProducesProblem(StatusCodes.Status413PayloadTooLarge);
    }

    private static IResult TooLarge() => Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "导入文件超过 1 MiB 或请求体超过 1.5 MiB");

    internal sealed record ImportRequest(string PackageBase64)
    {
        private const int MaxRequestBytes = 1536 * 1024;
        private const int MaxBase64Characters = ((DefinitionTransferPackage.MaxFileBytes + 2) / 3) * 4;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        internal bool ExceedsLimit { get; private init; }

        // The existing executor fingerprints the bound file and reuses it across fresh scopes.
        public static async ValueTask<ImportRequest> BindAsync(HttpContext http)
        {
            if (http.Request.ContentLength > MaxRequestBytes) return Oversized();
            if (!http.Request.HasJsonContentType()) return new(string.Empty);
            using var content = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await http.Request.Body.ReadAsync(buffer, http.RequestAborted);
                if (count == 0) break;
                if (content.Length + count > MaxRequestBytes) return Oversized();
                content.Write(buffer.AsSpan(0, count));
            }
            try
            {
                var request = JsonSerializer.Deserialize<ImportRequest>(content.GetBuffer().AsSpan(0, (int)content.Length), Json);
                if (request?.PackageBase64 is not { } encoded) return new(string.Empty);
                if (encoded.Length > MaxBase64Characters) return Oversized();
                var padding = encoded.EndsWith("==", StringComparison.Ordinal) ? 2 : encoded.EndsWith('=') ? 1 : 0;
                return encoded.Length / 4 * 3 - padding > DefinitionTransferPackage.MaxFileBytes ? Oversized() : request;
            }
            catch (JsonException) { return new(string.Empty); }
        }

        private static ImportRequest Oversized() => new(string.Empty) { ExceedsLimit = true };

        public byte[] Decode()
        {
            try { return Convert.FromBase64String(PackageBase64); }
            catch (FormatException ex)
            {
                throw new ApprovalException("导入请求必须包含有效的 packageBase64", ex) { Code = ApprovalErrorCode.InvalidDefinition };
            }
        }
    }
}
