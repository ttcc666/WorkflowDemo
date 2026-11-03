using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class CertificateGovernanceTests
{
    [Fact]
    public async Task SelectedHistoricalVersion_IsUsedByBusinessPreflight_AndDisabledFlowKeepsOldApproval()
    {
        await using var host = await CertificateHost.StartAsync();
        const string path = "/api/approval/definitions/certificate.create";
        var original = await host.GetAsync(path, "admin");
        var state = await host.GetAsync(path + "/governance", "admin");
        using var draft = await host.SendAsync(HttpMethod.Put, path + "/draft", "admin", new
        {
            name = "New registration", definitionJson = original.GetProperty("definitionJson").GetString(),
            expectedRevision = state.GetProperty("revision").GetInt64(), sourceDefinitionId = original.GetProperty("id").GetGuid()
        });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var saved = await draft.Content.ReadFromJsonAsync<JsonElement>();
        using var publish = await host.SendAsync(HttpMethod.Post, path + "/publish-draft", "admin", new { expectedRevision = saved.GetProperty("revision").GetInt64(), changeNote = "new name" });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        state = await host.GetAsync(path + "/governance", "admin");
        using var select = await host.SendAsync(HttpMethod.Post, path + "/select-version", "admin", new { definitionId = original.GetProperty("id").GetGuid(), expectedRevision = state.GetProperty("revision").GetInt64(), changeNote = "use prior version" });
        Assert.Equal(HttpStatusCode.OK, select.StatusCode);
        var requestId = await host.CreateAsync(CertificateHost.ValidData("governance-selected"));
        var instanceId = await host.InstanceIdAsync(requestId);
        var instance = await host.DbAsync(db => db.Set<ApprovalInstance>().AsNoTracking().SingleAsync(i => i.Id == instanceId));
        Assert.Equal(original.GetProperty("id").GetGuid(), instance.DefinitionId);
        Assert.Equal(original.GetProperty("version").GetInt32(), instance.DefinitionVersion);
        state = await host.GetAsync(path + "/governance", "admin");
        using var disable = await host.SendAsync(HttpMethod.Post, path + "/enabled", "admin", new { enabled = false, expectedRevision = state.GetProperty("revision").GetInt64(), changeNote = "retired" });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        using var blocked = await host.SendAsync(HttpMethod.Post, "/api/certificates", body: CertificateHost.ValidData("governance-blocked"));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(1, await host.DbAsync(db => db.CertificateRequests.CountAsync()));
        await host.CompleteAsync(instanceId);
        Assert.Equal(InstanceStatus.Approved, await host.DbAsync(db => db.Set<ApprovalInstance>().Where(i => i.Id == instanceId).Select(i => i.Status).SingleAsync()));
    }
}
