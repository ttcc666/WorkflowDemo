using System.Data;
using System.Data.Common;
using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Approval.Database.Tests;

public sealed partial class DatabaseContractTests
{
    [Fact]
    public async Task WorkflowAction_BindsParametersAndCommitsJsonOutputAcrossProviders()
    {
        await using var source = fixture.Create();
        var connectionString = source.Database.GetDbConnection().ConnectionString;
        DbConnection Connect()
        {
            if (fixture.Provider == "Sqlite")
                return new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString)
                    { Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            return fixture.Provider switch
            {
                "PostgreSql" => new Npgsql.NpgsqlConnection(connectionString),
                "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                "MySql" => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
                _ => throw new InvalidOperationException("Unknown provider")
            };
        }

        await using var host = await AtomicHostAsync(new(), configureServices: services =>
            new ApprovalBuilder(services).AddSqlActionQuery(new("database", "probe", "SELECT @probe AS probe",
                new Dictionary<string, DbType> { ["probe"] = DbType.String }), _ => Connect()));
        const string flow = """
            {"schemaVersion":1,"startNodeId":"start","nodes":{
              "start":{"type":"Start","next":"query"},
              "query":{"type":"Action","next":"end","actions":[
                {"id":"probe","type":"Sql","outputKey":"result",
                 "config":{"dataSource":"database","queryId":"probe","parameters":{"probe":"{{form.probe}}"}}}
              ]},"end":{"type":"End"}
            }}
            """;
        await AtomicRun(host, "admin", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("publish.probe", "workflow-action", "publish", new { flow }, ApprovalPermissions.DefinitionPublish),
            (fresh, ct) => fresh.GetRequiredService<ApprovalDefinitionService>().PublishAsync("probe", "Probe", flow, ct)));
        const string input = "采购 ' OR 1=1 --";
        var instanceId = await AtomicRun(host, "alice", sp => sp.GetRequiredService<IApprovalCommandExecutor>().ExecuteAsync(
            new("start.probe", "workflow-action", "start", new { input }, ApprovalPermissions.InstanceStart),
            async (fresh, ct) => (await fresh.GetRequiredService<ApprovalEngine>()
                .StartAsync(new("probe", AtomicBusiness.Type, "probe", "alice") { FormData = new { probe = input } }, ct)).Id));
        await AtomicRun(host, "alice", async sp =>
        {
            var instance = await sp.GetRequiredService<AtomicContractContext>().Set<ApprovalInstance>().SingleAsync(i => i.Id == instanceId);
            Assert.Equal(InstanceStatus.Approved, instance.Status);
            Assert.Equal(input, ExecutionState.Load(instance).ActionOutputs["result"].Value.GetProperty("rows")[0].GetProperty("probe").GetString());
            return true;
        });
    }
}
