using Approval.AspNetCore;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Engine;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Approval.MultiTenantHost;

public static class TenantApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        configure?.Invoke(builder);
        var directory = new TenantDirectory(builder.Configuration);
        builder.Services.AddSingleton(directory);
        builder.Services.AddSingleton<TenantScopes>();
        builder.Services.AddSingleton<IApprovalHostScopeProvider>(sp => sp.GetRequiredService<TenantScopes>());
        builder.Services.AddScoped<IApprovalContextResolver, TenantActor>();
        var mode = builder.Configuration["Authentication:Mode"] ?? "Jwt";
        if (mode == "Demo")
        {
            if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Demo authentication is restricted to Development.");
            directory.ValidateDemoIdentities();
            builder.Services.AddAuthentication("Demo").AddScheme<AuthenticationSchemeOptions, TenantDemoAuthentication>("Demo", _ => { });
        }
        else if (mode == "Jwt")
        {
            var authority = builder.Configuration["Authentication:Authority"];
            var audience = builder.Configuration["Authentication:Audience"];
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(audience))
                throw new InvalidOperationException("Configure an HTTPS JWT authority and audience.");
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            { o.Authority = authority; o.Audience = audience; o.MapInboundClaims = false; o.RequireHttpsMetadata = true; });
        }
        else throw new InvalidOperationException("Unknown authentication mode.");
        builder.Services.AddAuthorization();
        var connection = builder.Configuration.GetConnectionString("Approval") ?? throw new InvalidOperationException("Configure ConnectionStrings:Approval.");
        builder.Services.AddDbContext<TenantDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IApprovalIdentityProvider, TenantUsers>();
        builder.Services.AddScoped<IApprovalUserDirectory, TenantUsers>();
        builder.Services.AddScoped<IApprovalPermissionProvider, TenantPermissions>();
        builder.Services.AddScoped<IApprovalBusinessHandler, ExpenseLifecycle>();
        builder.Services.AddScoped<IApprovalEventHandler, TenantEvents>();
        builder.Services.AddApprovalWorkflow<TenantDbContext>(o =>
        { o.RequireBusinessHandler = true; o.EnableBackgroundServices = builder.Configuration.GetValue("Approval:EnableBackgroundServices", true); });
        builder.Services.AddApprovalAspNetCore();
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseExceptionHandler();
        if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
        app.UseAuthentication();
        app.Use(async (http, next) =>
        {
            if (http.User.Identity?.IsAuthenticated != true) { await next(); return; }
            using var tenant = http.RequestServices.GetRequiredService<TenantScopes>().FromPrincipal(http.User).Enter();
            await next();
        });
        app.UseAuthorization();
        app.MapApprovalReadiness();
        app.MapApprovalEndpoints().RequireAuthorization();
        app.MapPost("/demo/expenses", async (ExpenseSubmission body, HttpContext http, IApprovalContextResolver actor,
            IApprovalCommandExecutor executor, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.BusinessKey) || body.BusinessKey.Length > 100 || body.Amount <= 0)
                return Results.BadRequest(new { error = "业务键不能为空且最多 100 字符；金额必须为正数。" });
            var keys = http.Request.Headers["Idempotency-Key"];
            if (keys.Count != 1) throw new ApprovalException("需要唯一的 Idempotency-Key") { Code = ApprovalErrorCode.OperationIdRequired };
            var user = actor.Resolve().ActorId!;
            var result = await executor.ExecuteAsync(new("sample.expense.submit", body.BusinessKey, keys[0], body,
                ApprovalPermissions.InstanceStart, new() { DefinitionKey = "expense", BusinessType = "Expense", BusinessKey = body.BusinessKey }),
                async (attempt, token) =>
                {
                    var db = attempt.GetRequiredService<TenantDbContext>();
                    var expense = new Expense { Id = Guid.NewGuid(), BusinessKey = body.BusinessKey, ApplicantId = user, Amount = body.Amount };
                    db.Expenses.Add(expense);
                    var instance = await attempt.GetRequiredService<ApprovalEngine>().StartAsync(new("expense", "Expense", body.BusinessKey, user)
                        { Title = "报销 " + body.BusinessKey }, token);
                    expense.InstanceId = instance.Id;
                    return new ExpenseResult(expense.Id, instance.Id, db.PartitionKey);
                }, ct);
            return Results.Ok(result);
        }).RequireAuthorization();
        app.MapGet("/demo/expenses", async (TenantDbContext db, IApprovalContextResolver actor, IApprovalPermissionProvider permissions, CancellationToken ct) =>
        {
            var user = actor.Resolve().ActorId!;
            if (!await permissions.AuthorizeAsync(new(new(user, false), ApprovalPermissions.InstanceRead, new()), ct)) return Results.Forbid();
            var query = db.Expenses.AsNoTracking();
            if (!await permissions.AuthorizeAsync(new(new(user, false), ApprovalPermissions.InstanceReadAll, new()), ct))
                query = query.Where(e => e.ApplicantId == user);
            return Results.Ok(await query.OrderBy(e => e.BusinessKey).ToListAsync(ct));
        }).RequireAuthorization();
        app.MapGet("/demo/deliveries", async (TenantDbContext db, IApprovalContextResolver actor, IApprovalPermissionProvider permissions, CancellationToken ct) =>
        {
            if (!await permissions.AuthorizeAsync(new(new(actor.Resolve().ActorId!, false), ApprovalPermissions.OutboxRead, new()), ct)) return Results.Forbid();
            return Results.Ok(await db.Receipts.AsNoTracking().OrderBy(r => r.ReceivedAt).ToListAsync(ct));
        }).RequireAuthorization();
        return app;
    }
    public sealed record ExpenseSubmission(string BusinessKey, decimal Amount);
    public sealed record ExpenseResult(Guid ExpenseId, Guid InstanceId, string TenantId);
}
