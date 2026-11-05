# 阶段一实施与验证报告

2026-11-05 P4 最终安全验收完成：代码 SHA `247991233ffcd77349eeae745bb86a2ec90a550f` 的 S01～S22、最终汇总并发撤权回归、重新执行的 Backend（833 通过 / 0 失败 / 0 跳过）与 Contracts 门禁均通过；该确切 SHA 的 Windows 完整 verify 及 PostgreSQL / SQL Server / MySQL CI（各 68 / 0 / 0）全部通过。当前证据与完成结论见 [P4 安全验收报告](p4-safety-acceptance-2026-11-05.md)。下文原实施与验证记录保留为历史证据，不替代本次最终验收。

日期：2026-11-04（Asia/Shanghai）。工作分支：`main`。基线 HEAD：`76219b4d241ec989b5fe8926bd0c5b519baf2cd8`。开始时工作区干净；以下验证针对该基线上的本次未提交改动。未提交、推送、合并或发布，没有可用于本次改动验收的 CI 提交 SHA。

## 1. 工作包与实际变化

| 工作包 | 结果 |
|---|---|
| P1 文档治理 | 统一嵌入式审批、单宿主单实例与可选 Forms 边界；旧单体编排、单实例工作流和多租户路线顶部标记被取代；保留历史记录。新增当前计划和接入指南。 |
| P2 发布前模拟 | 新增只读 `/definitions/simulate`、模拟 DTO / 服务及设计器面板。正式执行与模拟共用 `ApprovalAutomaticTraversal`；条件、并行、嵌套 Join 和步数限制只有一份实现。人工决策边界停止；无实例、任务、日志、抄送、消息、Operation 或 Outbox 写入。 |
| P3 审批人预览 | 节点及单规则共用同一模拟 API 和 `ApprovalAssigneeService`，支持所有已注册 Resolver。展示分配对象、当前有效成员、排除原因及实际组织解析链；保持角色展开、顺序和去重。 |
| P4 异常中心 | 扩展诊断页，展示健康、模型 / Migration、心跳、有限异常样本及资源定位。复用 Outbox、超期实例和实例详情中的原有管理操作，没有新增修复命令或异常表。 |
| P5 SDK 与 UI 幂等 | 22 个受管理写方法要求非空稳定键；补齐批量改派和监控终止。所有现有 UI 意图入口增加同页原内容恢复，原任务按钮消失后仍能重放原键 / 原正文。批量部分提交后的 4xx 也保留恢复键。 |

有意修复的运行时有效性问题：同一角色必须至少有一名成员同时满足目录与状态条件，不能由不同成员分别满足；岗位资格和诊断使用相同的用户状态过滤；要求岗位资格却未注册目录的节点提前拒绝，事务不产生部分实例 / 任务。没有改变人工状态机、定义版本绑定、审批方式、Outbox 事务或批量逐实例提交语义。

设计与使用说明见 [预演与异常处理指南](definition-simulation-and-operations.md)。

## 2. 三条重点约束的实现证据

### 模拟不建设第二套引擎

`ApprovalRuntimeNavigator` 现在只适配正式执行的任务 / 审计 / 通知副作用，共享遍历负责自动流程控制。`ApprovalSimulationService` 的适配器只构造结果，不构造 `ApprovalInstance` 或 `ApprovalTask` 实体。两者使用同一条件求值器、`FlowStructure`、Join 到达身份和 `ApprovalAssigneeService`。

只模拟已知输入下的自动路径；不伪造人工同意、人工修改表单或完成回调。并行分支独立到人工边界，Join 未到齐时显示等待。Forms 输入复用正式规范化与公式计算；Core 没有引用 Forms 类型。

### 异常中心不修数据库

没有新增直接写实体、SQL 修复、ExecuteUpdate / Delete、异常表或 Worker。诊断发现补充关联实例、任务及绑定定义标识。诊断样本明确标示截断，筛选 / 分页只针对本次样本；失败投递和完整实例列表进入原有数据库分页页面。

