using Approval.MultiTenantHost;

var migrate = args.Contains("--migrate", StringComparer.Ordinal);
await using var app = TenantApplication.Build(args.Where(a => a != "--migrate").ToArray());
if (migrate)
{
    await TenantDatabase.MigrateAsync(app.Services);
    return;
}
await app.RunAsync();
