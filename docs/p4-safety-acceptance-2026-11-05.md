# P4 安全验收报告（2026-11-05）

## 执行摘要

本轮复现并修复两类已提交结果越权读取缺陷，完成 S01～S22 的本地安全测试补齐。最终 Backend 门禁通过：824 个测试通过、0 失败、0 跳过，Release 构建 0 警告 / 0 错误，9 项 Model Drift、ReferenceHost 27 项检查和契约检查通过。**P4 安全验收仍未正式关闭**：MySQL、SQL Server、PostgreSQL 的真实集成测试及本轮修改对应提交 SHA 的 GitHub Actions 尚未执行。

开始基线为 `main @ 0ce8d5c84adb806dc05b0a3c6f4d16c1f1011256`，工作区干净。执行期间仓库于 13:36:52（Asia/Shanghai）被快进至 `cec2292313a2fd2f51e52c5fc14af9b9cdc74957`；已核对两个 HEAD 之间只修改 P4 计划文档，生产源码完全相同。**最终本地验收对应 `cec2292313a2fd2f51e52c5fc14af9b9cdc74957` + 本轮未提交工作区**，不能称为该 SHA 的已提交 CI 验收。上述本地验收阶段未提交、推送、运行服务数据库专项或启动 Docker；后续提交与 CI 按实际提交 SHA 单独核验。

## 测试位置与证据口径

- C：[P4SafetyTests.cs](../tests/Approval.Core.Tests/P4SafetyTests.cs)，真实人工身份、Core 命令执行器和 SQLite。
- H：[P4SafetyEndpointTests.cs](../tests/Approval.AspNetCore.Tests/P4SafetyEndpointTests.cs)，TestServer 的完整 HTTP 请求链路、真实服务端授权和 SQLite。
- O：[OutboxRetryEndpointTests.cs](../tests/Approval.AspNetCore.Tests/OutboxRetryEndpointTests.cs)，完整 HTTP 重试的提交屏障和信号观察。
- D：[P4SafetyDatabaseTests.cs](../tests/Approval.Database.Tests/P4SafetyDatabaseTests.cs) 与 [P4DiagnosticsDatabaseTests.cs](../tests/Approval.Database.Tests/P4DiagnosticsDatabaseTests.cs)，现有数据库 Provider 测试项目；本地仅执行 SQLite。
- 拒绝 / 回滚用例使用新 scope 查询未跟踪行，比较任务、实例、时限、BusinessSlot、日志、Outbox、Operation 完整结果及宿主记录 / 回调。批量失败只要求当前实例回滚，已经成功的实例必须保留。
- 初始覆盖依据测试正文。已有管理授权测试多只断言状态码 / 异常 / 待办数量；原批量恢复测试未验证新增候选、当前逐任务撤权、最终汇总资源撤权；S22 已有完整提交屏障。本轮优先补足这些证据。

## S01～S22 验收矩阵

“通过”指本地已执行的安全断言，**不表示服务数据库 CI 已通过**。初始覆盖与本轮处理分开记录。