超期实例筛选在 QueryScope 后的 IQueryable 中完成，分页和 count 之前生效。诊断读权限不授予读取全部实例、改派、终止或投递重试权限；实际操作仍由原服务重新授权、检查状态并使用原审计和并发机制。

### 幂等闭环覆盖真实 UI

SDK 不生成随机键，缺键在发出请求前拒绝。UI 的 `CommandRecovery` 使用现有意图管理器保留的同页内存快照，恢复不会从刷新后的任务、表单或版本令牌重新拼正文。存储中仍只有键和指纹。身份变更不能使用其他身份的回调。

浏览器用例真实提交审批后故意丢弃响应：同页重新获取业务状态后原同意按钮消失，恢复入口仍可用内存快照发送完全相同的 Header 与正文；结果成功恢复，审批审计只有一条。该用例没有执行 `page.reload()`，不证明整页重新加载后自动恢复原正文的能力。

## 3. 契约变化

| 契约 | 实际变化 |
|---|---|
| C# 公共 API | 新增模拟请求 / 结果 / 步骤 / 分配 / 成员类型及 `ApprovalSimulationService`；新增 `approval.definition.simulate`；`ApproverResolveContext.Trace` 和 `ApproverResolutionTrace`；`IApprovalDefinitionExtension.NormalizeSimulationData`；诊断 Finding 增加关联标识；`InstanceQuery.OverdueOnly`。 |
| HTTP | 新增只读 POST `/api/approval/definitions/simulate`，可带 `nodeId` / `ruleIndex`；实例列表增加 `overdueOnly`；诊断响应补充资源关联。模拟无 Idempotency-Key 要求，无申请人切换参数。 |
| OpenAPI | 用现有 ContractTool 生成；同步只读模拟的无键例外，以及查询 / 响应字段。 |
| TypeScript / npm SDK | 新增模拟类型和方法；写方法的 `operationId` 改为必需，原有可选表单参数使用明确的 `undefined` 占位；意图管理器新增同页 retry / canRetry、hasPending 与批量保留错误选项。 |
| 扩展接入 | 自定义 `IApprovalDefinitionExtension` 必须实现只读的 `NormalizeSimulationData`；不拥有 Forms 快照的实现可原样返回。宿主明确授予 simulate、directory.read 和 definition.validate 权限。 |
| 流程 / Forms JSON Schema | 没有改变，schemaVersion 仍为 1。 |
| 数据库模型 / Migration | 没有改变，没有生成或重建 Migration。 |
| Samples | 消费更新后的框架 / SDK；无需新业务模型。样例独立包消费和浏览器验证使用新的包。 |

已审阅生成差异。仅 C# API、HTTP / OpenAPI、SDK / TypeScript 的预期基线变化；流程和表单 Schema、各 Provider Migration / Snapshot 均无差异。

## 4. 工作包验收证据

