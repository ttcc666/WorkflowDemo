using Approval.MultiTenant.Api;

var migrate = args.Contains("--migrate", StringComparer.Ordinal);
await using var app = ReferenceHost.Build(args.Where(a => a != "--migrate").ToArray());
if (migrate)
{
    await ReferenceDatabase.MigrateAsync(app.Services);
    return;
}
await app.RunAsync();
