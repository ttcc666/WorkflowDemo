using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Approval.Core.Definitions;

public sealed record DefinitionTransferSource(string Key, string Name, int Version, DateTime PublishedAt);

/// <summary>Portable, versioned file containing the configuration of a published definition.</summary>
public sealed record DefinitionTransferPackage(int PackageVersion, string Kind, DefinitionTransferSource Source, JsonElement Definition)
{
    public const int CurrentPackageVersion = 1;
    public const string DefinitionKind = "approval-definition";
    public const int MaxFileBytes = 1024 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions PackageJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    internal static DefinitionTransferPackage Parse(ReadOnlyMemory<byte> file)
    {
        if (file.Length > MaxFileBytes) throw Invalid("导入文件超过 1 MiB");
        try
        {
            var bytes = file.Span;
            if (bytes.StartsWith("\uFEFF"u8)) bytes = bytes[3..];
            _ = Utf8.GetCharCount(bytes);
            var package = JsonSerializer.Deserialize<DefinitionTransferPackage>(bytes, PackageJson)
                ?? throw Invalid("流程定义包必须是 JSON 对象");
            if (package.PackageVersion != CurrentPackageVersion || package.Kind != DefinitionKind)
                throw Invalid("不支持的流程定义包类型或版本");
            if (package.Source is not { Version: > 0 } source || string.IsNullOrWhiteSpace(source.Key) || source.Key.Length > 100
                || source.Key.Any(char.IsControl) || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 200
                || source.PublishedAt == default || package.Definition.ValueKind != JsonValueKind.Object)
                throw Invalid("流程定义包来源或 definition 无效");
            return package;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw Invalid("流程定义包必须是有效的 UTF-8 JSON", ex);
        }
    }

    private static ApprovalException Invalid(string message, Exception? inner = null) =>
        new(message, inner) { Code = ApprovalErrorCode.InvalidDefinition };
}
