using Approval.ReferenceHost;

var migrate = args.Contains("--migrate", StringComparer.Ordinal);
await using var app = ReferenceApplication.Build(args.Where(a => a != "--migrate").ToArray());
if (migrate)
{
    await ReferenceDatabase.MigrateAsync(app.Services);
    return;
}
await app.RunAsync();