| Requirement | Evidence |
|---|---|
| 条件优先级、默认分支、人工边界 | `ConditionsRespectPriorityAndDefault_AndStopAtHumanBoundary`；`MissingConditionVariableFails_WithoutSelectingDefault`。 |
| 并行、嵌套汇聚及自动策略 | `ParallelJoinWaitsForHumanBranches_OrCompletesAutomaticBranches`；`NestedParallelBranchesUseSharedJoinRules`。 |
| 步数预算及取消 | `SimulationUsesRuntimeAutomaticStepBudget`；`InFlightResolverObservesCallerCancellation`。 |
| 无持久化 / 业务副作用 | `SimulationHasNoPersistentOrBusinessEffects_AndHonorsCancellation`；`ResolverCannotSaveHostBusinessDataDuringPreview`。 |
| Forms 数字类型、公式与非法输入 | `OptionalFormsNormalizeValuesAndCalculateFormulas`。 |
| Resolver 全覆盖及角色任务语义 | `AllRegisteredResolverTypesUseRuntimeResolution`；`RolePreviewPreservesRuntimeTaskAssignment`。 |
| 资格、自审、未知目录与单规则 | `RoleRequiresSameMemberToPassBothDirectoryAndStatus_InPreviewAndRuntime`；`SelfApprovalAndInvalidRuleIndexAreExplicit_AndDoNotChangeDefinition`；`MissingDirectoryAndStatusAreReportedAsUnknown`；`SingleRulePreviewResolvesOnlySelectedRule_WhileWholeNodeKeepsOrderAndDeduplicates`；`QualifiedNodeWithoutDirectoryFailsBeforeCreatingInstanceOrTasks`。 |
| HTTP 权限、可信申请人与无键只读调用 | `SimulationNeedsAuthorization_ButNoKey_AndCannotImpersonateApplicant`；`InvalidSimulationInputReturnsExplicitErrors`。 |
| 模拟 UI 过期响应与草稿保护 | `模拟发送快照且不修改草稿，输入改变后旧结果立即失效`；`丢弃 %s 改变后的迟到响应`；浏览器 `packaged simulation stops at decisions and node preview leaves the saved draft unchanged`。 |
| 异常资源范围、样本截断、超期分页 | `DiagnosticFindingsCarryScopedResourceLinks_AndExposeSampleTruncation`；`OverdueInstanceFilterIsScopedAndPaged_AndExcludesCurrentTasks`；`OperationsOverdueFilter_TranslatesScopeAndPaginationOnEachProvider`（本地 SQLite）。 |
| 所有 SDK 写方法的键 | 参数化 `%s forwards exactly the supplied stable key` 与 `%s rejects absent or blank keys before sending`，覆盖 22 个方法。 |
| 原键 / 原内容恢复与身份隔离 | `重建意图管理器并重新提供原正文可恢复原键，存储不包含表单正文`（重建 manager 并再次提供原正文，未执行浏览器 Reload）；`任务按钮消失后可用内存快照重试，编辑表单不会改变原请求`；`CommitOutcomeUnknown 即使由 4xx 返回也保留原键`；`同页重复点击不会启动第二个发送；身份切换不会删除原用户的未知结果`。 |
| 批量部分提交、重建意图管理器及零待办恢复 | `批量改派部分提交后发生 4xx，重建意图管理器并提供原正文可恢复原键`：重新提供原正文后恢复存储中的原键；不是浏览器整页 Reload 后自动恢复正文。 |
| 上传与只读例外 | `file upload keeps the existing raw file boundary without a command key`；`read-only simulation and validation need no key`。 |
| 真实提交结果未知的同页恢复 | 浏览器 `packaged approval recovers the original key and payload after the server commits but response is lost`，同页重试断言键 / 正文完全相同，最终 Approved 且仅一条 Approve 日志；未执行整页 Reload。 |

新增断言已按外部结果复核，包含状态、具体对象顺序、拒绝类型、数据库最终状态、请求 Header / 正文及审计条数；未用随机等待制造并发。仅做静态伪变异映射和实际业务回归，没有宣称执行 mutation testing。

## 5. 实际验证命令与结果

