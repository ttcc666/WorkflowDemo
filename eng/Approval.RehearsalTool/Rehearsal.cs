using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Domain;
using Approval.ReferenceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.RehearsalTool;

internal static class Rehearsal
{
    private static readonly List<object> Checks = [];
    internal static void Check(bool condition, string name, string? detail = null)
    {
        if (!condition) throw new InvalidOperationException("Rehearsal failed: " + name + (detail is null ? "" : " (" + detail + ")"));
        Checks.Add(new { name, passed = true });
    }
    private static string Root => RehearsalHost.Required("REPORT");
    private static string File(string name) => Path.Combine(Root, name);
    private static string Source => RehearsalHost.Required("SOURCE_CONNECTION");
    private static string Files => RehearsalHost.Required("FILES");
    private static readonly object Body = new { formData = new { amount = 12 }, comment = "rehearsal" };
    private const string Flow = """
        {"schemaVersion":1,"startNodeId":"start","settings":{"startable":true,"titleTemplate":"恢复演练"},
        "form":{"fields":[{"key":"amount","label":"金额","type":"Number","required":true}]},
        "nodes":{"start":{"name":"提交","type":"Start","next":"review"},"review":{"name":"初审","type":"Approval","approvers":[{"type":"Role","value":"Reviewer"}],"next":"final"},
        "final":{"name":"终审","type":"Approval","approvers":[{"type":"Role","value":"FinalReviewer"}],"next":"end"},"end":{"name":"结束","type":"End"}}}
        """;
    internal static async Task RunAsync(string stage)
    {
        Directory.CreateDirectory(Root);
        try
        {
            switch (stage) { case "seed": await Seed(); break; case "auth": await Authentication(); break; case "restore": await Restore(); break; case "network": await Network(); break; }
            Check(Checks.Count > 0, "NonzeroChecks");
            await System.IO.File.WriteAllTextAsync(File(stage + ".json"), JsonSerializer.Serialize(new { stage, provider = RehearsalHost.Required("PROVIDER"), passed = true, checks = Checks }, RehearsalHost.Json));
            Console.WriteLine($"Rehearsal {stage} passed: {Checks.Count} checks.");
        }
        catch (Exception ex)
        {
            var errors = new List<object>();
            for (Exception? current = ex; current is not null; current = current.InnerException)
                errors.Add(new { type = current.GetType().Name, number = current.GetType().GetProperty("Number")?.GetValue(current), sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) });
            await System.IO.File.WriteAllTextAsync(File(stage + ".json"), JsonSerializer.Serialize(new { stage, passed = false, errors, checks = Checks }, RehearsalHost.Json));
            Console.Error.WriteLine(ex is InvalidOperationException && ex.Message.StartsWith("Rehearsal failed:", StringComparison.Ordinal) ? ex.Message : "Rehearsal infrastructure failed: " + JsonSerializer.Serialize(errors));
            Environment.ExitCode = 1;
        }
    }
    private static async Task<Guid> Start(RehearsalHost host, string key, string tenant = "alpha") =>
        (await host.Api(HttpMethod.Post, "/forms/shared/start", tenant + "-alice", key, Body)).GetProperty("instanceId").GetGuid();
    private static async Task Complete(RehearsalHost host, Guid id, string tenant = "alpha")
    {
        foreach (var reviewer in new[] { "bob", "carol" })
        {
            var current = await host.Api(HttpMethod.Get, $"/instances/{id}/my-task", tenant + "-" + reviewer);
            var task = current.GetProperty("task").GetProperty("id").GetGuid();
            await host.Api(HttpMethod.Post, $"/tasks/{task}/approve", tenant + "-" + reviewer, "approve-" + task, new { comment = "restore-verified" }, expected: HttpStatusCode.NoContent);
        }
        Check((await host.Api(HttpMethod.Get, $"/instances/{id}", tenant + "-alice")).GetProperty("status").GetString() == "Approved", "OriginalInstanceCompleted");
    }
    private static async Task Seed()
    {
        await using var host = await RehearsalHost.Start(Source, Files, true);
        foreach (var tenant in new[] { "alpha" }) await host.Api(HttpMethod.Post, "/definitions/shared?name=Recovery", tenant + "-admin", "publish", raw: Flow);
        var running = await Start(host, "running"); var second = await Start(host, "second-running");
        var expired = await Start(host, "expired"); await Complete(host, expired);
        var ended = await Start(host, "ended"); await Complete(host, ended);
        var bytes = Encoding.UTF8.GetBytes("附件恢复：审批材料😀\nversion=1\n");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/approval/files");
        upload.Headers.Authorization = new("Bearer", await RehearsalHost.Token("alpha-alice")); upload.Headers.Add("X-File-Name", "recovery.txt"); upload.Content = new ByteArrayContent(bytes);
        using var uploaded = await host.Client.SendAsync(upload); Check(uploaded.StatusCode == HttpStatusCode.OK, "AttachmentUploaded");
        var fileId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var outboxId = Guid.NewGuid();
        await using (var scope = await host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>(); (await db.Set<ApprovalFile>().SingleAsync(f => f.Id == fileId)).InstanceId = running;
            foreach (var row in await db.Set<ApprovalInstance>().Where(i => i.Id == expired || i.Id == ended).ToListAsync()) row.CompletedAt = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var tombstone = await db.Set<ApprovalOperation>().SingleAsync(o => o.RequestKey == "expired"); tombstone.IsTombstone = true; tombstone.ResultJson = "";
            db.Add(new ApprovalOutboxMessage { Id = outboxId, InstanceId = running, Kind = OutboxKind.Notification, Target = "RehearsalSink", PayloadJson = "{\"event\":\"recover\"}", Status = OutboxStatus.Pending, MaxAttempts = 3, CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        }
        var preview = await host.Api(HttpMethod.Get, "/maintenance/preview?target=Instances&mode=Archive", "alpha-admin"); Check(preview.GetProperty("eligible").GetInt32() == 2, "TwoArchiveCandidates");
        var job = await host.Api(HttpMethod.Post, "/maintenance/jobs", "alpha-admin", "archive-job", new { target = "Instances", mode = "Archive", policyHash = preview.GetProperty("policyHash").GetString() }); var jobId = job.GetProperty("id").GetGuid();
        var partial = await host.Api(HttpMethod.Post, $"/maintenance/jobs/{jobId}/run", "alpha-admin", "archive-batch");
        Check(partial.GetProperty("processed").GetInt32() == 1 && partial.GetProperty("completedBatches").GetInt32() == 1, "PartialMaintenanceCommitted");
        await System.IO.File.WriteAllTextAsync(File("seed-facts.json"), JsonSerializer.Serialize(new { running, second, expired, ended, fileId, outboxId, jobId, fileHash = Convert.ToHexStringLower(SHA256.HashData(bytes)) }, RehearsalHost.Json));
        await System.IO.File.WriteAllTextAsync(File("source-snapshot.json"), await Snapshot(host)); Check(running != second, "IndependentBusinessInstances");
        await using var versionScope = await host.Scope(); var versionDb = versionScope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
        await versionDb.Database.OpenConnectionAsync();
        await System.IO.File.WriteAllTextAsync(File("database.json"), JsonSerializer.Serialize(new { provider = versionDb.Database.ProviderName, serverVersion = versionDb.Database.GetDbConnection().ServerVersion }, RehearsalHost.Json));
    }
    private static async Task<string> Snapshot(RehearsalHost host)
    {
        var snapshot = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var tenant in new[] { "alpha" })
        {
            await using var scope = await host.Scope(tenant); var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
            snapshot[tenant + "/Migrations"] = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            foreach (var entity in db.Model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var query = (IEnumerable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(entity.ClrType).Invoke(db, null)!; var rows = new List<string>();
                foreach (var row in query)
                {
                    var properties = new SortedDictionary<string, object?>(StringComparer.Ordinal); foreach (var p in db.Entry(row).Properties) properties[p.Metadata.Name] = p.CurrentValue;
                    rows.Add(JsonSerializer.Serialize(properties));
                }
                rows.Sort(StringComparer.Ordinal); snapshot[tenant + "/" + entity.Name] = rows;
            }
        }
        return JsonSerializer.Serialize(snapshot, RehearsalHost.Json);
    }
    private static async Task Restore()
    {
        await using var host = await RehearsalHost.Start(RehearsalHost.Required("RESTORE_CONNECTION"), RehearsalHost.Required("RESTORE_FILES"));
        var actual = await Snapshot(host); Check(await System.IO.File.ReadAllTextAsync(File("source-snapshot.json")) == actual, "NativeBackupRestoredEveryPersistentProperty"); await System.IO.File.WriteAllTextAsync(File("restored-snapshot.json"), actual);
        var facts = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(File("seed-facts.json"))).RootElement; var running = facts.GetProperty("running").GetGuid(); var second = facts.GetProperty("second").GetGuid();
        Check(await Start(host, "running") == running && await Start(host, "second-running") == second, "RestoredOriginalKeysReturnOriginalIds");
        using var expired = await host.Send(HttpMethod.Post, "/forms/shared/start", key: "expired", body: Body); Check(expired.StatusCode == HttpStatusCode.Gone, "RestoredTombstoneReturns410");
        var fileId = facts.GetProperty("fileId").GetGuid(); using var download = await host.Send(HttpMethod.Get, "/files/" + fileId);
        Check(download.StatusCode == HttpStatusCode.OK && Convert.ToHexStringLower(SHA256.HashData(await download.Content.ReadAsByteArrayAsync())) == facts.GetProperty("fileHash").GetString(), "RestoredAttachmentBytesAndHash");
        using var foreignFile = await host.Send(HttpMethod.Get, "/files/" + fileId, "outsider"); Check(foreignFile.StatusCode == HttpStatusCode.Forbidden, "RestoredUnknownUserRejected");
        await Complete(host, running); await Complete(host, second); host.Services.GetRequiredService<ApprovalOptions>().Lifecycle.MaxBatchesPerRun = 3; var jobId = facts.GetProperty("jobId").GetGuid();
        JsonElement resumed = default;
        for (var batch = 0; batch < 3; batch++)
        {
            resumed = await host.Api(HttpMethod.Post, $"/maintenance/jobs/{jobId}/run", "alpha-admin", "resume-archive-" + batch);
            if (resumed.GetProperty("status").GetString() == "Completed") break;
        }
        Check(resumed.GetProperty("processed").GetInt32() == 2 && resumed.GetProperty("completedBatches").GetInt32() == 2 && resumed.GetProperty("status").GetString() == "Completed", "RestoredMaintenanceResumesFromCommittedCursor");
        await using var scope = await host.Scope(); var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>(); Check(await db.Set<ApprovalBusinessSlot>().CountAsync() == 0, "RestoredApprovalReleasesBusinessSlot");
        var originalOutbox = await db.Set<ApprovalOutboxMessage>().SingleAsync(o => o.Id == facts.GetProperty("outboxId").GetGuid()); Check(originalOutbox.Status == OutboxStatus.Pending && originalOutbox.PayloadJson == "{\"event\":\"recover\"}", "RestoredOutboxKeepsOriginalIdAndPayload");
    }
    private static async Task Authentication()
    {
        await using var host = await RehearsalHost.Start(Source, Files); var me = await host.Api(HttpMethod.Get, "/me"); Check(me.GetProperty("userId").GetString() == "alpha-alice", "RealOidcSubject");
        using var anonymous = await host.Send(HttpMethod.Get, "/me", anonymous: true); Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "JwtAnonymousRejected");
        var token = await RehearsalHost.Token("alpha-alice"); using var forged = await host.Send(HttpMethod.Get, "/me", token: token[..^8] + "AAAAAAAA"); Check(forged.StatusCode == HttpStatusCode.Unauthorized, "JwtSignatureRejected");
        using var audience = await host.Send(HttpMethod.Get, "/me", token: await RehearsalHost.Token("alpha-alice", "other-api")); Check(audience.StatusCode == HttpStatusCode.Unauthorized, "JwtWrongAudienceRejected");
        using var issuer = await host.Send(HttpMethod.Get, "/me", token: await RehearsalHost.Token("alpha-alice", authority: RehearsalHost.Required("AUTHORITY") + "-other")); Check(issuer.StatusCode == HttpStatusCode.Unauthorized, "JwtWrongIssuerRejected");
        using var member = await host.Send(HttpMethod.Get, "/me", token: await RehearsalHost.Token("outsider")); Check(member.StatusCode == HttpStatusCode.Forbidden, "JwtNonmemberRejected");
        var shortToken = await RehearsalHost.Token("alpha-alice", "short-lived"); var payload = shortToken.Split('.')[1]; payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=').Replace('-', '+').Replace('_', '/');
        var expiry = DateTimeOffset.FromUnixTimeSeconds(JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.GetProperty("exp").GetInt64()); Check(expiry <= DateTimeOffset.UtcNow.AddSeconds(10), "IdentityIssuedBoundedShortToken");
        while (DateTimeOffset.UtcNow <= expiry) await Task.Delay(100);
        using var expired = await host.Send(HttpMethod.Get, "/me", token: shortToken); Check(expired.StatusCode == HttpStatusCode.Unauthorized, "JwtExpiredTokenRejected");
        using var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/approval/me"); spoof.Headers.Authorization = new("Bearer", token); spoof.Headers.Add("X-Tenant-Id", "beta"); using var spoofed = await host.Client.SendAsync(spoof);
        Check(spoofed.StatusCode == HttpStatusCode.OK && (await spoofed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString() == "alpha-alice", "UntrustedHeaderCannotChangeSubject");
    }
    private static async Task Network()
    {
        var fault = new CommitNetworkFault(); await using var host = await RehearsalHost.Start(RehearsalHost.Required("PROXY_CONNECTION"), Files, fault: fault); await CommitNetworkFault.Enabled(false);
        try { using var unavailable = await host.Send(HttpMethod.Post, "/forms/shared/start", key: "network-outage", body: Body); Check(!unavailable.IsSuccessStatusCode, "RealTcpOutageRejectsNewCommand"); }
        finally { await CommitNetworkFault.Enabled(true); }
        await using (var direct = await RehearsalHost.Start(Source, Files)) { await using var scope = await direct.Scope(); Check(!await scope.ServiceProvider.GetRequiredService<ReferenceDbContext>().Set<ApprovalOperation>().AnyAsync(o => o.RequestKey == "network-outage"), "TcpOutageLeavesNoCommittedOperation"); }
        var recovered = await RecoverNetwork(host, "network-outage"); Check(recovered == await Start(host, "network-outage"), "TcpRecoveryOriginalKeyCommitsOnce");
        try
        {
            using var unknown = await host.Send(HttpMethod.Post, "/forms/shared/start", key: "network-lost-ack", body: Body);
            Check(unknown.StatusCode == HttpStatusCode.ServiceUnavailable && (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString() == "CommitOutcomeUnknown", "RealCommitAckLossReturnsUnknown");
        }
        finally { await CommitNetworkFault.RemoveToxic(); }
        Check(fault.Installed, "ActualDownstreamTcpToxicInstalledAtCommit"); Guid committed;
        await using (var direct = await RehearsalHost.Start(Source, Files))
        {
            await using var scope = await direct.Scope(); var db = scope.ServiceProvider.GetRequiredService<ReferenceDbContext>(); var operation = await db.Set<ApprovalOperation>().SingleAsync(o => o.RequestKey == "network-lost-ack"); committed = operation.InstanceId!.Value;
            Check(await db.Set<ApprovalInstance>().CountAsync(i => i.Id == committed) == 1 && await db.Set<ApprovalBusinessSlot>().CountAsync(s => s.InstanceId == committed) == 1, "DirectConnectionVerifiesCommittedResultDespiteLostAck");
        }
        Check(committed == await RecoverNetwork(host, "network-lost-ack") && committed == await Start(host, "network-lost-ack"), "RestoredNetworkReplaysVerifiedIdWithoutDuplicate");
    }
    private static async Task<Guid> RecoverNetwork(RehearsalHost host, string key)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        do
        {
            using var response = await host.Send(HttpMethod.Post, "/forms/shared/start", key: key, body: Body);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Check(true, "NetworkRecoveryHttpSuccess");
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
            }
            Check(response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                "NetworkRecoveryOnlyRetriesInfrastructureFailure");
            await Task.Delay(250); // actual availability predicate and fixed original key, not a probabilistic race
        } while (DateTimeOffset.UtcNow < deadline);
        throw new InvalidOperationException("Rehearsal failed: NetworkRecoveryDeadline");
    }
}
