# 架构盘点修复实施与验收记录

原始盘点基线：965e6cd5ad4ab9e5a3cf7078e6601846e43d4435。历史诊断保留在 architecture-product-inventory-2026-11-05.md。本次按 A01～A05 分阶段提交；完成状态须包括正式测试及最终 SHA CI。

| 编号 | 当前状态 | 关键证据 |
| --- | --- | --- |
| A01 | 四个真实 Provider 回归通过 | SQLite、SQL Server、PostgreSQL、MySQL 各 81 / 0 / 0 |
| A02 | 正式回归和完整 CI 通过 | Core 32 / 0 / 0；HTTP 8 / 0 / 0 |
| A03 | 补充回滚修复及完整 CI 通过 | 补充本地定点 Core 27 / 0 / 0；HTTP 4 / 0 / 0 |
| A04 | 文档核对、独立消费者和完整 CI 通过 | SDK、HTTP/OpenAPI、Outbox 和 EmbeddedHost |
| A05 | 公共 API 收敛和完整 CI 通过 | `ApprovalWorker`、`ApprovalOutboxSignal` 改为 internal |

## A01

生产修改：ApprovalDatabaseErrors、ApprovalCommandExecutor、ApprovalDefinitionService.Governance、ApprovalCarbonCopyService。使用现有 EF 模型中的表、索引或主键及字段身份，结合 Provider 错误码和异常 Entries 判定；PostgreSQL 使用结构化表/约束/Schema，SQL Server 与 MySQL 提取驱动返回的标識，SQLite 比较确切表/列集合。身份不明确时保留原异常。低级 Unique 方法仅保留作诊断，业务翻译不再使用它。

审批 Operation LookupHash、BusinessSlot LookupHash/InstanceId、定义版本/治理键/审计序号和逐人已读既有唯一约束保持原业务冲突语义。宿主唯一、外键及普通 SQL 异常原样传播。没有模型、Migration、公开 API 或 HTTP 契约变化。

正式回归：DatabaseErrorBoundaryTests、DatabaseErrorBoundaryDatabaseTests；扩展已有 ApprovalLogConflictDatabaseTests。TestRecord 仅在内存测试模型增加可空自引用外键用于真实宿主 FK 失败，不修改产品模型。

| Requirement | Evidence |
| --- | --- |
| 完整委托保留宿主唯一/FK/SQL 原异常并全量回滚 | Executor_HostDatabaseErrorPreservesOriginalAndRollsBackWholeCommand（3 个分区） |
| 治理保存不误吞宿主唯一错误 | Governance_HostUniqueFailureRemainsDatabaseErrorAndDoesNotSaveGovernance（draft/publish） |
| 真实 Operation 预约碰撞恢复赢家，副作用仅一次 | Executor_SynchronizedOperationReservationCollisionReplaysWinnerWithoutDuplicateEffects（读取屏障及实际唯一失败计数） |
| 真实 Provider 宿主唯一/SQL 保留及回滚 | Executor_RealProviderHostErrorPreservesOriginalAndRollsBack |
| 审批自身唯一约束精确归属 | RealProviderUniqueConstraints_ClassifyOnlyOwnedBusinessIndexes（7 个约束分区） |
| FK/SQL 不误判，日志序号和占用识别保持 | RealProviderConstraints_IdentifyOnlyApprovalLogSequence、RealProviderSqlError_IsNotApprovalLogConflict |
| 原有业务占用、并发同键和治理竞争保持 | AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent、AtomicCommands_TwentyConcurrentIdenticalRequestsAcrossTwoHosts_HaveOneCommittedResult、Governance_ConcurrentDraftsAndPublishCounter_DoNotOverwriteOrDuplicateVersions |

