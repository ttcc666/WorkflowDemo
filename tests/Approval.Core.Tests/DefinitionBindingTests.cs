using Approval.Core.Domain;

namespace Approval.Core.Tests;

public sealed class DefinitionBindingTests
{
    [Fact]
    public async Task Explicit_definition_binding_uses_the_validated_version_when_newer_version_exists()
    {
        await using var host = new TestHost();
        host.Identity.Add("reviewer");
        const string flow = """
        { "startNodeId": "s", "nodes": {
          "s": { "name": "提交", "type": "Start", "next": "r" },
          "r": { "name": "旧版审核", "type": "Approval", "next": "e", "approvers": [{ "type": "User", "value": "reviewer" }] },
          "e": { "name": "结束", "type": "End" }
        } }
        """;
        await host.PublishAsync("bound", flow);
        var validated = await host.RunAsync(sp => sp.GetRequiredService<ApprovalDefinitionService>().GetLatestAsync("bound"));
        await host.PublishAsync("bound", flow.Replace("旧版审核", "新版审核"));

        var instance = await host.RunAsync(sp => sp.GetRequiredService<ApprovalEngine>().StartAsync(
            new StartApprovalRequest("bound", RecordingBusinessHandler.Type, "B1", "alice") { DefinitionId = validated!.Id }));

        Assert.Equal(1, instance.DefinitionVersion);
        Assert.Equal(validated!.Id, instance.DefinitionId);
        Assert.Equal("旧版审核", Assert.Single(instance.Tasks).NodeName);
    }
}
