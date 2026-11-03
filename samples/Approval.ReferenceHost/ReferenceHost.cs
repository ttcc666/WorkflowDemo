using Approval.AspNetCore;
using Approval.Core;
using Approval.Core.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Approval.ReferenceHost;

public static class ReferenceApplication
{
    // Both tests and Program use this composition root; no test-only permission bypass.
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var directory = new ReferenceDirectory(builder.Configuration);
        builder.Services.AddSingleton(directory);
        builder.Services.AddScoped<IApprovalContextResolver, DirectoryContextResolver>();
        var mode = builder.Configuration["Authentication:Mode"] ?? "Jwt";
        if (mode == "Demo")
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Demo authentication is restricted to Development.");
            directory.ValidateDemoIdentities();
            builder.Services.AddAuthentication("Demo").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DemoAuthenticationHandler>("Demo", _ => { });
        }
        else if (mode == "Jwt")
        {
            var authority = builder.Configuration["Authentication:Authority"];
            var audience = builder.Configuration["Authentication:Audience"];
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(audience))
                throw new InvalidOperationException("Configure an HTTPS JWT authority and audience.");
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            {
                o.Authority = authority;
                o.Audience = audience;
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = true;
            });
        }
        else throw new InvalidOperationException("Unknown authentication mode.");

        builder.Services.AddAuthorization();
        ReferenceDatabase.Register(builder.Services, builder.Configuration, approval =>
        {
            approval.AddIdentityProvider<DirectoryIdentityProvider>()
                .AddUserDirectory<DirectoryIdentityProvider>()
                .AddPermissionProvider<DirectoryPermissionProvider>()
                .AddFormApprovals();
        });
        builder.Services.AddApprovalAspNetCore();
        var app = builder.Build();
        app.UseExceptionHandler();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapApprovalReadiness();
        app.MapApprovalEndpoints().RequireAuthorization();
        if (mode == "Demo")
        {
            // Represents the third-party access console, never part of the framework API.
            app.MapPut("/demo/grants/{userId}/{permission}", (string userId, string permission, GrantChange body,
                IApprovalContextResolver resolver, ReferenceDirectory access) =>
            {
                var identity = resolver.Resolve();
                if (!access.InRole(identity.ActorId!, "AccessAdmin"))
                    return Results.Forbid();
                if (!ApprovalPermissions.All.Contains(permission) || permission.StartsWith("approval.system.", StringComparison.Ordinal))
                    return Results.BadRequest();
                return access.ChangeGrant(userId, permission, body.Allowed)
                    ? Results.NoContent() : Results.NotFound();
            }).RequireAuthorization();
        }
        return app;
    }
    public sealed record GrantChange(bool Allowed);
}
