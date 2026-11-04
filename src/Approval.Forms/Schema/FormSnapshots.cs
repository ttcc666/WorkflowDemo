using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core;
using Approval.Core.Definitions;
namespace Approval.Forms.Schema;
public static class FormSnapshots
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public static FormSchema? GetForm(this FlowDefinition flow)
    {
        if (flow.Form is null) return null;
        if (flow.Form is FormSchema schema) return schema;
        try
        {
            schema = JsonSerializer.Deserialize<FormSchema>(JsonSerializer.Serialize(flow.Form, Json), Json)
                ?? throw new ApprovalException("表单快照不能为空") { Code = ApprovalErrorCode.InvalidDefinition };
            flow.Form = schema;
            return schema;
        }
        catch (JsonException ex) { throw new ApprovalException("表单快照格式错误", ex) { Code = ApprovalErrorCode.InvalidDefinition }; }
    }
}