以下均为本地执行；完整后端门禁先通过，后续新增的测试用完整 Core 回归和最后一项定向负向检查补充。没有以编译通过代替测试通过。

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend' -SkipRestore
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --verbosity 'minimal'
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~QualifiedNodeWithoutDirectoryFailsBeforeCreatingInstanceOrTasks' --verbosity 'minimal'
Set-Location './web'
pnpm 'contracts:check' '--no-build'
```

| 检查 | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core 完整回归 | 493 | 0 | 0 |
| 最后补充的无目录岗位资格负向测试 | 1 | 0 | 0 |
| ASP.NET Core 完整测试 | 92 | 0 | 0 |
| 数据库默认 SQLite 测试 | 57 | 0 | 0 |
| Certificate API 完整测试 | 59 | 0 | 0 |
| UI Vitest | 236 | 0 | 0 |
| Demo Vitest | 6 | 0 | 0 |
| 前端依赖边界测试 | 7 | 0 | 0 |
| 最终独立包浏览器验收 | 7 | 0 | 0 |

另外：Release 解决方案构建 0 warnings / 0 errors；9 个 Context 的 Model Drift 检查无变化；ReferenceHost smoke 27 项通过；前端依赖检查、TypeScript、库与 Demo 构建通过；契约重新生成与基线一致。

包验证执行：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify-packages.ps1' -SkipBuild -RunBrowser
Set-Location './web'
node 'scripts/browser-smoke.mjs' --manifest 'D:/Study/CSharp/Workflow/output/engineering/consumer-5137a43785134e4b9bce36cd5b41bea7/verification.json'
```

最终包的 NuGet / npm 打包、Certificate / ReferenceHost 独立消费、Core-only EmbeddedHost 消费均通过；EmbeddedHost 含 18 项事务 / 权限 / 并行 / 事件检查。包脚本首次浏览器阶段为 6 通过 / 1 失败：测试选择器同时命中隐藏预览和编辑画布，不是后端模拟失败。限定到可编辑节点并等待对话框关闭后，以同一组包和消费者重新运行完整浏览器阶段，最终 7 / 0 / 0、退出码 0。没有把原脚本的首次非零退出记作整条脚本一次通过。

最终包证据目录：`output/engineering/consumer-5137a43785134e4b9bce36cd5b41bea7/`；浏览器结果见其 `browser/results.json`、`browser/passed.json`。

## 6. 边界、风险与未执行项

- MySQL、SQL Server、PostgreSQL 服务数据库未在本地运行，也未启动 Docker。新增跨 Provider 超期查询测试已编译，并在本地 SQLite 运行；三个服务数据库的真实 SQL 翻译 / 行为仍需本次未来提交 SHA 的 CI 矩阵验证。
- 预演只到人工边界，不执行真实启动准备、生命周期回调、附件绑定或外部副作用。自定义 Resolver 若依赖真实业务 / 实例标识或忽略取消令牌，应按指南处理；没有强制终止宿主线程的机制。
- 异常样本不是全量故障清单。业务占用和模型问题仅诊断，不提供数据修改工具；未建设异常持久化、告警调度或自动修复平台。
- 同页请求正文只存在内存；真正重载页面后不能从 SHA-256 指纹还原敏感表单，需恢复原内容或核对结果后显式开始新意图。
- 没有未完成的本轮代码工作包；服务数据库 CI 验收待提交 / 推送后执行。本轮没有用户数据库重置、历史删除、兼容层或新后台基础设施。

## 7. 完整文件清单

以下按实际工作区差异列出，包含新增文件，不含忽略的构建产物和测试内部工作记录。

### Core

- `src/Approval.Core/Abstractions/Abstractions.cs`
- `src/Approval.Core/Abstractions/ApprovalDataExtensions.cs`
- `src/Approval.Core/Abstractions/ApprovalPermissions.cs`
- `src/Approval.Core/ApprovalServiceCollectionExtensions.cs`
- `src/Approval.Core/Diagnostics/ApprovalDiagnosticsService.cs`
- `src/Approval.Core/Engine/ApprovalQueryService.cs`
- `src/Approval.Core/Engine/ApprovalSimulationService.cs`
- `src/Approval.Core/Engine/Runtime/ApprovalAssigneeService.cs`
- `src/Approval.Core/Engine/Runtime/ApprovalAutomaticTraversal.cs`
- `src/Approval.Core/Engine/Runtime/ApprovalRuntimeNavigator.cs`
- `src/Approval.Core/Resolvers/OrganizationResolvers.cs`

### ASP.NET Core

