# Action Engine 安全加固验收报告

修复基线为 `265a341150822064858026fc5f3a6a52ee9a781c`。实施范围见 [安全加固计划](../plans/2026-11-06-action-engine-hardening.md)。

## 1. 修复概览

- P1 投递记录只返回安全运维元数据，实例关联正文通过独立接口读取。
- P1 Notification 在发布和运行时验证可信人员字段。
- P2 条件表达式保留裸字段，新增 `workflow.form` 和 `workflow.outputs`。
- P2 单点 Action 预演沿自动路径运行，累积上游输出并在目标处停止。
- P2 同步和异步执行记录统一的故障诊断字段，保留取消、事务和重试边界。

## 2. 修改文件

| 模块 | 文件 |
| --- | --- |
| Core 读取与投递 | `Authorization/ApprovalResourceReader.cs`、`Engine/ApprovalOutboxService.cs`、`Engine/WorkflowActionDelivery.cs`、`Engine/Runtime/ApprovalEventWriter.cs`、`ApprovalServiceCollectionExtensions.cs` |
| Core 定义与运行 | `Actions/NotificationActionHandler.cs`、`Definitions/FlowDefinitionValidator.cs`、`Engine/Extensions/ApprovalActionValidation.cs`、`Engine/ApprovalEngine.cs`、`Engine/Runtime/ApprovalRuntimeNavigator.cs`、`Engine/Runtime/ApprovalActionExecutor.cs`、`Engine/Runtime/ApprovalActionDiagnostics.cs`、`Conditions/ActionConditionNamespace.cs`、`Conditions/DynamicExpressoConditionEvaluator.cs`、`Engine/ApprovalSimulationService.cs` |
| AspNetCore | `ApprovalEndpointPermissions.cs`、`Contracts/ApprovalResponses.cs`、`Endpoints/DefinitionAndMessageEndpoints.cs` |
| Forms 协作 | Core `Persistence/ApprovalFormsBridge.cs`、Forms `Schema/FormSchemaValidator.cs` |
| Frontend | `api/approval.ts`、`contracts/api.ts`、`contracts/http.generated.ts`、`features/designer/BranchInspector.vue`、`DesignerSimulation.vue`、`NodeInspector.vue` |
| Tests | `OutboxPayloadEndpointTests.cs`、`WorkflowActionEndpointTests.cs`、`HttpResponseContractTests.cs`、`NotificationWorkflowActionTests.cs`、`WorkflowActionTests.cs`、`ActionNodeSimulationTests.cs`、`ActionFailureDiagnosticsTests.cs`、`ApprovalLogConflictDatabaseTests.cs`、`tests/shared/http-resources-v1.json`、`web/tests/browser/package.spec.ts` |
| Contracts / Docs | `contracts/approval.openapi.json`、`contracts/Approval.Core.api.txt`、`contracts/Approval.AspNetCore.api.txt`、`docs/action-engine.md`、`docs/events.md`、`docs/action-troubleshooting.md`、实施计划及本报告 |

新增 Core API `ApprovalOutboxService.GetPayloadAsync`、HTTP 正文路由与响应类型，以及前端 SDK 方法 `outboxPayload`。原列表继续使用现有 DTO 和分页契约，`payloadJson` 作为可省略的兼容字段。流程 JSON 继续使用 Schema v1，EF 模型、迁移、Outbox 存储格式和稳定投递 ID 沿用现有基线。

## 3. 问题修复明细