| 编号 | 安全场景 | 初始覆盖 / 缺失断言 | 本轮处理与精确测试证据 | 最终结果 |
| --- | --- | --- | --- | --- |
| S01 | 只有 DiagnosticsRead 不能处理 | 部分：管理拒绝缺少诊断后四类写入完整状态 | 新增 H.`ManagementHttp_IndependentPermissionsRejectWithoutAnyWrites(diagnostics-only)`；先读取诊断，再请求重试、单改派、批量、终止；每次均 403、完整快照不变、宿主回调 0 | 本地通过 |
| S02 | 只有 OutboxRead 不能重试 | 部分：缺少 Outbox / Operation 完整断言 | 同上 `outbox-read-only`；先读取非空列表，重试 403，原状态 / 次数 / 完成时间 / 令牌 / Operation 不变 | 本地通过 |
| S03 | 缺少 TaskReassign | 部分：缺少时限、接收任务、审计和事件 | 同上 `reassign-denied`，包括任务原行、时限、日志、Outbox 的完整快照 | 本地通过 |
| S04 | 缺少 TaskReassignBulk | 部分：缺少 plan/item/result 存储证据 | 同上 `bulk-denied`；不创建任何批量 Operation，也没有实际改派 | 本地通过 |
| S05 | 批量权限不能覆盖逐任务权限 | 部分：已有 `PermissionTests.BulkGrant_DoesNotOverridePerTaskDenial` 只断言 Count / todo | 新增 C.`BulkGrant_PerTaskDenialWithinSameInstancePreservesRejectedTasks(false / true)`，同实例两个任务部分 / 全部拒绝；拒绝项 Skipped、原行不变且无相关日志 / Outbox；C.`BulkPartialSaveFailure_OriginalPlanResumesWithCurrentPerTaskAuthorization(true)` 验证恢复时重新授权 | 本地通过 |
| S06 | 缺少 InstanceTerminate | 部分：缺少占用和回调 | H.`ManagementHttp_IndependentPermissionsRejectWithoutAnyWrites(terminate-denied)`，实例、开放任务、BusinessSlot、Operation 不变，回调 0 | 本地通过 |
| S07 | QueryScope 外资源 / 伪造 operatorId | 部分：现有 Scope / 伪造审批身份未覆盖管理三类资源 | H.`ManagementHttp_QueryScopeExcludesRealResourceWithoutWrites` 覆盖任务、实例、Outbox；C.`ManagementCore_ForgedOperatorRejectsWithoutWrites` 覆盖单改派、终止、批量；复用 `PermissionTests.ProviderReceivesCanonicalResource_RevocationHasNoCachedAllow`、`DataScope_AppliesBeforePaginationCountAndBulkReassign`、`UnavailableQueryScope_DoesNotReturnUnfilteredRows` | 本地通过 |
| S08 | 新意图不能改派旧任务 / 终态实例 | 部分：没有完整三类旧任务负向快照 | C.`ReassignCore_InvalidStateOrAssigneeRejectsWithoutLeaks` 的 approved/canceled/transferred/ended-instance；用新键，断言实际拒绝原因和全部存储不变，与历史重放区分 | 本地通过 |
| S09 | Succeeded 不允许新重试 | 缺少新键与副作用证据 | C.`RetryCore_OuterRollbackPreservesFailedStateAndOnlyCommitWakes` 的 Succeeded 新意图断言；全部状态 / Operation 保留，无信号；复用既有投递成功用例 | 本地通过 |
| S10 | 投递 / 乐观并发不覆盖获胜状态 | 部分：已有投递屏障，缺少人工重试失败后的赢家状态 | 复用 `DatabaseContractTests.AtomicOutbox_RetryDuringDeliveryIsRejected_AndConcurrentDispatchDoesNotDuplicate`，阻塞真实处理器；新增 D.`OutboxRetry_OptimisticConflictDoesNotOverwriteWinnerOrCommitOperation`，注入并发异常并提交赢家，断言 Succeeded / Attempts=4 / Stamp=9 / 完成时间、无重试 Operation 或处理器调用。后者是故障注入测试，不冒充服务数据库真实竞争 | 本地通过 |
| S11 | 四类同键重复 / 并发只提交一次 | 部分：原并发主要证明 Start | D.`ManagementCommands_ConcurrentSameKeyCommitOneResult` 的 retry/reassign/bulk/terminate，以 TCS 开始屏障同时提交；验证 Operation、原 / 接收任务、日志、Reassign / TaskCreated / Terminate / Completed Outbox 数量；H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 完整快照验证四类 HTTP 重放；至少一次投递独立见 S15 | 本地通过 |
| S12 | 同键异请求 Conflict | 部分：已有 Core 批量内容冲突，缺普通 HTTP 管理命令 | H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 改派 / 批量 / 终止更改原因后 409，原记录 / 结果不变；复用 `StrictCommandBoundaryTests.KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion` | 本地通过 |
| S13 | 部分成功后原计划恢复 | 部分：未验证新增候选 / 当前任务撤权 / 失败实例完整回滚 | C.`BulkPartialSaveFailure_OriginalPlanResumesWithCurrentPerTaskAuthorization`：第二实例保存失败、首实例保留、失败实例完整回滚、新实例不混入、原键恢复后 Count / Skipped 和审计正确；H.`ManagementHttp_UnknownCommitRecoversOriginalKeyWithoutDuplicateEffects(bulk)` 验证首实例 COMMIT 确认丢失后的 HTTP 恢复；两份既有 BulkReassignmentConflict 测试保留原竞争，新增撤权拒绝 / 恢复后重放 | 本地通过 |
| S14 | Running / Returned 终止收尾 | 部分：原 Running 用例缺完整 Returned 占用 / 事件证据 | C.`TerminateCore_RunningOrReturnedCancelsTasksAndReleasesSlot`；并行节点加签后分别终止 Running / Returned，开放任务清空、StateJson 清理、BusinessSlot 释放、完成时间、可信审计、默认拒绝回调和 Completed 落库；复用 `AdminTests.Terminate_closes_parallel_branches_and_calls_rejected_handler_by_default` | 本地通过 |
| S15 | 回调 / 保存 / 并发回滚、外部副作用 | 部分：已有事务测试，缺管理命令与外部去重断言 | C.`TerminateCore_CallbackFailureRollsBackHostAndApprovalAndInvalidatesScope`；批量保存故障见 S13；Outbox 并发故障见 S10；C.`OutboxAtLeastOnce_HostDeduplicatesExternalEffectByStableEventId` 先模拟外部副作用成功但确认丢失，允许同一 Event.Id 两次处理尝试，宿主去重后副作用只有一次；复用 `AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` | 本地通过 |
| S16 | 停用 / 自审 / 重复 / 资格 / Resubmit / 原因 / 时限 | 部分：诊断资格测试不能替代实际改派拒绝 | C.`ReassignCore_InvalidStateOrAssigneeRejectsWithoutLeaks` 的 inactive/self/duplicate/unqualified/qualified-reason/resubmit，具体拒绝原因与完整快照；C.`ReassignCore_WaitingOrSuspendedKeepsScheduleAndDefersNotification` 保留状态、DueAt / NextRemindAt / Order / 父任务 / Kind，无提前 NotificationRequested；Waiting 原本未开始计时为 null；终止必填原因见 S14 | 本地通过 |
| S17 | Core Retry 的外层提交 / 唤醒 | 部分：原泛化工作单元测试不能代替 Retry | C.`RetryCore_OuterRollbackPreservesFailedStateAndOnlyCommitWakes`：带键外层委托回滚，无 Operation / 唤醒、原完整状态恢复；原键成功后 Pending / Attempts=0 / Stamp 递增且提交前无信号；重放不再唤醒 | 本地通过 |
| S18 | Core 缺键 / 裸 Unit / 批量外层和脏 scope | 部分：原测试未覆盖三类管理命令和宿主跟踪器 | C.`ManagementCore_MissingKeyAndNakedUnitRejectWithoutWrites`；C.`BulkCore_OuterBoundaryRejectsAndPreservesHostTracker` 覆盖外层 executor / 裸事务 / 脏 scope，宿主 Added 实体保留；复用 `OperationTests.RawExternalTransaction_AndBulkInsideHostUnit_AreRejectedBeforeMutations` | 本地通过 |
| S19 | 损坏资源 / Schema / 后台故障只能诊断 | 部分：没有诊断负向 Schema 和完整无写入证据 | C.`DiagnosticsCore_CorruptResourcesAreReadOnlyAndExposeActualFindings` 验证 invalid_definition/missing_node/MissingBusinessSlots/StaleBusinessSlots；C.`DiagnosticsCore_BackgroundFailureIsReportedWithoutRepairWrites`；D.`Diagnostics_ModelDriftOrPendingMigrationFailsClosedWithoutWrites` 用既有 Provider Snapshot 和测试元数据验证 Ready=false / DataAvailable=false / 实际 Drift 或 Pending 值，不生成 / 应用 Migration；复用 `OperationsQueryTests.DiagnosticFindingsCarryScopedResourceLinks_AndExposeSampleTruncation`、HTTP 维护路由缺失及 ApprovalWriteProtectionTests | 本地通过 |
| S20 | HTTP 普通 / 批量键边界 | 部分：Core 键检查不等于 HTTP Header 检查 | H.`ManagementHttp_InvalidKeysRejectOrdinaryAndBulkWithoutWrites`，缺 / 空 / 空白 / 201 字符 / 控制字符 / 多值，用安全 caseId 命名，四类路由均 400、OperationIdRequired、完整状态不变 | 本地通过 |
| S21 | 未知提交恢复 / 撤销结果读取 | 部分且存在实际安全缺陷 | H.`ManagementHttp_UnknownCommitRecoversOriginalKeyWithoutDuplicateEffects` 四类命令；批量在 plan 提交后、首实例提交后丢失确认，原键完成其余实例；C.`BulkPartialReplay_RevokedCommittedItemStopsBeforeRemainingInstance`；C.`BulkSummaryReplay_RevokedResourceAccessRejectsWithoutWrites` 覆盖当前 InstanceRead / Scope / AccessPolicy / Bulk 权限；C.`OutboxReplay_QueryScopeRevokedRejectsWithoutRequeueOrWakeup`；H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 验证真实 HTTP 撤权 | 修复后本地通过 |
| S22 | 完整 HTTP 重试提交边界 | 已有完整屏障，成功信号计数仍可被合并掩盖 | 扩展 O.`OutboxRetry_CommitBoundaryControlsWakeupAndState(false / true)`：提交前无信号、回滚完整状态与 Operation 不变、成功后两者同时落库；预先登记的 waiter 消耗第一次通知，第二次通知若出现则留下信号被断言发现；原键恢复 / 重放不重复排队或唤醒，处理器次数不变 | 本地通过 |

