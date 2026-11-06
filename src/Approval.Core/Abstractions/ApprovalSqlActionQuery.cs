using System.Data;

namespace Approval.Core.Abstractions;

/// <summary>宿主注册的固定 SQL 查询。连接工厂须返回使用只读账号的独立连接。</summary>
/// <param name="DataSource">流程可选择的数据源标识。</param>
/// <param name="QueryId">数据源内的查询标识。</param>
/// <param name="CommandText">宿主维护的参数化只读查询。</param>
/// <param name="Parameters">参数名及 ADO.NET 类型。参数必须与查询对应。</param>
/// <param name="MaxRows">最多允许返回的行数。</param>
/// <param name="TimeoutSeconds">连接和查询的总时限。</param>
public sealed record ApprovalSqlActionQuery(string DataSource, string QueryId, string CommandText,
    IReadOnlyDictionary<string, DbType> Parameters, int MaxRows = 100, int TimeoutSeconds = 5);
