using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Approval.AspNetCore.Contracts;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;

namespace Approval.AspNetCore.Tests;

public sealed class HttpResponseContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Instance_response_preserves_v1_json_and_detaches_snapshots(bool nullValues)
    {
        var expected = Read("http-instance-v1.json");
        if (nullValues)
        {
            foreach (var name in new[] { "title", "currentNodeId", "completedAt" }) expected[name] = null;
            foreach (var name in new[] { "parentTaskId", "handledBy", "comment", "completedAt", "dueAt", "nextRemindAt", "lastRemindedAt" })
                expected["tasks"]![0]![name] = null;
            foreach (var name in new[] { "nodeId", "nodeName", "comment", "details" }) expected["logs"]![0]![name] = null;
            expected["canWithdraw"] = false;
        }
        var detail = expected.Deserialize<InstanceDetail>(Json)!;
        expected["capabilities"] = new JsonObject();
        expected["tasks"]![0]!["capabilities"] = new JsonObject();
        var response = ApprovalInstanceResponse.From(detail);
        AssertJson(expected, JsonSerializer.SerializeToNode(response, Json)!);
        var sourceTasks = (IList<ApprovalTaskContext>)detail.Tasks;
        var sourceLogs = (IList<ApprovalLogContext>)detail.Logs;
        sourceTasks[0] = sourceTasks[0] with { NodeName = "changed after projection" };
        sourceLogs[0] = sourceLogs[0] with { OperatorId = "another user" };
        Assert.Equal("复审", response.Tasks[0].NodeName);
        Assert.Equal("bob", response.Logs[0].OperatorId);
    }

    [Fact]
    public void Definition_message_and_outbox_responses_preserve_v1_json()
    {
        var values = Read("http-resources-v1.json");
        AssertJson(values["definition"]!, JsonSerializer.SerializeToNode(ApprovalDefinitionResponse.From(values["definition"]!.Deserialize<ApprovalDefinition>(Json)!), Json)!);
        AssertJson(values["message"]!, JsonSerializer.SerializeToNode(ApprovalMessageResponse.From(values["message"]!.Deserialize<ApprovalMessage>(Json)!), Json)!);
        var storedOutbox = values["outbox"]!.DeepClone();
        storedOutbox["payloadJson"] = "{\"secret\":\"stored-only\"}";
        AssertJson(values["outbox"]!, JsonSerializer.SerializeToNode(ApprovalOutboxMessageResponse.From(storedOutbox.Deserialize<ApprovalOutboxMessage>(Json)!), Json)!);
    }

    private static JsonNode Read(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContractFixtures", name)))!;

    private static void AssertJson(JsonNode expected, JsonNode actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected: {expected}\nActual: {actual}");
}