| 优先级与问题 | 根因与修复 | 回归证据 |
| --- | --- | --- |
| P1 Outbox 正文泄露 | 列表原先读取并序列化整条记录。`ApprovalOutboxService` 改用元数据投影，按目标筛选安全描述和错误摘要。独立正文接口要求 `OutboxRead`、`InstanceReadSensitive`、查询范围和 `Full` 实例访问级别，并验证正文的实例与定义归属。系统记录和未知目标只显示摘要。 | `OutboxPayloadEndpointTests` 验证双能力、无能力、参与人、无访问权限、查询范围、孤立系统消息、正文归属、分页及缓存控制。`HttpResponseContractTests` 和浏览器用例验证列表字段与重试入口。 |
| P1 通知接收人 | Notification 之前没有传入可信人员字段。`NotificationActionHandler` 和 `ApprovalActionValidation` 复用已注册的定义扩展与 Resolver 描述符，在发布和实例绑定版本运行时检查。Forms 入口沿用人员目录校验及字段权限。 | `NotificationWorkflowActionTests` 覆盖 `User`、`Users`、混合规则、非法类型、未知和空字段、缺少扩展以及历史发布定义回滚。`WorkflowActionEndpointTests` 验证未知用户提交失败，合法人员收到站内消息。 |
| P2 条件变量 | 引擎原先用系统别名覆盖同名表单字段，语法检查也可能把字符串或成员名误判为根变量。`ActionConditionNamespace` 检查根路径和冲突，`ApprovalActionExecutor` 保留裸字段并注入显式命名空间。 | `WorkflowActionTests` 验证双重名、大小写冲突、字面量、旧别名、无动作历史流程、运行时回滚、CLR 类型保护和并行输出隔离。 |
| P2 单点预演 | 直接进入目标节点导致输出状态为空。`ApprovalSimulationService` 复用正式自动导航和作用域，从 Start 执行明确注册的预演 Handler，到达目标后停止。 | `ActionNodeSimulationTests` 验证上游依赖、条件选路、缺少预演、上游异常、人工阻断、空审批人自动通过、嵌套 Join、异步计划及数据零写入。 |
| P2 动作诊断 | 同步异常被折叠成固定错误，异步异常被安全包装时丢失内部线索。`ApprovalActionDiagnostics` 保留有界异常链类型和方法栈，执行器与投递器统一字段并继续对外返回安全分类。异步 Handler 失败走 Outbox 退避，保存阶段保留 EF 原有执行策略。 | `ActionFailureDiagnosticsTests` 验证同步 Fail 和 Continue、异步 Handler 和保存失败、损坏快照、取消、LastError、内部异常结构及宿主写入回滚。HTTP 测试断言 ProblemDetails 不含异常正文。 |

## 4. 测试结果

从仓库根目录执行 `pwsh -NoProfile -File './eng/verify.ps1'`。验证对象为基于 `265a341` 的本地修复工作区。

| 测试 | 结果 |
| --- | --- |
| Core Tests | Pass，694 项 |
| AspNetCore Tests | Pass，148 项 |
| Database Tests | Pass，SQLite 87 项 |
| Certificate.Api Tests | Pass，59 项 |
| Frontend Tests | Pass，262 项 Vitest，另有 12 项依赖边界测试、类型检查和构建通过 |
| Browser Tests | Pass，12 项 Playwright，覆盖普通 Outbox 页面、手动重试及采购 ERP 入队时机 |
| API Contract | Pass，OpenAPI、程序集公共 API、生成 TypeScript 和示例契约通过 |
| Flow Schema | Pass，生成的 Schema v1 与基线一致 |
| Release / EF Model Drift | Pass，Release 构建无警告，9 个上下文均无模型漂移 |
| ReferenceHost | Pass，27 项检查 |
| 独立包消费者 | Pass，Certificate、ReferenceHost、嵌入式宿主、测试宿主和 npm 消费者 |
| MySQL | 未执行，待修复提交的 GitHub Actions 矩阵 |
| PostgreSQL | 未执行，待修复提交的 GitHub Actions 矩阵 |
| SQL Server | 未执行，待修复提交的 GitHub Actions 矩阵 |
| Full Verify | Pass，`Verification succeeded: All` |

现有回归覆盖投递 ID、幂等重放、进程重启恢复、退回重提、同步和异步回滚，以及 JavaScript 沙箱、HTTP SSRF、只读 SQL 查询和输出预算。基线提交的 Windows verify 与三个服务数据库矩阵通过，仅作为修复前基线。[基线 CI](https://github.com/ttcc666/WorkflowDemo/actions/runs/42161098143)

## 5. 剩余风险与宿主配置

- 已发布的非法 Notification 人员字段会在进入动作时被拒绝。宿主需要发布修正后的定义，并按现有版本治理流程处理仍绑定历史版本的实例。
- 裸字段 `form` 和 `outputs` 可继续使用。旧限定别名与字段重名时，需把系统变量改为 `workflow.form` 或 `workflow.outputs`。含动作流程的表单字段 `workflow` 需改名。
- 原文可包含完整业务快照。宿主应仅向确需原文且具备完整实例可见性的账号同时授予两项能力。现有外部投递仍由接收方按稳定 ID 去重。
- 历史外部通知事件没有独立步骤执行 ID。渠道故障可通过 `InstanceId`、`NodeId` 和 `OutboxId` 追踪，同步 Notification 动作自身的故障记录包含 `ActionId` 与 `ExecutionId`。

## 6. 最终验收意见

P1 和 P2 的五项修复均已完成，完整本地门禁通过。生产试运行需以修复提交的 Windows verify 和服务数据库矩阵通过为前提，并核对宿主的正文读取权限及历史定义。

## 7. Git 状态

工作基线为 `main` 上的 `265a341`。上表记录本地实施验收，远端验证以修复提交对应的 GitHub Actions 结果为准。