修复前 Core 新用例退出 1：Passed 2 / Failed 3 / Skipped 0，真实复现宿主唯一、治理草稿和治理发布误分类。修复后执行：

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~DatabaseErrorBoundaryTests|FullyQualifiedName~ApprovalLogConflictTests|FullyQualifiedName~DefinitionGovernanceTests|FullyQualifiedName~StrictCommandBoundaryTests' --verbosity 'minimal'
dotnet test 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~RealProviderUniqueConstraints|FullyQualifiedName~Executor_RealProviderHostError|FullyQualifiedName~Executor_SynchronizedOperation|FullyQualifiedName~RealProviderConstraints|FullyQualifiedName~RealProviderSqlError|FullyQualifiedName~AtomicCommands_Twenty|FullyQualifiedName~AtomicCommands_DifferentKeys|FullyQualifiedName~Governance_ConcurrentDrafts' --verbosity 'minimal'
```

两条均退出 0，分别 Passed 27 / Failed 0 / Skipped 0 与 Passed 18 / Failed 0 / Skipped 0。此处 Database 本地实际 Provider 为 SQLite，后续四种 Provider 的证据见补充验收。

## A02

`ApprovalEngine` 的人员预检与正式启动共用定义选择。默认选择活动发布版本，显式版本优先于工作单元固定版本，两类请求均受停用规则约束。正式启动继续固定所选版本，已提交命令按原键重放绑定结果。预检使用宿主提供的表单变量和业务上下文，保持只读。

| Requirement | Evidence |
| --- | --- |
| 活动版本及显式历史/当前版本与实际分配一致 | `StartDefinitionSelectionTests.Preflight_AndStartSelectSameActiveOrExplicitVersion` |
| 停用拒绝显式及隐式预检和启动，且无审批侧写入 | `StartDefinitionSelectionTests.Preflight_AndStartRejectDisabledFlowWithSameError` |
| 原键重放历史绑定，且无重复实例、Operation 或提交日志 | `StartDefinitionSelectionTests.Start_OriginalKeyReplaysBoundVersionAfterNewPublicationAndDisable`、`OperationTests.KeyedCoreStart_ReplaysOriginalVersionAndRejectsDifferentContent` |
| Resolver 保留准备变量、业务上下文和版本，预检不调用生命周期准备 | `StartDefinitionSelectionTests.Preflight_PreservesPreparedVariablesAndContextWithoutRunningLifecycle` |

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~StartDefinitionSelectionTests|FullyQualifiedName~DefinitionBindingTests|FullyQualifiedName~DefinitionGovernanceTests|FullyQualifiedName~OperationTests' --verbosity 'minimal'
dotnet test 'tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~DefinitionGovernanceEndpointTests' --verbosity 'minimal'
```

两条均退出 0，分别 Passed 32 / Failed 0 / Skipped 0 与 Passed 8 / Failed 0 / Skipped 0。HTTP 验证覆盖现有发布、切换和停用治理入口，人员预检是 Core 接入 API。

## A03

`ApprovalDefinitionService.Warnings` 在现有 `Extension` 中调用扩展并复制警告结果。`ApprovalCommandExecutor` 与工作单元复用调用作用域的重入检查，防止回调借助执行器创建写入作用域。`UseApproval` 继续负责同一上下文的保存、事务提交和审批表批量写入保护。

| Requirement | Evidence |
| --- | --- |
| 允许宿主读取并返回警告 | `WarningsBoundaryTests.Warnings_CanReadHostDataAndReturnWarningsWithoutWriting` |
| 拒绝同步/异步保存并核验最终库状态 | `WarningsBoundaryTests.Warnings_RejectHostSaveChangesAndPreserveDatabase` |
| 拒绝 Commit，外部事务仍可回滚 | `WarningsBoundaryTests.Warnings_RejectCommitAndLeaveOuterTransactionAvailableForRollback` |
| 拒绝通过带键执行器重入，委托未执行且无持久化副作用 | `WarningsBoundaryTests.Warnings_RejectKeyedCommandReentryBeforeInvokingDelegate` |
| 审批定义批量更新继续被拦截 | `WarningsBoundaryTests.Warnings_RejectBulkUpdateOfProtectedDefinition` |
| 实际校验 HTTP 入口正常返回警告并拒绝宿主保存 | `DefinitionGovernanceEndpointTests.ValidateEndpoint_WarningsCanReadButCannotPersistHostRecords` |

