using System.Text.Json;
using Approval.Core.Definitions;
using Approval.Core.Forms;

namespace Approval.Core.Tests;

public sealed class DefinitionContractTests
{
    private sealed record ContractCase(string Name, string Json, bool Valid);
    private static ContractCase[] Cases() => JsonSerializer.Deserialize<ContractCase[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definition-cases.json")), JsonSerializerOptions.Web)!;
    public static IEnumerable<object[]> CaseNames() => Cases().Select(item => new object[] { item.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Shared_definition_contract_accepts_current_or_default_version_only(string name)
    {
        var item = Cases().Single(item => item.Name == name);
        if (!item.Valid)
        {
            Assert.Throws<ApprovalException>(() => FlowDefinition.Parse(item.Json));
            return;
        }
        var definition = FlowDefinition.Parse(item.Json);
        Assert.Equal(1, definition.SchemaVersion);
        var saved = definition.ToJson();
        using var document = JsonDocument.Parse(saved);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var restored = FlowDefinition.Parse(saved);
        Assert.Equal(new[] { "end", "start" }, restored.Nodes.Values.Select(node => node.Id).Order());
        Assert.Equal("end", restored.GetNode("start").Next);
        Assert.Equal(new[] { FormFieldType.Text, FormFieldType.Money }, restored.Form!.Fields.Select(field => field.Type));
        Assert.True(restored.Form.Fields[0].Required);
        Assert.Equal(2, restored.Form.Fields[1].Precision);
    }

    [Fact]
    public void Direct_future_definition_cannot_be_validated_or_serialized_as_current()
    {
        var definition = FlowDefinition.Parse(Cases().First(item => item.Valid).Json);
        definition.SchemaVersion = 2;
        Assert.Contains(FlowDefinitionValidator.Validate(definition, new HashSet<string>()), error => error.Contains("协议版本 2"));
        Assert.Throws<ApprovalException>(() => definition.ToJson());
    }
}
