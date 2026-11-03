using Certificate.Api;
using Certificate.Api.Certificates;
using Certificate.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var operation = builder.Configuration["Certificate:DatabaseOperation"];
if (operation == "backup")
{
    var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
    connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
    var destination = builder.Configuration["Certificate:Destination"] ?? throw new InvalidOperationException("请明确指定 Certificate:Destination 新文件路径");
    await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ToString()).Options);
    var database = new CertificateDatabase(context);
    var result = await database.BackupAsync(destination);
    Console.WriteLine(JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
    return;
}
if (operation is not null and not "publish-flows") throw new InvalidOperationException("不支持的数据库维护操作");
builder.AddCertificateApplication();
await using var app = builder.Build();
if (operation == "publish-flows")
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CertificateDatabase>().InitializeAsync();
    var count = await CertificateApplication.PublishFlowsAsync(app, replaceExisting: true);
    Console.WriteLine(JsonSerializer.Serialize(new { published = count }));
    return;
}
await app.UseCertificateApplicationAsync();
app.Run();
