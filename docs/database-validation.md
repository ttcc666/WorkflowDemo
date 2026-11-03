# 数据库验证与接入边界

日期：2026-11-02（Asia/Shanghai）。数据库基建现覆盖租户、权限、事务/幂等、治理、生命周期及真实多租户参考宿主。租户接入见 [多租户指南](multitenancy.md)，最终最低验收矩阵与未演练边界见[生产能力验收](production-acceptance.md)。

## 可重复运行

在仓库根目录运行，要求 .NET SDK（global.json）、PowerShell 7 和 Linux x64 Docker Engine。SQL Server 容器采用 Developer edition，仅用于测试；脚本通过 ACCEPT_EULA=Y 启动。

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
```

脚本读取 `eng/database-images.json` 的不可变镜像摘要；仅在本机缺失时拉取。每次创建独立的随机命名容器、随机口令和 `approval_test_` 数据库，端口只绑定 loopback。不挂载宿主数据目录，不读取 appsettings 或业务连接配置。清理时核对容器的本次运行标签，只删除本次容器及其匿名卷。成功和失败均保留报告。

已有专用测试库可以通过 `APPROVAL_TEST_CONNECTION` 环境变量及 `-UseExistingDatabase` 显式选择；库名必须以 `approval_test_` 开头。**迁移往返用例会删除并重建该库的审批表，已有数据不可保留。** 框架不会删除此类外部数据库。直接调用测试项目同样执行库名保护，且不接受 SQLite 外部文件。

普通 solution 测试默认使用独立临时 SQLite 文件，不要求 Docker。显式选择三库后，缺少连接、数据库不可用、迁移失败、零测试或任一跳过/失败均导致非零退出，不能用 SQLite 结果代替三库验收。

`-SkipRestore` / `-SkipBuild` 仅用于已还原/构建的本地循环。CI 使用完整入口，按锁文件还原并构建。

## Provider、迁移与依赖

Core 仍然仅依赖 Relational，具体驱动只加在 `tests/Approval.Database.Tests`。沿用 EF Core 10.0.14：

| Provider | NuGet 版本 | 说明 |
|---|---|---|
| MySql.EntityFrameworkCore | 10.0.12 | TLS + utf8mb4 连接；utf8mb4 服务端 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.14 | 关闭 MARS，测试自签证书仅用于临时容器 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL 17 |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.14 | 保留快速回归 |

锁文件固定传递依赖；显式引用 System.Security.Cryptography.Pkcs 10.0.14，避免 SQL Server 依赖链拉入已报告漏洞的 9.0.11。测试项目不可打包。

每个 Provider 有独立 DbContext、Migration 和 ModelSnapshot，均调用真实 `ConfigureApproval(this)`，涵盖九类当前实体。测试链不替代证照示例的既有迁移，也不直接用于宿主升级。

新增迁移（以 PostgreSQL 为例）：

```powershell
dotnet tool restore
dotnet ef migrations add 'DescribeChange' --project 'tests/Approval.Database.Tests' --context 'PostgreSqlContractContext' --output-dir 'Migrations/PostgreSql'
```

其他 Provider 分别使用 MySqlContractContext、SqlServerContractContext、SqliteContractContext。实际宿主维护自己的迁移链；旧数据租户映射和升级预检已在阶段 1 实现，见 `docs/multitenancy.md`。MySQL DDL 隐式提交，不能依靠事务回滚 schema 升级。

## 用例与产物

| Requirement | Evidence |
|---|---|
| 迁移回退、重新应用、幂等升级、模型漂移 | `Migrations_RollBackAndReapply_WithoutModelDrift` |
| Unicode、长 JSON、GUID、bool、UTC Kind、nullable、enum 往返 | `Model_RoundTripsUnicodeLongJsonUtcNullableAndEnums` |
| 流程键/版本唯一约束、获胜记录保持 | `UniqueDefinitionVersion_RejectsDuplicateWithoutLosingWinner` |
| 两个 DbContext 的陈旧更新被拒绝 | `ConcurrencyStamp_RejectsStaleWriter` |
| 实例与 Outbox 已保存后一起回滚 | `Transaction_RollsBackApprovalAndOutboxTogether` |
| 内层保存点回滚、外层提交 | `Savepoint_RollsBackInnerWriteAndCommitsOuterWrite` |
| Include、分页/count、Contains、ExecuteUpdate/Delete | `Queries_TranslateIncludePagingContainsAndBulkUpdates` |
| 同键双租户、模型复用、批量归属保护及复合约束 | `TenantIsolation_ModelReuseForeignIdsBulkWritesAndConstraints` |
| 拒绝无映射升级、回填重入、保留 ID、旧待办继续审批 | `LegacyUpgrade_RequiresMappingPreservesIdsAndContinuesPendingApproval` |

阶段 3 在同一套真实 Provider 契约中增加：

| Requirement | Evidence |
|---|---|
| 20 个同键请求跨两个独立宿主并发，只提交一次业务/流程/任务/Outbox | `AtomicCommands_TwentyConcurrentIdenticalRequestsAcrossTwoHosts_HaveOneCommittedResult` |
| 异键争抢业务、同键改内容冲突 | `AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent` |
| 租户隔离、撤权后禁止重放 | `AtomicCommands_TenantIsolationAndReplayAuthorizationRemainMandatory` |
| 原样业务键不受大小写/尾空格排序规则折叠 | `AtomicCommands_BusinessIdentityPreservesCaseAndTrailingWhitespaceAcrossProviders` |
| 提交前故障重试采用新上下文，提交后故障读取原结果 | `AtomicCommands_CommitFaultIsVerified_AndRetriesUseFreshContexts`（两个参数用例） |
| 核验不可用返回待确认，原键恢复结果 | `AtomicCommands_UnavailableCommitVerification_ReturnsUnknownAndOriginalKeyRecovers` |
| 外层回滚撤销宿主与两次引擎调用，原键可重试 | `AtomicCommands_OuterRollbackUndoesHostAndMultipleEngineCalls_AndKeyCanRetry` |
| 业务回调失败一起回滚 | `AtomicCommands_CallbackFailureRollsBackAndSameKeyCanRetry` |
| 自动重试下异步动作提前保存仍回滚，重试恢复 | `AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` |
| 租约替换后旧 worker 不能提交本地副作用 | `AtomicOutbox_ReplacedLeaseRejectsLateWorkerAndRollsBackItsWrites` |

阶段 4 增加 `Governance_ConcurrentDraftsAndPublishCounter_DoNotOverwriteOrDuplicateVersions`、`Governance_DisabledFlowKeepsOriginalInstanceAndTenantDraftsIndependent` 和 `Governance_OuterRollbackRemovesDraftPublicationAuditAndOperation`，覆盖双宿主草稿/发布竞争、停用后原实例继续、跨租户草稿及治理回滚。

阶段 5 再增加五项 Lifecycle 契约：双 worker 与瞬时批次重试、提交失败游标回滚、Pending Outbox/保留保护和跨租户原键、命令 tombstone 持久化、归档附件到期后的删除任务及存储失败重试。

阶段 6 增加参考宿主空库迁移重跑/回退、双租户完整审批与当前权限变化、20 个 HTTP 请求跨宿主并发、数据库后台租户故障隔离，以及同步动作全回滚和逐实例改派结果。

`output/database-tests/<Provider>/<runId>/` 保留 TRX、Provider/Server 版本和默认隔离级别、迁移 SQL、manifest 与 SHA256。锁文件摘要将运行关联到依赖组合。报告不包含连接字符串或口令。脚本解析真实 TRX，当前要求至少 33 项且全部通过。

CI 增加 Ubuntu 三库矩阵，保留原 Windows 完整验证。远端 CI 尚未触发，本机运行结果见 `plans/2026-11-02-multitenant-production-hardening-progress.md`。

## 当前语义与后续边界

- 默认隔离级别沿用数据库：MySQL InnoDB REPEATABLE READ，SQL Server READ COMMITTED（报告数字 2），PostgreSQL READ COMMITTED；SQLite 为串行化写入。原子命令测试显式启用可注入故障的 EF execution strategy，验证整段重试和新上下文；不强制宿主启用统一重试策略或隔离级别。
- 时间使用 UTC，当前验证到秒级以及 Kind 恢复；不同数据库的小数秒精度未声明等价。
- 标识、业务主键和流程键保留原值；不自动 trim 或转大写。阶段 3 通过哈希占用统一新业务身份的精确语义；历史流程键等自然键仍受数据库默认排序规则影响，不能声称全部自然键语义已经统一。
- 三库验证模型新建、迁移往返、旧库租户升级及上述原子命令矩阵。提交故障通过真实事务拦截器在 commit 前后注入，并使独立核验查询失败；维护恢复使用持久游标和受控提交失败。它们不等同生产网络分区/数据库进程崩溃、真实死锁拓扑或备份附件还原演练；未运行项继续列入最终支持边界。
- 迁移 SQL 中的事务包装不能证明 MySQL DDL 原子性；Outbox 数据回滚也不能证明外部副作用恰好一次。

迁移划分依据：[EF Core 多 Provider 迁移](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)。MySQL 字符集配置依据：[官方 EF Core 字符集文档](https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core-charset.html)。

补充的[真实服务演练](live-service-rehearsals.md)已在三库完成原生备份/新库完整恢复、附件/所有持久属性核对、实际 TCP 断开及 COMMIT 下行确认丢失。普通数据库入口的 MySQL/TLS 与 PostgreSQL 探针现使用最终 TCP 服务，避免临时初始化 socket 过早就绪。生产拓扑、密钥/权限和 HA/RPO/RTO 不由该隔离结果推定。