- `src/Approval.AspNetCore/ApprovalEndpointPermissions.cs`
- `src/Approval.AspNetCore/ApprovalHttpCommands.cs`
- `src/Approval.AspNetCore/Endpoints/DefinitionAndMessageEndpoints.cs`
- `src/Approval.AspNetCore/Endpoints/Requests.cs`

### Forms

- `src/Approval.Forms/Schema/FormDefinitionValidator.cs`

### Tests

- `tests/Approval.AspNetCore.Tests/SimulationEndpointTests.cs`
- `tests/Approval.Core.Tests/AssigneePreviewTests.cs`
- `tests/Approval.Core.Tests/AsyncValidationTests.cs`
- `tests/Approval.Core.Tests/OperationsQueryTests.cs`
- `tests/Approval.Core.Tests/SimulationTests.cs`
- `tests/Approval.Database.Tests/OperationsCenterDatabaseTests.cs`

### Web

- `web/packages/approval-ui/tests/api/approval.test.ts`
- `web/packages/approval-ui/src/api/approval.ts`
- `web/packages/approval-ui/src/components/CommandRecovery.vue`
- `web/packages/approval-ui/src/components/MessageBell.vue`
- `web/packages/approval-ui/tests/composables/useCommandIntent.test.ts`
- `web/packages/approval-ui/src/composables/useCommandIntent.ts`
- `web/packages/approval-ui/src/contracts/api.ts`
- `web/packages/approval-ui/src/contracts/http.generated.ts`
- `web/packages/approval-ui/src/features/definitions/DefinitionGovernancePanel.vue`
- `web/packages/approval-ui/src/features/definitions/DefinitionVersionsDrawer.vue`
- `web/packages/approval-ui/src/features/designer/ApproverRulesEditor.vue`
- `web/packages/approval-ui/src/features/designer/DesignerSimulation.vue`
- `web/packages/approval-ui/src/features/designer/NodeInspector.vue`
- `web/packages/approval-ui/src/features/designer/designerContext.ts`
- `web/packages/approval-ui/tests/features/designer/useDefinitionSimulation.test.ts`
- `web/packages/approval-ui/src/features/designer/useDefinitionSimulation.ts`
- `web/packages/approval-ui/src/features/diagnostics/DiagnosticsPanel.vue`
- `web/packages/approval-ui/src/features/flow/FlowViewer.vue`
- `web/packages/approval-ui/src/features/flow/simulationState.ts`
- `web/packages/approval-ui/src/features/monitor/HandoverPanel.vue`
- `web/packages/approval-ui/src/features/monitor/MonitorInstances.vue`
- `web/packages/approval-ui/src/views/ApprovalDetailView.vue`
- `web/packages/approval-ui/src/views/DesignerView.vue`
- `web/packages/approval-ui/src/views/DiagnosticsView.vue`
- `web/packages/approval-ui/src/views/OutboxView.vue`
- `web/packages/approval-ui/src/views/StartCenterView.vue`
- `web/packages/approval-ui/src/views/StartFormView.vue`
- `web/packages/approval-ui/src/views/WorkbenchView.vue`
- `web/tests/browser/package.spec.ts`

### Contracts

- `contracts/Approval.AspNetCore.api.txt`
- `contracts/Approval.Core.api.txt`
- `contracts/Approval.Forms.api.txt`
- `contracts/approval.openapi.json`

### Engineering

- `eng/Approval.ContractTool/Program.cs`

### Docs / Plans / Guidance

- `AGENTS.md`
- `README.md`
- `docs/approval-productization-report-2026-11-04.md`
- `docs/architecture.md`
- `docs/definition-simulation-and-operations.md`
- `docs/embedded-host-integration.md`
- `docs/permissions.md`
- `plans/2026-11-02-multitenant-production-hardening.md`
- `plans/2026-11-02-single-instance-workflow-roadmap.md`
- `plans/2026-11-03-monolith-workflow-engine.md`
- `plans/2026-11-04-approval-productization.md`