修复前 HTTP 用例收到 200 而非 500，Core 复现同步/异步保存、Commit 和带键执行器重入均未被拒绝。修复后执行：

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~WarningsBoundaryTests|FullyQualifiedName~AsyncValidationTests|FullyQualifiedName~OutboxTests|FullyQualifiedName~StrictCommandBoundaryTests' --verbosity 'minimal'
dotnet test 'tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~DefinitionGovernanceEndpointTests|FullyQualifiedName~OutboxRetryEndpointTests' --verbosity 'minimal'
dotnet test 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj' --configuration 'Release' --no-build --filter 'FullyQualifiedName~AtomicCommands_|FullyQualifiedName~AtomicOutbox_|FullyQualifiedName~Governance_' --verbosity 'minimal'
```

三条均退出 0，分别 Passed 19、12、16，Failed 0 / Skipped 0。Database 测试实际使用 SQLite。

## A04

- [事务与幂等](transactions-and-idempotency.md) 使用四参数 `client.api.reassignUser`。已核对 SDK `commandHeaders`、`AdminEndpoints` 的 `OperationId` 提取、`ReassignRequest` 和 OpenAPI 必填 `Idempotency-Key`。
- [事件与 Outbox](events.md) 按 `ApprovalEvent`、`OutboxJson` 和 `ApprovalOutboxService` 记录现行格式、稳定 ID、投递与人工重试及宿主去重责任。
- [宿主接入](embedded-host-integration.md) 集中给出注册与带键完整业务委托示例，对照 `ApprovalBuilder` 和可运行的 [EmbeddedHost](../eng/consumers/EmbeddedHost/Program.cs)。该阶段仅修改文档。

## A05

| 对象 | 决定 | 消费者与依据 |
| --- | --- | --- |
| `ApprovalWorker` | 改为 internal | DI 注册唯一 `IHostedService`。手动调度已有 `ApprovalOutboxDispatcher` 和 `ApprovalTimeoutScanner`，组合循环只有 Core 和友元测试使用。 |
| `ApprovalOutboxSignal` | 改为 internal | Core 在提交后唤醒投递器并协调批次，外部无需操作信号。已有 Core 与 HTTP 友元测试覆盖唤醒时序。 |
| `ApprovalOutboxDispatcher`、`ApprovalTimeoutScanner` | 保留 | 单次运行是关闭定时后台服务后的宿主调度入口。独立 NuGet 消费者调用 `DispatchOnceAsync`。 |
| `ApprovalBackgroundRunner`、`ApprovalBackgroundState` | 保留现有可见性 | 两个单次运行服务的构造器使用 Runner，数据库诊断测试用 State 构造诊断服务，ReferenceHost 场景直接验证 Runner 的系统作用域。同步隐藏需要更改公开构造器和真实测试消费者。宿主诊断扩展走 `IApprovalDiagnosticsContributor`。 |
| `ApprovalDbContextAccessor`、`DefaultApprovalAccessPolicy` | 保留 | Forms 服务与 ASP.NET Core readiness 使用 Accessor，Certificate 的访问策略从 `DefaultApprovalAccessPolicy` 派生，构造器需要 Accessor。宿主业务写入仍从命令委托解析自己的 DbContext。 |
| 查询、统计、消息、抄送、诊断及 Forms 服务构造器 | 保留 | 服务依赖既有宿主配置、投影、权限、用户目录、文件存储和时钟。继续使用现有 DI 组合，无新增包装。 |
| 启动、发布、治理审计、Outbox 查询的实体结果 | 保留 | 启动和定义返回值被 HTTP、Forms、Certificate、ReferenceHost 和独立 NuGet 消费者读取，宿主诊断和 HTTP 适配使用现有结果。审批写保护继续约束实体写入，宿主扩展回调使用 Context/Snapshot。 |

审查范围包括 Core、AspNetCore、Forms、Forms.AspNetCore 的公开基线，以及项目示例和独立 NuGet/npm 消费者。A05 的有意契约差异是 Core 中这两个内部实现类型的公开记录，HTTP/OpenAPI、JSON、TypeScript 与数据库模型保持原契约。

## 本地完整门禁与远程验收

分支 `codex/architecture-audit-fixes`，实际验证的代码 SHA `3526c07d0c5606ca1316a13e6f7e7209c555d4b5`。A01 `0695b10`、A02 `3ac5662`、A03 `2c0b65c`、A04 `e7bf865`、A05 `3526c07`。其后的验收记录提交仅包含 Markdown。

从仓库根目录运行以下门禁，全部退出 0。

| 门禁 | 本地证据 |
| --- | --- |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'` | Release 构建 0 警告 / 0 错误。Core 607、HTTP 119、SQLite Database 78、Certificate 59，合计 Passed 863 / Failed 0 / Skipped 0。Model Drift 9 项通过，ReferenceHost 冒烟 27 项通过。 |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'` | 四个程序集、OpenAPI、JSON Schema、TypeScript 和示例契约均匹配基线。之前运行的 `pnpm --dir 'web' 'contracts:update'` 退出 0，审阅生成差异后仅 `Approval.Core.api.txt` 的两个类型记录变更。 |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend'` | 依赖边界和类型检查通过，边界测试 12 项、UI 242 项、Demo 6 项通过，失败及跳过均为 0，库和 Demo 构建通过。 |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Packages'` | 独立 NuGet 与 npm 消费者通过，打包版 ReferenceHost 冒烟 27 项，EmbeddedHost 18 项，浏览器 Passed 7 / Failed 0 / Skipped 0。 |

