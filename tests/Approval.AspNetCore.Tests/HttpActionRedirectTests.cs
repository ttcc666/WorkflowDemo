using System.Net;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Actions;
using Approval.Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.AspNetCore.Tests;

[Trait("Category", "Integration")]
public sealed class HttpActionRedirectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_action_rejects_redirect_without_contacting_disallowed_host(bool useHostHandler)
    {
        // TestServer / HTTP stubs do not follow redirects; use real transport on an ephemeral loopback port.
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<ApiDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        builder.Services.AddSingleton<IApprovalIdentityProvider>(new FakeUsers());
        builder.Services.AddApprovalWorkflow<ApiDbContext>(o =>
        {
            o.EnableBackgroundServices = false;
            o.Http.AllowedHosts.Add("127.0.0.1");
        });
        // Host defaults can be registered after the workflow, including a replacement transport and proxy settings.
        builder.Services.ConfigureHttpClientDefaults(b =>
        {
            if (useHostHandler) b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
            else b.ConfigurePrimaryHttpMessageHandler((handler, _) => ((SocketsHttpHandler)handler).UseProxy = false);
        });

        await using var app = builder.Build();
        var redirectedRequests = 0;
        app.MapGet("/ok", () => Results.Ok(new { accepted = true }));
        app.MapGet("/redirect", (HttpContext http) => Results.Redirect($"http://localhost:{http.Request.Host.Port}/target"));
        app.MapGet("/target", () =>
        {
            Interlocked.Increment(ref redirectedRequests);
            return Results.Ok(new { accepted = true });
        });
        await app.StartAsync();

        await using var scope = app.Services.CreateAsyncScope();
        var action = scope.ServiceProvider.GetServices<IApprovalActionHandler>().OfType<HttpActionHandler>().Single();
        var address = new Uri(app.Urls.Single());
        ApprovalActionContext Context(string path) => new()
        {
            Instance = new ApprovalInstance { DefinitionKey = "http", BusinessType = "Doc", BusinessKey = "D1", InitiatorId = "alice" },
            NodeId = "http",
            StepName = "HTTP",
            Parameters = new Dictionary<string, string> { ["url"] = new Uri(address, path).ToString(), ["method"] = "GET" },
            FormData = new Dictionary<string, object?>()
        };

        var allowed = await action.ExecuteAsync(Context("/ok"), CancellationToken.None);
        Assert.Equal(200, allowed.Output!["status"]!.GetValue<int>());
        Assert.True(allowed.Output["body"]!["accepted"]!.GetValue<bool>());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => action.ExecuteAsync(Context("/redirect"), CancellationToken.None));

        Assert.Equal(HttpStatusCode.Found, error.StatusCode);
        Assert.Equal(0, Volatile.Read(ref redirectedRequests));
    }
}
