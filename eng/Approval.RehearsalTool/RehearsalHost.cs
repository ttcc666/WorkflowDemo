using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.ReferenceHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Approval.RehearsalTool;

internal sealed class RehearsalHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Dictionary<string, string> _tokens = [];
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;
    public static string Required(string name) => Environment.GetEnvironmentVariable("APPROVAL_REHEARSAL_" + name) ?? throw new InvalidOperationException("Missing rehearsal setting: " + name);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static HttpClient IdentityClient()
    {
        using var expected = X509CertificateLoader.LoadCertificateFromFile(Required("CERTIFICATE"));
        var hash = expected.GetCertHash(HashAlgorithmName.SHA256);
        return new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, actual, _, errors) => request.RequestUri?.IsLoopback == true && actual is not null
                && actual.NotBefore <= DateTime.Now && actual.NotAfter > DateTime.Now
                && (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) == 0
                && CryptographicOperations.FixedTimeEquals(actual.GetCertHash(HashAlgorithmName.SHA256), hash)
        }) { Timeout = TimeSpan.FromSeconds(30) };
    }
    private RehearsalHost(WebApplication app) { _app = app; Client = app.GetTestClient(); Client.BaseAddress = new("https://localhost"); Client.Timeout = TimeSpan.FromMinutes(3); }
    public static async Task<RehearsalHost> Start(string connection, string files, bool migrate = false, CommitNetworkFault? fault = null)
    {
        var authority = Required("AUTHORITY");
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "https") throw new InvalidOperationException("Rehearsal authority must be explicit loopback HTTPS.");
        var database = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connection };
        var name = database.ContainsKey("Database") ? Convert.ToString(database["Database"]) : Convert.ToString(database["Initial Catalog"]);
        if (name is null || !System.Text.RegularExpressions.Regex.IsMatch(name, "^approval_test_[a-f0-9]+(_restore)?$")) throw new InvalidOperationException("Only owned approval_test databases are allowed.");
        var endpoint = Convert.ToString(database[database.ContainsKey("Host") ? "Host" : "Server"])!.Split(',')[0];
        if (endpoint is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Database rehearsal endpoints must be loopback.");
        var app = ReferenceApplication.Build(["--environment", "Production"], builder =>
        {
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.Sources.Clear(); builder.Configuration.AddJsonFile(Required("CONFIG"));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = "Jwt", ["Authentication:Authority"] = authority, ["Authentication:Audience"] = "approval-api",
                ["Database:Provider"] = Required("PROVIDER"), ["ConnectionStrings:Approval"] = connection,
                ["Approval:FileRoot"] = files, ["Approval:EnableBackgroundServices"] = "false"
            });
            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Backchannel = IdentityClient(); options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                options.Events.OnAuthenticationFailed = context =>
                {
                    var codes = System.Text.RegularExpressions.Regex.Matches(context.Exception.ToString(), "IDX[0-9]+")
                        .Select(m => m.Value).Distinct().ToArray();
                    Console.Error.WriteLine("JWT validation failed: " + context.Exception.GetType().Name + "; " + string.Join(",", codes));
                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = context =>
                {
                    Console.Error.WriteLine("Validated JWT claim types: " + string.Join(",", context.Principal!.Claims.Select(c => c.Type).Distinct()));
                    return Task.CompletedTask;
                };
            });
            if (fault is not null)
                switch (Required("PROVIDER"))
                {
                    case "MySql": builder.Services.ConfigureDbContext<MySqlReferenceContext>(o => o.AddInterceptors(fault)); break;
                    case "SqlServer": builder.Services.ConfigureDbContext<SqlServerReferenceContext>(o => o.AddInterceptors(fault)); break;
                    case "PostgreSql": builder.Services.ConfigureDbContext<PostgreSqlReferenceContext>(o => o.AddInterceptors(fault)); break;
                }
        });
        var workflow = app.Services.GetRequiredService<ApprovalOptions>(); workflow.CommitVerificationTimeout = TimeSpan.FromSeconds(3);
        workflow.Lifecycle.Enabled = true; workflow.Lifecycle.AllowDeletion = true; workflow.Lifecycle.ArchiveAfter = TimeSpan.FromDays(1);
        workflow.Lifecycle.OperationsAfter = TimeSpan.FromDays(1); workflow.Lifecycle.BatchSize = 1; workflow.Lifecycle.MaxBatchesPerRun = 1;
        try { if (migrate) await ReferenceDatabase.MigrateAsync(app.Services); await app.StartAsync(); return new(app); }
        catch { await app.DisposeAsync(); throw; }
    }
    public static async Task<string> Token(string user, string client = "approval-api", string? authority = null)
    {
        using var http = IdentityClient(); using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "password", ["client_id"] = client, ["username"] = user, ["password"] = Required("PASSWORD") });
        using var response = await http.PostAsync((authority ?? Required("AUTHORITY")) + "/protocol/openid-connect/token", content);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Identity token endpoint rejected rehearsal user/client.");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, string user = "alpha-alice", string? key = null, object? body = null, string? raw = null, string? token = null, bool anonymous = false)
    {
        using var request = new HttpRequestMessage(method, "/api/approval" + path);
        if (!anonymous) { if (token is null && !_tokens.TryGetValue(user, out token)) _tokens[user] = token = await Token(user); request.Headers.Authorization = new("Bearer", token); }
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (raw is not null) request.Content = new StringContent(raw, System.Text.Encoding.UTF8, "application/json"); else if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    public async Task<JsonElement> Api(HttpMethod method, string path, string user = "alpha-alice", string? key = null, object? body = null, string? raw = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await Send(method, path, user, key, body, raw); Rehearsal.Check(response.StatusCode == expected, "HttpStatus", path + ": " + (int)response.StatusCode);
        return expected == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public async Task<Approval.Core.Hosting.ApprovalScope> Scope(string tenant = "alpha") => await Services.GetRequiredService<IApprovalScopeFactory>().CreateAsync();
    public async ValueTask DisposeAsync() { Client.Dispose(); await _app.DisposeAsync(); }
}