## 问题与最小修复

| 级别 | 问题与根因 | 修复位置 | 复现 / 回归 |
| --- | --- | --- | --- |
| P0（越权读取） | 批量最终汇总直接重放，仅检查来源用户 TaskReassignBulk，跳过原候选实例当前 InstanceRead、QueryScope 和 CanView；Skipped 会泄露实例 / 任务 / 标题 / 节点 | `ApprovalEngine.Admin.cs` 在返回原汇总前读取已提交原计划并逐实例复核；`ApprovalEngine.cs` / DI 延迟解析既有 IApprovalAccessPolicy，保留系统身份和宿主身份解析时机 | `BulkSummaryReplay_RevokedResourceAccessRejectsWithoutWrites` 修复前 read-permission/query-scope/access-policy 3 个用例均未抛异常，修复后拒绝且存储不变 |
| P0（越权读取） | Outbox Retry 未设置 Operation.InstanceId，但存储资源仍关联实例；执行器仅复核历史快照 ACL，未应用当前 QueryScope | `ApprovalCommandExecutor.cs` 对关联实例的未绑定操作重新解析当前资源，然后复核原命令权限；已删除、无关联实例资源的既有重放规则保留 | `OutboxReplay_QueryScopeRevokedRejectsWithoutRequeueOrWakeup` 修复前未抛异常，修复后 NotFound 且状态 / Operation / 信号不变 |
| P3（覆盖不足） | 批量保存故障、HTTP 逐实例未知提交、读取撤权、诊断负向模型和部分信号断言不完整 | 四个 P4 测试文件与既有三个定点测试文件 | 本轮新增 24 个测试方法、71 个 xUnit 用例；没有删除测试、放宽权限或改变原竞争场景 |

