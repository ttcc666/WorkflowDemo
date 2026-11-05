# 架构盘点修复实施与验收记录

原始盘点基线：965e6cd5ad4ab9e5a3cf7078e6601846e43d4435。历史诊断保留在 architecture-product-inventory-2026-11-05.md。本次按 A01～A05 分阶段提交；完成状态须包括正式测试及最终 SHA CI。

| 编号 | 当前状态 | 关键证据 |
| --- | --- | --- |
| A01 | 代码及本地正式回归通过，待最终 CI | Core 27 / 0 / 0；SQLite Database 18 / 0 / 0 |
| A02 | 代码及本地正式回归通过，待最终 CI | Core 32 / 0 / 0；HTTP 8 / 0 / 0 |
| A03 | 待实施 | Warnings 只读保存、事务及重入边界 |
| A04 | 待实施 | 当前 SDK、事件协议及宿主接入 |
| A05 | 待审查 | 按宿主长期依赖判断，不追求 public 数量 |

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