上一轮推送提交 `8e4fb8753d061e7a934fb6f6197a3628ea664752` 的 [CI #42008090488](https://github.com/ttcc666/WorkflowDemo/actions/runs/42008090488) 全部通过。PostgreSQL、SQL Server 和 MySQL 各运行 78 项真实数据库测试。下文记录补充 A03 和 A01 后的新证据。

## 补充验收 A03 和 A01

基线 `8e4fb8753d061e7a934fb6f6197a3628ea664752`。A03 `3f7e231` 单独修复，A01 代码及测试提交 `b6e4febe61c69739f82a05fd417953944078140f`。

### A03 捕获异常后的事务回滚

先执行新增 Warnings 回归，得到 7 项通过、2 项失败。同步和异步两种场景均观察到 Warnings 捕获保存拒绝后继续返回，外层命令将正常业务记录和扩展插入的记录一起提交。

`ApprovalPersistence` 在拦截保存、事务开启/提交及只读扩展的 EF 直接写入时将当前上下文标为失败。`ApprovalUnitOfWork` 重入检查也锁定失败状态，`ApprovalCommandExecutor` 在宿主委托返回后检查该状态，随后由现有工作单元回滚。作用域退出时保留失败状态直到上下文释放或池化租期变更。

| 用例 | 证据 |
| --- | --- |
| `Warnings_CaughtSaveRejectionRollsBackEntireKeyedCommand` | 两种 SaveChanges 都被拒绝，扩展捕获异常后外层委托走到返回。独立上下文确认宿主记录、实例、任务、业务占用、Operation、日志和 Outbox 均回滚，发布定义仍存在。 |
| `Warnings_RejectHostSaveChangesAndPreserveDatabase` | 验证同步和异步拒绝异常的原有传播路径。 |
| `Warnings_ReadOnlyCallbackAllowsKeyedHostAndApprovalWrites` | 只读回调正常返回时，带键委托保存宿主数据并提交审批、Operation、审计和 Outbox。 |
| `Warnings_RejectOpeningTransactionAndInvalidateCallerScope`、`Warnings_RejectCommitAndLeaveOuterTransactionAvailableForRollback` | 拒绝开启事务和提交，验证同一作用域后续保存或提交仍被拒绝且可回滚。 |
| `Warnings_RejectDirectEfWritesToHostTable`、`Warnings_RejectKeyedCommandReentryBeforeInvokingDelegate` | 拒绝批量更新、原生命令及执行器重入。 |

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~WarningsBoundaryTests|FullyQualifiedName~StrictCommandBoundaryTests|FullyQualifiedName~OutboxTests|FullyQualifiedName~HostDbContextIntegrationTests' --verbosity 'minimal'
dotnet test 'tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~ValidateEndpoint_WarningsCanReadButCannotPersistHostRecords|FullyQualifiedName~OutboxRetryEndpointTests' --verbosity 'minimal'
```

本地分别通过 27 项、4 项，失败和跳过均为 0，两条命令均退出 0。

### A01 真实 Provider 回归

MySQL 错误 1062 末尾的索引标识使用原生索引名，主键为 `PRIMARY`。识别时仍验证 EF 实体归属及错误中的表名或同类型 Entries，取末尾索引标识以排除重复值干扰。其余 Provider 保留现有约束身份判断。

`RealProviderUniqueConstraints_ClassifyOnlyOwnedBusinessIndexes` 新增抄送已读的业务唯一索引和主键冲突。它还验证 Operation、业务占用、流程版本、流程治理与审计约束，以及不属于已识别业务冲突的日志主键。`Executor_RealProviderHostErrorPreservesOriginalAndRollsBack` 验证宿主主键、外键及 SQL 错误的原生异常类型和错误码，保留异常实例并回滚同事务的审批和宿主写入。外键仅加入测试宿主夹具。

```powershell
dotnet test 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~RealProviderUniqueConstraints|FullyQualifiedName~Executor_RealProviderHostError|FullyQualifiedName~Executor_SynchronizedOperation|FullyQualifiedName~RealProviderConstraints|FullyQualifiedName~RealProviderSqlError' --verbosity 'minimal'
```

本地 SQLite 18 项通过、0 失败、0 跳过，退出 0。

| Provider | 实测 | 原生错误证据 |
| --- | --- | --- |
| SQLite | 本地定点 18 / 0 / 0，CI 全集 81 / 0 / 0 | `SqliteException` 扩展码 1555、787、1 分别对应宿主主键、外键和缺失列，审批唯一约束由表与列身份匹配。 |
| SQL Server | 真实数据库 81 / 0 / 0 | `SqlException` 2601 和 2627 包含审批索引或主键及表名，宿主主键/外键/缺失列为 2627、547、207。 |
| PostgreSQL | 真实数据库 81 / 0 / 0 | `PostgresException` 的 23505、23503、42703 分别为唯一、外键、缺失列，约束冲突保留结构化约束名和表名。 |
| MySQL | 真实数据库 81 / 0 / 0 | `MySqlException` 1062 区分 `ApprovalCarbonCopyReads.PRIMARY` 和宿主主键，外键/缺失列为 1452、1054。 |

三个服务数据库的 TRX 和原生异常证据均来自代码 SHA `b6e4febe61c69739f82a05fd417953944078140f`。[CI #42030923238](https://github.com/ttcc666/WorkflowDemo/actions/runs/42030923238)

### 完整 CI

代码 SHA `b6e4febe61c69739f82a05fd417953944078140f` 的 `approval-pilot` 全部四个 Job 成功。Windows 的 `eng/verify.ps1` 退出 0，后端 872 项通过、0 失败、0 跳过（Core 613、HTTP 119、SQLite Database 81、Certificate 59）。9 项模型漂移检查通过，ReferenceHost 冒烟 27 项、前端门禁、契约、独立 NuGet/npm 消费者和浏览器 7 项均通过。代码差异集中在 Core 持久化及测试夹具，契约检查与模型漂移检查分别确认公开契约和产品 Migration 无差异。[CI #42030923238](https://github.com/ttcc666/WorkflowDemo/actions/runs/42030923238)