生产修改只有四个 Core 文件，约 30 行新增 / 3 行替换。未增加服务抽象、Worker、数据库表、Migration、公开 API、HTTP 路由、DTO、JSON / TypeScript 契约。单任务正常改派、终止状态机、批量独立逐实例提交与部分成功语义保持原规则。安全行为变化仅限当前无权查看资源时拒绝旧结果读取。

## 执行记录

以下保留开发失败与补齐过程；前期逐次执行未单独冻结提交快照，其生产源码基线在上述两个 HEAD 间未变。最终门禁明确对应该最终 HEAD + 本轮未提交工作区，不将失败构建称为测试失败或用历史 CI 替代验收。

- 修复前定点 Core：`dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~P4SafetyTests' --verbosity 'minimal'`，退出 1：通过 5、失败 4、跳过 0；四个失败准确复现上述两类缺陷。
- 修复后最初 Core 定点退出 0：通过 9、失败 0、跳过 0。后续 Core 扩展定点退出 0：通过 41、失败 0、跳过 0；后台诊断新增用例单独通过 1。
- HTTP P4 + 原 S22 定点退出 0：通过 24、失败 0、跳过 0。增强 S22 信号断言后定点通过 2；增强批量首实例未知提交后四类未知恢复定点通过 4。
- 数据库管理并发 / 乐观异常定点退出 0：通过 5、失败 0、跳过 0；负向模型 / Migration 元数据定点退出 0：通过 2、失败 0、跳过 0（SQLite）。
- 首次完整 Backend 退出 1：Core 578 通过 / 1 失败，HTTP 117 通过，数据库 65 通过 / 1 失败，Certificate 59 通过；两个失败为原批量竞争测试的旧撤权后读取预期。保留原竞争场景并增加撤权拒绝、恢复权限重放断言后，完整 Backend 退出 0：Core 579、HTTP 117、数据库 66、Certificate 59，合计 821 通过、0 失败、0 跳过；Release 0 警告 / 0 错误、9 项 Model Drift 和 ReferenceHost 27 项检查通过。
- 最后补充诊断 / 信号断言时有夹具编译及模型缓存键失败，均在测试代码内修正，没有为其修改生产实现。一次 `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'` 在构建阶段退出 1（后台诊断用例缺少命名空间引用），测试未执行；修正后最终使用同一命令完整运行，退出 0。
- 最终 Backend：Core 580、HTTP 117、数据库 68、Certificate 59，**Passed: 824, Failed: 0, Skipped: 0**。其中新增 71 个用例（Core 42 / HTTP 22 / 数据库 7）均实际执行。Release 0 警告、0 错误；Certificate 加两个宿主 / 测试 Context × 四 Provider 共 9 项 Model Drift 均通过；ReferenceHost SQLite 演练 27 项通过，报告为 `output/reference-host/87236752edce4bc19445bb51b587c901/smoke.json`，`passed=true`、`checks=27`。
- 契约：`pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'`，退出 0；API / OpenAPI / JSON Schema / TypeScript 与所有基线一致。未运行 contracts:update。

可复制验证命令（仓库根目录）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'
```

## 证据限制与关闭建议

- SQLite 运行测试与 ReferenceHost 演练不代表 MySQL / SQL Server / PostgreSQL 真实集成通过。三个服务 Provider 本地只执行现有 Model Drift 检查；新增 Provider 测试将由现有 GitHub Actions 矩阵执行，包含合成模型负向诊断和并发故障注入。
- 本轮未修改前端 / SDK / UI；未执行前端全套测试、包消费者或浏览器全套门禁。本轮契约检查实际执行且通过。
- 外部副作用去重使用测试宿主模拟处理器验证 Event.Id 契约，没有接入真实邮件 / 企业消息平台；不能据此宣称跨系统 exactly-once。
- S01～S22 的本地安全行为均有直接证据；正式关闭 P4 前仍需获授权提交 / 推送后检查**该确切提交 SHA** 的服务数据库矩阵及 Windows verify，不使用旧绿色 Run。现阶段保持 P4 安全验收未勾选；可继续下一阶段的本地开发，但不能宣称已完成最终发布验收。
