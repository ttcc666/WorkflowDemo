# P4 安全验收报告（2026-11-05）

## 执行摘要

**P4 安全验收完成。** 验收代码为 `main @ 247991233ffcd77349eeae745bb86a2ec90a550f`。本次重新执行完整 Backend 和 Contracts 门禁，均退出 0：Core 589、HTTP 117、SQLite 数据库 68、Certificate 59，合计 **Passed: 833, Failed: 0, Skipped: 0**；Release 0 警告 / 0 错误，9 项 Model Drift 和 ReferenceHost 27 项检查通过。

已重新核验该确切 SHA 的 [GitHub Actions Run 41981665694](https://github.com/ttcc666/WorkflowDemo/actions/runs/41981665694)：Windows 完整 verify、PostgreSQL、SQL Server、MySQL 四个 Job 全部成功，三个服务数据库各通过 68、失败 0、跳过 0。S01～S22 的具体测试及并发汇总补丁证据见下文；历史失败和验证记录保留。

## 首轮实施基线（历史记录）

开始基线为 `main @ 0ce8d5c84adb806dc05b0a3c6f4d16c1f1011256`，工作区干净。执行期间仓库于 13:36:52（Asia/Shanghai）被快进至 `cec2292313a2fd2f51e52c5fc14af9b9cdc74957`；已核对两个 HEAD 之间只修改 P4 计划文档，生产源码完全相同。**最终本地验收对应 `cec2292313a2fd2f51e52c5fc14af9b9cdc74957` + 本轮未提交工作区**，不能称为该 SHA 的已提交 CI 验收。上述本地验收阶段未提交、推送、运行服务数据库专项或启动 Docker；后续提交与 CI 按实际提交 SHA 单独核验。

## 测试位置与证据口径

- C：[P4SafetyTests.cs](../tests/Approval.Core.Tests/P4SafetyTests.cs)，真实人工身份、Core 命令执行器和 SQLite。
- H：[P4SafetyEndpointTests.cs](../tests/Approval.AspNetCore.Tests/P4SafetyEndpointTests.cs)，TestServer 的完整 HTTP 请求链路、真实服务端授权和 SQLite。
- O：[OutboxRetryEndpointTests.cs](../tests/Approval.AspNetCore.Tests/OutboxRetryEndpointTests.cs)，完整 HTTP 重试的提交屏障和信号观察。
- D：[P4SafetyDatabaseTests.cs](../tests/Approval.Database.Tests/P4SafetyDatabaseTests.cs) 与 [P4DiagnosticsDatabaseTests.cs](../tests/Approval.Database.Tests/P4DiagnosticsDatabaseTests.cs)，现有数据库 Provider 测试项目；本地仅执行 SQLite。
- 拒绝 / 回滚用例使用新 scope 查询未跟踪行，比较任务、实例、时限、BusinessSlot、日志、Outbox、Operation 完整结果及宿主记录 / 回调。批量失败只要求当前实例回滚，已经成功的实例必须保留。
- 初始覆盖依据测试正文。已有管理授权测试多只断言状态码 / 异常 / 待办数量；原批量恢复测试未验证新增候选、当前逐任务撤权、最终汇总资源撤权；S22 已有完整提交屏障。本轮优先补足这些证据。

## S01～S22 验收矩阵

“通过”指上述验收 SHA 的当前测试证据：C / H / O 用例在本地与 Windows CI 全量执行，使用 SQLite；D 用例同时在 SQLite 和 PostgreSQL / SQL Server / MySQL 对应 Provider 的 CI 项目执行。三个服务数据库已实际执行集成测试，不能将它们仅描述为 Model Drift 检查。各用例的故障注入 / 模拟性质仍按原记录说明，不把 Core 的 SQLite 夹具解释为三种服务数据库上的直接运行。初始覆盖与最终验收结果分开记录。

| 编号 | 安全场景 | 初始覆盖 / 缺失断言 | 本轮处理与精确测试证据 | 最终结果 |
| --- | --- | --- | --- | --- |
| S01 | 只有 DiagnosticsRead 不能处理 | 部分：管理拒绝缺少诊断后四类写入完整状态 | 新增 H.`ManagementHttp_IndependentPermissionsRejectWithoutAnyWrites(diagnostics-only)`；先读取诊断，再请求重试、单改派、批量、终止；每次均 403、完整快照不变、宿主回调 0 | 通过 |
| S02 | 只有 OutboxRead 不能重试 | 部分：缺少 Outbox / Operation 完整断言 | 同上 `outbox-read-only`；先读取非空列表，重试 403，原状态 / 次数 / 完成时间 / 令牌 / Operation 不变 | 通过 |
| S03 | 缺少 TaskReassign | 部分：缺少时限、接收任务、审计和事件 | 同上 `reassign-denied`，包括任务原行、时限、日志、Outbox 的完整快照 | 通过 |
| S04 | 缺少 TaskReassignBulk | 部分：缺少 plan/item/result 存储证据 | 同上 `bulk-denied`；不创建任何批量 Operation，也没有实际改派 | 通过 |
| S05 | 批量权限不能覆盖逐任务权限 | 部分：已有 `PermissionTests.BulkGrant_DoesNotOverridePerTaskDenial` 只断言 Count / todo | 新增 C.`BulkGrant_PerTaskDenialWithinSameInstancePreservesRejectedTasks(false / true)`，同实例两个任务部分 / 全部拒绝；拒绝项 Skipped、原行不变且无相关日志 / Outbox；C.`BulkPartialSaveFailure_OriginalPlanResumesWithCurrentPerTaskAuthorization(true)` 验证恢复时重新授权 | 通过 |
| S06 | 缺少 InstanceTerminate | 部分：缺少占用和回调 | H.`ManagementHttp_IndependentPermissionsRejectWithoutAnyWrites(terminate-denied)`，实例、开放任务、BusinessSlot、Operation 不变，回调 0 | 通过 |
| S07 | QueryScope 外资源 / 伪造 operatorId | 部分：现有 Scope / 伪造审批身份未覆盖管理三类资源 | H.`ManagementHttp_QueryScopeExcludesRealResourceWithoutWrites` 覆盖任务、实例、Outbox；C.`ManagementCore_ForgedOperatorRejectsWithoutWrites` 覆盖单改派、终止、批量；复用 `PermissionTests.ProviderReceivesCanonicalResource_RevocationHasNoCachedAllow`、`DataScope_AppliesBeforePaginationCountAndBulkReassign`、`UnavailableQueryScope_DoesNotReturnUnfilteredRows` | 通过 |
| S08 | 新意图不能改派旧任务 / 终态实例 | 部分：没有完整三类旧任务负向快照 | C.`ReassignCore_InvalidStateOrAssigneeRejectsWithoutLeaks` 的 approved/canceled/transferred/ended-instance；用新键，断言实际拒绝原因和全部存储不变，与历史重放区分 | 通过 |
| S09 | Succeeded 不允许新重试 | 缺少新键与副作用证据 | C.`RetryCore_OuterRollbackPreservesFailedStateAndOnlyCommitWakes` 的 Succeeded 新意图断言；全部状态 / Operation 保留，无信号；复用既有投递成功用例 | 通过 |
| S10 | 投递 / 乐观并发不覆盖获胜状态 | 部分：已有投递屏障，缺少人工重试失败后的赢家状态 | 复用 `DatabaseContractTests.AtomicOutbox_RetryDuringDeliveryIsRejected_AndConcurrentDispatchDoesNotDuplicate`，阻塞真实处理器；新增 D.`OutboxRetry_OptimisticConflictDoesNotOverwriteWinnerOrCommitOperation`，注入并发异常并提交赢家，断言 Succeeded / Attempts=4 / Stamp=9 / 完成时间、无重试 Operation 或处理器调用。后者是故障注入测试，不冒充服务数据库真实竞争 | 通过 |
| S11 | 四类同键重复 / 并发只提交一次 | 部分：原并发主要证明 Start | D.`ManagementCommands_ConcurrentSameKeyCommitOneResult` 的 retry/reassign/bulk/terminate，以 TCS 开始屏障同时提交；验证 Operation、原 / 接收任务、日志、Reassign / TaskCreated / Terminate / Completed Outbox 数量；H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 完整快照验证四类 HTTP 重放；至少一次投递独立见 S15 | 通过 |
| S12 | 同键异请求 Conflict | 部分：已有 Core 批量内容冲突，缺普通 HTTP 管理命令 | H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 改派 / 批量 / 终止更改原因后 409，原记录 / 结果不变；复用 `StrictCommandBoundaryTests.KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion` | 通过 |
| S13 | 部分成功后原计划恢复 | 部分：未验证新增候选 / 当前任务撤权 / 失败实例完整回滚 | C.`BulkPartialSaveFailure_OriginalPlanResumesWithCurrentPerTaskAuthorization`：第二实例保存失败、首实例保留、失败实例完整回滚、新实例不混入、原键恢复后 Count / Skipped 和审计正确；H.`ManagementHttp_UnknownCommitRecoversOriginalKeyWithoutDuplicateEffects(bulk)` 验证首实例 COMMIT 确认丢失后的 HTTP 恢复；两份既有 BulkReassignmentConflict 测试保留原竞争，新增撤权拒绝 / 恢复后重放 | 通过 |
| S14 | Running / Returned 终止收尾 | 部分：原 Running 用例缺完整 Returned 占用 / 事件证据 | C.`TerminateCore_RunningOrReturnedCancelsTasksAndReleasesSlot`；并行节点加签后分别终止 Running / Returned，开放任务清空、StateJson 清理、BusinessSlot 释放、完成时间、可信审计、默认拒绝回调和 Completed 落库；复用 `AdminTests.Terminate_closes_parallel_branches_and_calls_rejected_handler_by_default` | 通过 |
| S15 | 回调 / 保存 / 并发回滚、外部副作用 | 部分：已有事务测试，缺管理命令与外部去重断言 | C.`TerminateCore_CallbackFailureRollsBackHostAndApprovalAndInvalidatesScope`；批量保存故障见 S13；Outbox 并发故障见 S10；C.`OutboxAtLeastOnce_HostDeduplicatesExternalEffectByStableEventId` 先模拟外部副作用成功但确认丢失，允许同一 Event.Id 两次处理尝试，宿主去重后副作用只有一次；复用 `AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` | 通过 |
| S16 | 停用 / 自审 / 重复 / 资格 / Resubmit / 原因 / 时限 | 部分：诊断资格测试不能替代实际改派拒绝 | C.`ReassignCore_InvalidStateOrAssigneeRejectsWithoutLeaks` 的 inactive/self/duplicate/unqualified/qualified-reason/resubmit，具体拒绝原因与完整快照；C.`ReassignCore_WaitingOrSuspendedKeepsScheduleAndDefersNotification` 保留状态、DueAt / NextRemindAt / Order / 父任务 / Kind，无提前 NotificationRequested；Waiting 原本未开始计时为 null；终止必填原因见 S14 | 通过 |
| S17 | Core Retry 的外层提交 / 唤醒 | 部分：原泛化工作单元测试不能代替 Retry | C.`RetryCore_OuterRollbackPreservesFailedStateAndOnlyCommitWakes`：带键外层委托回滚，无 Operation / 唤醒、原完整状态恢复；原键成功后 Pending / Attempts=0 / Stamp 递增且提交前无信号；重放不再唤醒 | 通过 |
| S18 | Core 缺键 / 裸 Unit / 批量外层和脏 scope | 部分：原测试未覆盖三类管理命令和宿主跟踪器 | C.`ManagementCore_MissingKeyAndNakedUnitRejectWithoutWrites`；C.`BulkCore_OuterBoundaryRejectsAndPreservesHostTracker` 覆盖外层 executor / 裸事务 / 脏 scope，宿主 Added 实体保留；复用 `OperationTests.RawExternalTransaction_AndBulkInsideHostUnit_AreRejectedBeforeMutations` | 通过 |
| S19 | 损坏资源 / Schema / 后台故障只能诊断 | 部分：没有诊断负向 Schema 和完整无写入证据 | C.`DiagnosticsCore_CorruptResourcesAreReadOnlyAndExposeActualFindings` 验证 invalid_definition/missing_node/MissingBusinessSlots/StaleBusinessSlots；C.`DiagnosticsCore_BackgroundFailureIsReportedWithoutRepairWrites`；D.`Diagnostics_ModelDriftOrPendingMigrationFailsClosedWithoutWrites` 用既有 Provider Snapshot 和测试元数据验证 Ready=false / DataAvailable=false / 实际 Drift 或 Pending 值，不生成 / 应用 Migration；复用 `OperationsQueryTests.DiagnosticFindingsCarryScopedResourceLinks_AndExposeSampleTruncation`、HTTP 维护路由缺失及 ApprovalWriteProtectionTests | 通过 |
| S20 | HTTP 普通 / 批量键边界 | 部分：Core 键检查不等于 HTTP Header 检查 | H.`ManagementHttp_InvalidKeysRejectOrdinaryAndBulkWithoutWrites`，缺 / 空 / 空白 / 201 字符 / 控制字符 / 多值，用安全 caseId 命名，四类路由均 400、OperationIdRequired、完整状态不变 | 通过 |
| S21 | 未知提交恢复 / 撤销结果读取 | 部分且存在实际安全缺陷 | H.`ManagementHttp_UnknownCommitRecoversOriginalKeyWithoutDuplicateEffects` 四类命令；批量在 plan 提交后、首实例提交后丢失确认，原键完成其余实例；C.`BulkPartialReplay_RevokedCommittedItemStopsBeforeRemainingInstance`；C.`BulkSummaryReplay_RevokedResourceAccessRejectsWithoutWrites` 覆盖当前 InstanceRead / Scope / AccessPolicy / Bulk 权限；C.`OutboxReplay_QueryScopeRevokedRejectsWithoutRequeueOrWakeup`；H.`ManagementHttp_StableReplayAndRevocationPreserveCommittedResult` 验证真实 HTTP 撤权；补充 C.`BulkSummaryConcurrentReplay_RevokedResourceAccessRejectsWithoutLeaks`（InstanceRead / QueryScope / AccessPolicy 三种撤权）、`BulkSummaryFirstCommit_RevokedResourceAccessRejectsAndOriginalKeyRecovers`（首次提交后撤权）、`BulkSummary_SystemReadBypassPreservesWriteGrants`（系统边界）、`BulkSummary_EmptyPlanDoesNotRequireInstanceRead`（空计划）；全部最终汇总返回路径在当前读取检查后才返回 | 修复后通过 |
| S22 | 完整 HTTP 重试提交边界 | 已有完整屏障，成功信号计数仍可被合并掩盖 | 扩展 O.`OutboxRetry_CommitBoundaryControlsWakeupAndState(false / true)`：提交前无信号、回滚完整状态与 Operation 不变、成功后两者同时落库；预先登记的 waiter 消耗第一次通知，第二次通知若出现则留下信号被断言发现；原键恢复 / 重放不重复排队或唤醒，处理器次数不变 | 通过 |

## 首轮问题与最小修复（历史记录）

| 级别 | 问题与根因 | 修复位置 | 复现 / 回归 |
| --- | --- | --- | --- |
| P0（越权读取） | 批量最终汇总直接重放，仅检查来源用户 TaskReassignBulk，跳过原候选实例当前 InstanceRead、QueryScope 和 CanView；Skipped 会泄露实例 / 任务 / 标题 / 节点 | `ApprovalEngine.Admin.cs` 在返回原汇总前读取已提交原计划并逐实例复核；`ApprovalEngine.cs` / DI 延迟解析既有 IApprovalAccessPolicy，保留系统身份和宿主身份解析时机 | `BulkSummaryReplay_RevokedResourceAccessRejectsWithoutWrites` 修复前 read-permission/query-scope/access-policy 3 个用例均未抛异常，修复后拒绝且存储不变 |
| P0（越权读取） | Outbox Retry 未设置 Operation.InstanceId，但存储资源仍关联实例；执行器仅复核历史快照 ACL，未应用当前 QueryScope | `ApprovalCommandExecutor.cs` 对关联实例的未绑定操作重新解析当前资源，然后复核原命令权限；已删除、无关联实例资源的既有重放规则保留 | `OutboxReplay_QueryScopeRevokedRejectsWithoutRequeueOrWakeup` 修复前未抛异常，修复后 NotFound 且状态 / Operation / 信号不变 |
| P3（覆盖不足） | 批量保存故障、HTTP 逐实例未知提交、读取撤权、诊断负向模型和部分信号断言不完整 | 四个 P4 测试文件与既有三个定点测试文件 | 本轮新增 24 个测试方法、71 个 xUnit 用例；没有删除测试、放宽权限或改变原竞争场景 |

生产修改只有四个 Core 文件，约 30 行新增 / 3 行替换。未增加服务抽象、Worker、数据库表、Migration、公开 API、HTTP 路由、DTO、JSON / TypeScript 契约。单任务正常改派、终止状态机、批量独立逐实例提交与部分成功语义保持原规则。安全行为变化仅限当前无权查看资源时拒绝旧结果读取。

## 首轮执行记录（历史证据，不替代本次验收）

以下保留首轮开发失败与补齐过程；前期逐次执行未单独冻结提交快照，首轮生产源码基线在 0ce8d5c / cec2292 两个 HEAD 间未变。这里的 824、未提交工作区和此前未执行的 CI 均描述首轮历史时间点；本次最终验收的 833 与服务数据库证据单独记录于下一节。

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

## 并发汇总补丁与当前执行记录

验收 SHA：`247991233ffcd77349eeae745bb86a2ec90a550f`。本次开始时工作区干净，Backend、Contracts 和 CI 核验完成前没有修改生产代码或勾选完成状态。本次只更新验收文档。

### 最终汇总并发授权补丁

首轮顺序重放检查仍遗漏最初 AnyAsync 返回 false、末尾 ExecuteAsync 命中并发已提交汇总的路径。A / B 使用相同身份、键和请求；B 完成实例重放后，A 提交汇总；此时撤销读取权限，旧末尾返回路径仍返回汇总。

`2479912` 在 [ApprovalEngine.Admin.cs](../src/Approval.Core/Engine/ApprovalEngine.Admin.cs) 提取 `ValidateBulkResultReadAccessAsync`，顺序重放、首次提交和执行器内并发重放均在返回前对原候选实例检查 InstanceRead、当前 QueryScope、AccessPolicy。系统操作人例外、可信身份和显式写权限边界保持。检查只读，不重新执行已提交改派或修改 Operation。

新增 4 个测试方法、9 个用例，首轮 71 个加本补丁 9 个共 80 个新增安全用例：

| 精确测试方法 | 用例 | 控制点与不变量 |
| --- | ---: | --- |
| `BulkSummaryConcurrentReplay_RevokedResourceAccessRejectsWithoutLeaks` | 3 | TCS + 查询拦截器证明双方经过最初检查、B 已完成实例重放、A 汇总已提交、B 命中最终记录重放；撤销 InstanceRead / Scope / AccessPolicy 后拒绝且不泄露任务 / 实例 / 标题 / 原因；数据库完整快照不变；恢复权限原键重放相同结果 |
| `BulkSummaryFirstCommit_RevokedResourceAccessRejectsAndOriginalKeyRecovers` | 3 | 在汇总提交后撤权，首次返回同样拒绝；已提交任务 / 审计 / Operation 保留，恢复权限原键读取，不重复改派 |
| `BulkSummary_SystemReadBypassPreservesWriteGrants` | 2 | 合法系统身份可按既有例外读取汇总；没有逐任务或批量写权限仍拒绝 / 跳过，不扩张系统能力 |
| `BulkSummary_EmptyPlanDoesNotRequireInstanceRead` | 1 | 没有实例的计划不要求全局 InstanceRead；原键重放不产生新 Operation |

并发复现修复前 0 通过 / 3 失败 / 0 跳过，退出 1；修复后的批量定点回归 28 通过 / 0 失败 / 0 跳过，退出 0。相关原有冲突测试增加首次拒绝与恢复权限断言，原竞争过程和任务 / 日志 / 事件断言保留。没有 Task.Delay、随机调度、删除测试或放宽安全断言。具体方法在 [P4SafetyTests.cs](../tests/Approval.Core.Tests/P4SafetyTests.cs)。

### 本次本地门禁

在仓库根目录顺序执行，均为验收 SHA 的已提交源代码；没有同时构建共享输出目录：

| 实际命令 / 范围 | 通过 | 失败 | 跳过 | 退出码 |
| --- | ---: | ---: | ---: | ---: |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'` | 833 | 0 | 0 | 0 |
| 其中 Core 全量 | 589 | 0 | 0 | 0 |
| 其中 Approval.AspNetCore 全量 | 117 | 0 | 0 | 0 |
| 其中 SQLite 数据库全量 | 68 | 0 | 0 | 0 |
| 其中 Certificate 全量 | 59 | 0 | 0 | 0 |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'` | 基线一致 | 不适用 | 不适用 | 0 |

Backend 的测试实际命令为 `dotnet test 'Workflow.slnx' --no-build --no-restore --configuration 'Release' --verbosity 'minimal'`；Release 构建无警告、无错误。9 个 Migration / Model Drift 检查全部通过。ReferenceHost SQLite 演练通过 27 项，当前报告为 [smoke.json](../output/reference-host/f771ed35f3dd4e5f993e97fa8d86e126/smoke.json)，`passed=true`、`checks=27`。契约检查包含公共 API、OpenAPI、JSON Schema 与生成 TypeScript，均匹配已检入基线；未运行 contracts:update。

### 当前 SHA 的 GitHub Actions 与真实服务数据库

[Run 41981665694](https://github.com/ttcc666/WorkflowDemo/actions/runs/41981665694)，工作流 `approval-pilot`。API 核验 `headSha=247991233ffcd77349eeae745bb86a2ec90a550f`、`status=completed`、`conclusion=success`；Run 创建于 2026-11-05 14:37:34，API 最后更新时间为 14:46:05（Asia/Shanghai）。本次重新读取 Job 状态及日志，没有使用其他分支或旧 SHA 的绿色 Run。

| Job / ID | 实际 CI 命令 | 实际测试结果 | 结果 |
| --- | --- | --- | --- |
| Windows verify / 127092924715 | `./eng/verify.ps1` | 后端 833 / 0 / 0；前端 approval-ui 242、demo 6；9 项 Model Drift、ReferenceHost 27 项、契约、包消费者与浏览器门禁通过 | success，退出 0 |
| PostgreSql / 127092925081 | `./eng/verify-databases.ps1 -Provider 'PostgreSql'` | Passed: 68, Failed: 0, Skipped: 0 | success，退出 0 |
| SqlServer / 127092924941 | `./eng/verify-databases.ps1 -Provider 'SqlServer'` | Passed: 68, Failed: 0, Skipped: 0 | success，退出 0 |
| MySql / 127092924963 | `./eng/verify-databases.ps1 -Provider 'MySql'` | Passed: 68, Failed: 0, Skipped: 0 | success，退出 0 |

三个服务 Provider 在 CI 中实际创建隔离数据库并运行现有集成测试。本地没有启动 Docker 或运行服务数据库专项。Job 日志均含 `Database verification succeeded`；脚本实际解析 `database.trx`，核对定义 / 结果数量、非零发现、全部执行和 passed / failed / notExecuted 计数，确认不是只编译或空发现的成功。

## 证据边界与关闭结论

- 本次五项检查全部通过，S01～S22 有当前版本的直接测试证据；并发最终汇总授权遗漏已修复并经三种撤权回归验证。**已将 P4 安全验收标记为完成，验收代码版本为 `247991233ffcd77349eeae745bb86a2ec90a550f`。**
- 数据库 Provider 模型检查、SQLite 本地测试和三个服务数据库真实 CI 测试分别记录，不相互替代。
- 外部副作用去重使用测试宿主模拟处理器验证 Event.Id 契约，没有接入真实邮件 / 企业消息平台；继续保持至少一次投递和宿主去重，不宣称跨系统 exactly-once。该限制属于产品边界，不阻塞本次 P4 验收。
- 本次只更新报告与计划完成状态；公共 API、HTTP / JSON / TypeScript 契约、数据库模型、Migration、审批状态机、逐实例批量事务和稳定键规则未变。关闭 P4 安全验收不代表所有宿主的生产上线验收。
