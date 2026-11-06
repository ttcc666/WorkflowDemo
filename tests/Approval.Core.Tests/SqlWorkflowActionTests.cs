using System.Data;
using System.Text.Json.Nodes;
using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Approval.Core.Tests;

public sealed class SqlWorkflowActionTests
{
    private const string Definition = """
        {"schemaVersion":1,"startNodeId":"start","nodes":{
          "start":{"type":"Start","next":"query"},
          "query":{"type":"Action","next":"end","actions":[
            {"id":"inventory","type":"Sql","outputKey":"inventory",
             "config":{"dataSource":"warehouse","queryId":"inventory.available","parameters":{"code":"{{form.productCode}}"}}}
          ]},"end":{"type":"End"}
        }}
        """;

    [Theory]
    [InlineData("P001", 1)]
    [InlineData("' OR 1=1 --", 0)]
    public async Task Registered_query_binds_parameters_and_produces_bounded_rows(string code, int count)
    {
        using var database = new InventoryDatabase();
        await using var host = CreateHost(database);
        await host.PublishAsync("stock", Definition);
        var instance = await host.StartAsync("stock", "PO-1", "alice", new { productCode = code });
        var output = ExecutionState.Load(await host.GetInstanceAsync(instance.Id)).ActionOutputs["inventory"].Value;
        Assert.Equal(count, output.GetProperty("count").GetInt32());
        Assert.Equal(count, output.GetProperty("rows").GetArrayLength());
        if (count == 1) Assert.Equal(30, output.GetProperty("rows")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Sqlite_reads_multiple_computed_columns_without_using_blob_metadata()
    {
        using var database = new InventoryDatabase();
        await using var host = CreateHost(database, "SELECT @code AS code, 30 AS quantity");
        await host.PublishAsync("stock", Definition);
        var instance = await host.StartAsync("stock", "PO-computed", "alice", new { productCode = "P001" });
        var row = ExecutionState.Load(await host.GetInstanceAsync(instance.Id)).ActionOutputs["inventory"].Value.GetProperty("rows")[0];
        Assert.Equal("P001", row.GetProperty("code").GetString());
        Assert.Equal(30, row.GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData("connectionString", "DataSource=arbitrary")]
    [InlineData("dataSource", "unregistered")]
    [InlineData("queryId", "DELETE FROM inventory")]
    public async Task Publish_rejects_injected_connections_unknown_queries_and_inline_sql(string field, string value)
    {
        using var database = new InventoryDatabase();
        await using var host = CreateHost(database);
        var flow = JsonNode.Parse(Definition)!;
        flow["nodes"]!["query"]!["actions"]![0]!["config"]![field] = value;
        await Assert.ThrowsAsync<ApprovalException>(() => host.PublishAsync("invalid", flow.ToJsonString()));
    }

    [Theory]
    [InlineData("SELECT quantity FROM inventory WHERE @code IS NOT NULL", 1)]
    [InlineData("DELETE FROM inventory WHERE code = @code RETURNING quantity", 10)]
    public async Task Readonly_data_source_and_row_budget_prevent_writes_and_partial_workflow(string sql, int maxRows)
    {
        using var database = new InventoryDatabase();
        await using var host = CreateHost(database, sql, maxRows);
        await host.PublishAsync("stock", Definition);
        await Assert.ThrowsAsync<ApprovalException>(() => host.StartAsync("stock", "PO-2", "alice", new { productCode = "P001" }));
        Assert.Empty(await host.OutboxAsync());
        using var connection = database.ReadOnly();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM inventory";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    private static TestHost CreateHost(InventoryDatabase database,
        string sql = "SELECT code, quantity FROM inventory WHERE code = @code", int maxRows = 10) =>
        new(build: b => b.AddSqlActionQuery(new("warehouse", "inventory.available", sql,
            new Dictionary<string, DbType> { ["code"] = DbType.String }, maxRows), _ => database.ReadOnly()));

    private sealed class InventoryDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"workflow-action-{Guid.NewGuid():N}.db");

        internal InventoryDatabase()
        {
            using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE inventory (code TEXT NOT NULL, quantity INTEGER NOT NULL);
                INSERT INTO inventory VALUES ('P001', 30), ('P002', 5);
                """;
            command.ExecuteNonQuery();
        }

        internal SqliteConnection ReadOnly() => new($"Data Source={_path};Mode=ReadOnly;Pooling=False");

        public void Dispose() => File.Delete(_path);
    }
}
