using System.Text.Json;
using System.Text.Json.Serialization;

namespace Approval.Core.Definitions;

/// <summary>协议版本只接受 JSON 整数值；1 与 1.0 含义相同，不接受字符串或 null。</summary>
internal sealed class FlowSchemaVersionConverter : JsonConverter<int>
{
    public FlowSchemaVersionConverter()
    { }

    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value)
            || value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new JsonException("schemaVersion 必须为 JSON 整数");
        return (int)value;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
