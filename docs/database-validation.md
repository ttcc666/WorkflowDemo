# 数据库验证与迁移

当前框架使用宿主关系数据库。支持 EF Core SQLite、MySQL、SQL Server、PostgreSQL 的模型和迁移链；本次实际执行的 Provider 以完成报告中的日志为准，SQLite 通过不能代替真实三库证据。

## 本地门禁

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File 'eng/verify.ps1' -Scope 'Backend'
```

覆盖宿主构建/测试、证书及四 Provider 参考模型漂移、真实 Kestrel 单宿主冒烟。数据库契约另外包含四种 Provider 的空库初始化/迁移往返、事务/并发/命令恢复与并行竞争。

关键用例：Migrations_RollBackAndReapply_WithoutModelDrift、Initial_migration_creates_current_schema_and_repeated_initialization_preserves_rows、Parallel_ConcurrentBranchCompletionAndReplay_CreateOnlyOneDownstreamTask。旧回滚、提交确认、外层事务、保留/清理和永久防重用例继续执行。

## 真实三库

Docker 可用后分别运行：

```powershell
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File 'eng/verify-databases.ps1' -Provider $provider
    if ($LASTEXITCODE -ne 0) { throw ('数据库验证失败：' + $provider) }
}
```

脚本只创建有本次所有权标签的临时容器/approval_test 数据库，不连接用户业务库。已有测试库模式必须显式指定一次性库；迁移往返会清空该测试库，不能指定生产连接。SQLite 使用测试自己创建的临时文件。

九套上下文各保留一条 `InitialApproval`，不再保留开发期历史升级链、旧数据映射和回填工具。新库模型直接包含单宿主、分类索引、审批历史、治理与抄送阅读等当前结构；反复执行初始化不应更改已有业务行。真实三库验收以实测日志为准。
