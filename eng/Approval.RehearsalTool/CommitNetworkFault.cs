using System.Data.Common;
using System.Net.Http.Json;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.RehearsalTool;

internal sealed class CommitNetworkFault : DbTransactionInterceptor
{
    public bool Installed { get; private set; }
    public static HttpClient ProxyClient()
    {
        var uri = new Uri(RehearsalHost.Required("PROXY_CONTROL")); if (!uri.IsLoopback || uri.Scheme != "http") throw new InvalidOperationException("Proxy control must be loopback.");
        var client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Approval-Rehearsals");
        return client;
    }
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
    {
        if (!Installed && eventData.Context?.ChangeTracker.Entries<ApprovalOperation>().Any(e => e.Entity.RequestKey == "network-lost-ack") == true)
        {
            using var http = ProxyClient(); using var response = await http.PostAsJsonAsync("/proxies/db/toxics", new { name = "lost-commit-ack", type = "timeout", stream = "downstream", toxicity = 1, attributes = new { timeout = 500 } }, ct);
            response.EnsureSuccessStatusCode(); Installed = true;
            // No fake exception: COMMIT goes upstream; the proxy drops/closes its real downstream response.
        }
        return result;
    }
    public static async Task Enabled(bool enabled)
    {
        using var http = ProxyClient(); var definition = await http.GetFromJsonAsync<Dictionary<string, object>>("/proxies/db"); definition!["enabled"] = enabled;
        using var response = await http.PostAsJsonAsync("/proxies/db", definition); response.EnsureSuccessStatusCode();
    }
    public static async Task RemoveToxic()
    {
        using var http = ProxyClient(); using var response = await http.DeleteAsync("/proxies/db/toxics/lost-commit-ack"); if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
}
