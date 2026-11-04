using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Approval.Core.Persistence;

internal static class ApprovalCommandHash
{
    public static string Of(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Concat(parts.Select(p => $"{Encoding.UTF8.GetByteCount(p)}:{p}")))));

    public static string Payload(object? payload)
    {
        var element = payload is JsonElement json ? json : JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, element);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.Number when value.TryGetDecimal(out var number): writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)); break;
            case JsonValueKind.Undefined: writer.WriteNullValue(); break;
            default: value.WriteTo(writer); break;
        }
    }
}
