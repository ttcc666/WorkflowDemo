# 架构盘点修复实施与验收记录

原始盘点基线：965e6cd5ad4ab9e5a3cf7078e6601846e43d4435。历史诊断保留在 architecture-product-inventory-2026-11-05.md。本次按 A01～A05 分阶段提交；完成状态须包括正式测试及最终 SHA CI。

| 编号 | 当前状态 | 关键证据 |
| --- | --- | --- |
| A01 | 代码及本地正式回归通过，待最终 CI | Core 27 / 0 / 0；SQLite Database 18 / 0 / 0 |
| A02 | 代码及本地正式回归通过，待最终 CI | Core 32 / 0 / 0；HTTP 8 / 0 / 0 |
| A03 | 代码及本地正式回归通过，待最终 CI | Core 19 / 0 / 0；HTTP 12 / 0 / 0；SQLite Database 16 / 0 / 0 |
| A04 | 文档与代码签名已核对，独立消费者通过，待最终 CI | SDK、HTTP/OpenAPI、Outbox 和 EmbeddedHost |
| A05 | 公共 API 收敛及本地门禁通过，待最终 CI | `ApprovalWorker`、`ApprovalOutboxSignal` 改为 internal |

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

两条均退出 0，分别 Passed 27 / Failed 0 / Skipped 0 与 Passed 18 / Failed 0 / Skipped 0。Database 本地实际 Provider 为 SQLite；服务数据库的新回归尚待最终提交对应 CI，不能标为已通过。

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

最终发布门禁仍需当前分支 SHA 的 GitHub Actions Run。`3526c07d0c5606ca1316a13e6f7e7209c555d4b5` 当前无远程 Run。PostgreSQL、SQL Server 和 MySQL 的新回归尚未在本轮服务数据库上运行，9 项 Model Drift 只记录模型验证。
