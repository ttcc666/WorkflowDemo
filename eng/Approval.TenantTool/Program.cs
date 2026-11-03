using System.Text.Json;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

var arguments = new Dictionary<string, string>();
var apply = false;
var businessSlots = false;
var allowParallelRunning = false;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--business-slots") { businessSlots = true; continue; }
    if (args[index] == "--apply") { apply = true; continue; }
    if (args[index] == "--allow-parallel-running") { allowParallelRunning = true; continue; }
    if (args[index] is not ("--provider" or "--mapping" or "--fixed-tenant" or "--output") || index + 1 >= args.Length)
        throw new ArgumentException("Usage: --provider MySql|SqlServer|PostgreSql|Sqlite (--mapping file.json | --fixed-tenant tenant) [--apply] [--output report.json]");
    arguments.Add(args[index], args[++index]);
}
if (!arguments.TryGetValue("--provider", out var provider)) throw new ArgumentException("必须显式选择 Provider");
var connection = Environment.GetEnvironmentVariable("APPROVAL_MAINTENANCE_CONNECTION") ?? throw new ArgumentException("请显式设置 APPROVAL_MAINTENANCE_CONNECTION，不读取业务配置");
var builder = new DbContextOptionsBuilder();
switch (provider)
{
    case "MySql": builder.UseMySQL(connection); break;
    case "SqlServer": builder.UseSqlServer(connection); break;
    case "PostgreSql": builder.UseNpgsql(connection); break;
    case "Sqlite": builder.UseSqlite(connection); break;
    default: throw new ArgumentException("不支持的 Provider");
}
if (businessSlots)
{
    var selected = arguments.GetValueOrDefault("--fixed-tenant") ?? throw new ArgumentException("占用回填必须显式指定 --fixed-tenant，并已完成 schema 迁移、停止写入");
    if (allowParallelRunning) throw new ArgumentException("并行运行模式不需要业务占用，不应运行此回填");
    await using var slotDb = new SlotMaintenanceContext(builder.Options, new Approval.Core.Abstractions.FixedApprovalTenantContext(selected));
    var slotReport = await ApprovalBusinessSlotBackfill.RunAsync(slotDb, apply);
    var slotOutput = Path.GetFullPath(arguments.GetValueOrDefault("--output") ?? $"output/business-slots/{Guid.NewGuid():N}.json");
    Directory.CreateDirectory(Path.GetDirectoryName(slotOutput)!);
    await File.WriteAllTextAsync(slotOutput, JsonSerializer.Serialize(slotReport, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Business slots: ready={slotReport.Ready}, missing={slotReport.Missing}, changed={slotReport.Changed}; report: {slotOutput}");
    return slotReport.Ready ? 0 : 1;
}
var mapping = arguments.TryGetValue("--mapping", out var mappingFile)
    ? JsonSerializer.Deserialize<List<ApprovalTenantMapping>>(await File.ReadAllTextAsync(mappingFile), JsonSerializerOptions.Web) ?? [] : [];
var tenant = arguments.GetValueOrDefault("--fixed-tenant");
if (mapping.Count == 0 && tenant is null) throw new ArgumentException("必须提供显式逐行映射或单企业归属");
await using var db = new DbContext(builder.Options);
var maintenance = new ApprovalTenantBackfill(db.Database.GetDbConnection(), provider, singleRunningInstancePerBusiness: !allowParallelRunning);
var report = apply ? await maintenance.ApplyAsync(mapping, tenant) : await maintenance.PreviewAsync(mapping, tenant);
var output = Path.GetFullPath(arguments.GetValueOrDefault("--output") ?? $"output/tenant-mapping/{Guid.NewGuid():N}.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Tenant mapping: ready={report.Ready}, changed={report.Changed}, problems={report.Problems.Count}, keyWarnings={report.PotentialKeyCollisions.Count}; report: {output}");
return report.Ready ? 0 : 1;

internal sealed class SlotMaintenanceContext(DbContextOptions options, Approval.Core.Abstractions.IApprovalTenantContext tenant) : ApprovalDbContext(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder builder) => builder.ConfigureApproval(this);
}
