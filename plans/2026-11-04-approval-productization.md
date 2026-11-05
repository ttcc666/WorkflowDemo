# 通用审批引擎阶段一产品化实施计划

日期：2026-11-04。状态：代码工作包与本地验收完成；服务数据库 CI 待本次未来提交验证。基线：main / 76219b4d241ec989b5fe8926bd0c5b519baf2cd8，开始时工作区干净。

本计划取代历史多租户、服务任务、持久等待与工作流编排路线。历史实施与验收记录保留，仅作为历史证据。

## 范围与不变量

- 单宿主、单应用实例、模块化单体；共享 DbContext / 事务，Forms 可选。
- 不增加数据库表、Migration、Worker、用户组织体系或 BPMN 能力。
- 模拟只读，复用正式条件、自动导航与审批人解析，停在人工决策边界；不执行通知、业务或生命周期回调。
- 异常中心只组合现有诊断与查询，处理入口只调用现有授权命令。截断样本不能当成全量异常。
- SDK 写命令要求显式稳定键；UI 的未知结果恢复必须同时保留原键与请求内容指纹。

## 实施与逐包验收

- [x] P1：同步产品边界，标记过期路线；检查文档链接及完整 diff。
- [x] P2：从正式导航提取共享自动遍历，增加只读模拟 DTO / 服务 / HTTP / 设计器；覆盖条件优先级、默认分支、并行、嵌套汇聚、人工停止、取消、预算、权限和无副作用。
- [x] P3：共享审批人快照解析，支持单规则和节点预览；区分规则输出、任务分配对象和有效成员，保持角色展开和顺序；覆盖自定义 Resolver、自审、停用、目录缺失与异常。
- [x] P4 功能实现：扩展诊断页，连接 Outbox、监控、详情及已有改派 / 终止入口；覆盖权限、分页及样本截断；不允许数据修复。
- [ ] P4 安全验收：逐项完成 P4.7 的 S01～S22，记录用例、执行命令、对应 SHA 与未验证 Provider；定点回归不能替代整个矩阵。

  **P4.1 安全边界与实现基线**

  本节按 2026-11-05 的代码基线 `1d9fa4f672630cdac2112d173f72cab8ce24d6a1` 补充安全约束。下述“现有能力”是代码已提供的行为；“验收要求”是后续实现与回归必须逐项证明的要求，不代表全部矩阵已完成。HTTP 重试唤醒修复及本地回归证据见 P4.7 的 2026-11-05 记录。P4 的完成标记仅代表异常中心功能实现，不代表 P4.7 的安全验收矩阵已完成；其他工作包状态保持不变。

  - 异常中心只聚合现有诊断与查询、链接已有页面并调用已有业务命令。诊断结果是定位线索，不能作为写操作授权凭据，也不保证操作时资源状态仍与采样时一致。
  - `ApprovalDiagnosticsService.CheckAsync` 自身要求 `DiagnosticsRead`，实例、任务及关联 Outbox 数据按现有 QueryScope 收窄。处理人 Findings 来自受限 Pending 任务样本；`AssignmentSampleTruncated=true` 必须提示样本截断，不能把当前样本、前端筛选或分页结果当作全量异常。
  - 禁止直接修改审批跟踪实体、`Attach` 伪造实体、`ExecuteUpdate` / `ExecuteDelete` 或原生 SQL；尤其不得直接设置 `ApprovalTask.AssigneeValue`、实例终态、任务状态、BusinessSlot、审计或 Operation 记录。保留 `UseApproval()` 的现有写入保护。
  - 不新增通用数据库修复接口、任意审批状态修改 API、SQL 修复入口、第二套审计或事务机制、独立异常修复 Worker，也不增加绕过 `ApprovalEngine` 的任务修改逻辑。

  调用边界如下；批量改派保留独立入口和逐实例事务：

  ```mermaid
  flowchart TD
    UI["异常中心：诊断 / 列表 / 详情"] --> Read["各读取入口独立授权；诊断结果仅用于定位"]
    Read --> Intent["处理意图：可信身份 + 稳定键 + 原请求"]
    Intent --> Managed["ApprovalHttpCommands → IApprovalCommandExecutor"]
    Managed --> Commands["RetryAsync / ReassignAsync / TerminateAsync"]
    Intent --> Bulk["AdminEndpoints → ReassignUserAsync（显式传键）"]
    Bulk --> Items["固定候选实例 → 逐实例事务 → 逐任务授权"]
  ```

  核对依据：业务行为见 [ApprovalOutboxService](../src/Approval.Core/Engine/ApprovalOutboxService.cs)、[ApprovalEngine.Admin](../src/Approval.Core/Engine/ApprovalEngine.Admin.cs)、[ApprovalEngine 的终态收尾](../src/Approval.Core/Engine/ApprovalEngine.cs) 和 [ApprovalTaskCoordinator](../src/Approval.Core/Engine/Runtime/ApprovalTaskCoordinator.cs)；诊断与授权见 [ApprovalDiagnosticsService](../src/Approval.Core/Diagnostics/ApprovalDiagnosticsService.cs)、[ApprovalPermissions](../src/Approval.Core/Abstractions/ApprovalPermissions.cs)、[ApprovalAuthorizationService](../src/Approval.Core/Authorization/ApprovalAuthorizationService.cs) 和 [ApprovalResourceReader](../src/Approval.Core/Authorization/ApprovalResourceReader.cs)。事务与 HTTP 依据见 P4.4、P4.5。

  **P4.2 读取权限与操作权限矩阵**

  权限常量均属于 `ApprovalPermissions`；下表路由使用当前默认前缀 `/api/approval`，前缀可由宿主配置。读取权限之间也不能相互推导：诊断报告可包含其既有统计与样本，但继续读取 Outbox 列表、全部流程或实例详情时，仍须通过各自入口授权。

  | 功能 | 权限常量 / 实际权限码 | 现有 HTTP 入口 |
  | --- | --- | --- |
  | 诊断读取 | `DiagnosticsRead` / `approval.diagnostics.read` | `GET /api/approval/diagnostics` |
  | Outbox 读取 | `OutboxRead` / `approval.outbox.read` | `GET /api/approval/outbox` |
  | Outbox 手动重试 | `OutboxRetry` / `approval.outbox.retry` | `POST /api/approval/outbox/{id:guid}/retry` |
  | 全部流程读取 | `InstanceReadAll` / `approval.instance.read-all` | `GET /api/approval/admin/instances` |
  | 单任务改派 | `TaskReassign` / `approval.task.reassign` | `POST /api/approval/admin/tasks/{id:guid}/reassign` |
  | 批量离职交接（含候选预览） | `TaskReassignBulk` / `approval.task.reassign-bulk` | `GET /api/approval/admin/users/{userId}/tasks`；`POST /api/approval/admin/users/{userId}/reassign` |
  | 流程终止 | `InstanceTerminate` / `approval.instance.terminate` | `POST /api/approval/admin/instances/{id:guid}/terminate` |
  | 实例详情读取 | `InstanceRead` / `approval.instance.read`，并经过现有访问策略 | `GET /api/approval/instances/{id:guid}` |

  - `DiagnosticsRead`、`OutboxRead`、`InstanceReadAll` 均不授予任何处理权限；前端隐藏按钮、缓存 capabilities、角色名或曾经读取过资源，都不能代替服务端授权。
  - 实际写入前由 Endpoint / 命令执行器及 Core 命令执行现有授权。资源 ID 必须解析到真实实例、定义和业务归属，并应用 QueryScope，再由宿主权限 Provider 判断；不得用诊断返回的归属字段替代服务端解析。越界资源沿用现有 `NotFound` / `Forbidden` 语义。
  - `TaskReassignBulk` 只允许发起批量命令，不能代替每个任务的 `TaskReassign`。候选查询先应用数据范围；逐实例命令校验实例资源权限，实际改派仍逐任务授权。
  - HTTP 操作人来自 `http.GetApprovalUserId()`；直接调用 Core 的 `operatorId` 必须与可信 `IApprovalActorContext` 一致。请求正文只传目标用户、原因等业务数据，不信任正文传入的操作人身份。
  - 权限或 QueryScope 服务异常 / 超时沿用 `AuthorizationUnavailable` 的拒绝语义，不降级为允许。已提交结果重放的当前读取 / 资源权限复核见 P4.4，不能因持有旧操作键而绕过。

  **P4.3 必须复用的四类业务命令（现有能力）**

  以下 Outbox 重试、单任务改派和终止片段是带键命令执行器委托内的调用，不能当作无需 OperationId 的独立宿主入口；完整包装示例见 P4.4。批量改派自身协调带键操作，直接使用其独立入口。

  **1. Outbox 失败重试：`ApprovalOutboxService.RetryAsync`**

  ```csharp
  await outbox.RetryAsync(outboxId, ct);
  ```

  - 使用 `OutboxRetry`（`approval.outbox.retry`），按实际 `OutboxId` 重新授权。已 `Succeeded` 的消息拒绝重试；当前实现也允许对未成功的 Pending 消息发起手动重试，不能描述成 Core 仅接受 Failed。
  - 当前宿主正在投递任一批次时，由进程内 `IsDispatching` 检查拒绝，返回 `Conflict`，不能覆盖投递状态；保留消息 `ConcurrencyStamp` 递增及 EF 乐观并发检查，竞争失败同样转为 `Conflict`。
  - 重试将状态恢复 `Pending`，`Attempts=0`，`NextAttemptAt=Now`，清空 `CompletedAt`，保留消息 ID；当前不会清空 `LastError`。在原有工作单元提交成功后，通过 `AfterCommit` 唤醒现有后台投递器，不能把保存状态视为已经投递成功。
  - `RetryAsync` 是管理员手动重试命令。`ProcessDueAsync` 是后台系统投递入口，使用 `OutboxDispatch`（`approval.system.outbox-dispatch`）并要求独立干净 scope，不能代替管理员重试，也不能把系统投递权限授予异常中心。
  - 投递保持至少一次；宿主 `IApprovalEventHandler` 必须按消息 ID（`ApprovalEvent.Id`，与 Outbox ID 一致）对外部副作用去重，不承诺跨系统 exactly-once。关闭 Dispatcher 时仍遵守现有配置，不因手动重试另起投递路径。

  **2. 单任务改派：`ApprovalEngine.ReassignAsync`**

  ```csharp
  await engine.ReassignAsync(
      taskId,
      operatorId,
      toUserId,
      reason,
      ct);
  ```

  - 复用 `TaskReassign` 资源权限、目标用户有效性、实例及任务状态、自审限制、节点审批人重复及岗位资格检查。仅 Running / Returned 实例的 Pending / Waiting / Suspended 任务可进入现有改派规则；Resubmit 任务拒绝改派。
  - 普通改派原因参数可空；`RequireQualifiedAssignee` 节点必须填写原因，目标用户须具备该节点资格。异常中心应保留原因输入，不得宣称 Core 对所有改派都强制非空原因。
  - 通过既有 `HandOver` 完成任务转移：原任务标记 Transferred，生成接收人的新任务，保留原等待 / 挂起状态、时限及加签关系；不是原地覆盖处理人。命令写入 `ApprovalAction.Reassign` 审计并保留原事务与并发控制。
  - 新任务为 Pending 时按既有规则触发 `NotifyEvent.Transferred`；Waiting / Suspended 任务在激活时再通知，不能为异常中心强制补发。严禁直接修改 `ApprovalTask.AssigneeValue` 或另写一套转移逻辑。

  **3. 批量离职交接：`ApprovalEngine.ReassignUserAsync`**

  ```csharp
  var result = await engine.ReassignUserAsync(
      fromUserId,
      toUserId,
      operatorId,
      reason,
      operationId,
      ct);
  // result.Count 为成功任务数；result.Skipped 包含任务、实例及跳过原因。
  ```

  - 必须提供稳定 `operationId`；先检查来源用户资源的 `TaskReassignBulk`，实际改派逐任务检查 `TaskReassign` 并复用单任务改派的业务限制、转移、审计和通知逻辑。
  - 候选为数据范围内 Running / Returned 实例中，直接指派给来源用户的 Pending / Waiting / Suspended 任务，不包含角色任务。首次保存固定候选实例清单，之后按清单处理，不在恢复时重新以已变化的待办列表替换原计划。
  - Core 使用 `task.reassign-bulk.plan`、`task.reassign-bulk.item`、`task.reassign-bulk.result` 保存候选清单、逐实例结果和最终汇总；每个实例使用独立 scope / 事务提交。业务或逐任务权限不满足项写入 `Skipped`；捕获的实例并发 Conflict 按当前可见、仍未完成的用户任务报告跳过。其他异常可能中断批次，之前实例的成功提交仍保留。
  - 相同操作人、键和原请求内容可恢复原计划：已提交实例重放结果，未完成实例继续执行；已有最终汇总则返回原汇总。`Count` 与 `Skipped` 表示部分成功结果，不得改成整批原子成功 / 失败。
  - **批量改派不是普通单事务命令**：不得套在外层 `IApprovalCommandExecutor` 原子委托、`IApprovalUnitOfWork` 或裸数据库事务中，也不得携带未提交跟踪修改；现有命令会拒绝加入这样的 scope。禁止自行遍历数据库修改任务处理人。

  **4. 流程终止：`ApprovalEngine.TerminateAsync`**

  ```csharp
  await engine.TerminateAsync(
      instanceId,
      operatorId,
      reason,
      ct);
  ```

  - 使用实际实例资源的 `InstanceTerminate`；终止原因必须非空白，只允许终止 Running / Returned 实例。
  - 复用原有收尾逻辑取消所有未完成任务（含并行分支及加签任务）、清理运行汇聚状态和业务占用 `ApprovalBusinessSlot`，设置完成信息，并执行既有业务处理器的 `OnTerminatedAsync`（默认按驳回回调处理）。
  - 命令写入 `ApprovalAction.Terminate` 审计与 `Completed` 事件，并按既有规则产生完成通知；业务回调、审批状态、审计及 Outbox 由原有同一事务保证一致性。严禁异常中心直接设置实例终态或批量修改任务状态。

  **P4.4 C# 命令包装与 HTTP 幂等安全**

  直接调用 Core 的宿主必须遵守 [ApprovalOperations](../src/Approval.Core/Abstractions/ApprovalOperations.cs)、[ApprovalCommandExecutor](../src/Approval.Core/Persistence/ApprovalCommandExecutor.cs) 和 [事务与幂等约定](../docs/transactions-and-idempotency.md)。以单任务改派为例，`commands` 为 `IApprovalCommandExecutor`，`operatorId` 来自可信当前身份；示例使用 `Approval.Core.Abstractions`、`Approval.Core.Engine` 和 `Microsoft.Extensions.DependencyInjection`：

  ```csharp
  await commands.ExecuteAsync(
      new ApprovalCommand(
          "task.reassign", taskId.ToString(), operationId,
          new { taskId, toUserId, reason },
          ApprovalPermissions.TaskReassign,
          new ApprovalResource { TaskId = taskId }),
      async (sp, token) =>
      {
          var engine = sp.GetRequiredService<ApprovalEngine>();
          await engine.ReassignAsync(taskId, operatorId, toUserId, reason, token);
          return true;
      }, ct);
  ```

  `Kind` / `Scope` 由宿主定义并保持稳定；上述 `task.reassign` 是宿主命令命名示例，不是新增 API。Payload 必须包含所有影响业务决策的输入。其他两类普通命令按下表同步替换命令描述和委托内调用，服务均从委托传入的 `sp` 重新解析：

  | 委托内调用 | Permission / Resource | Payload 必须覆盖 |
  | --- | --- | --- |
  | `outbox.RetryAsync(outboxId, token)` | `OutboxRetry` / `new ApprovalResource { OutboxId = outboxId }` | `outboxId` |
  | `engine.ReassignAsync(taskId, operatorId, toUserId, reason, token)` | `TaskReassign` / `new ApprovalResource { TaskId = taskId }` | `taskId`、`toUserId`、`reason` |
  | `engine.TerminateAsync(instanceId, operatorId, reason, token)` | `InstanceTerminate` / `new ApprovalResource { InstanceId = instanceId }` | `instanceId`、`reason` |

  不捕获原请求 scope 的 DbContext、Engine 或跟踪实体；需要原子保存的宿主业务变更也在完整委托内从 `sp` 解析同一 DbContext 后完成。单独包一层 `IApprovalUnitOfWork` 不能代替执行器已经预约的稳定操作键，否则普通写命令拒绝为 `OperationIdRequired`。批量改派只用 P4.3 的带 `operationId` 独立入口，不能照搬上述外层包装。

  HTTP 路由已核对 [MapApprovalEndpoints](../src/Approval.AspNetCore/ApprovalAspNetCoreExtensions.cs)、[ApprovalEndpointPermissions](../src/Approval.AspNetCore/ApprovalEndpointPermissions.cs)、[AdminEndpoints](../src/Approval.AspNetCore/Endpoints/AdminEndpoints.cs)、[Outbox Endpoint](../src/Approval.AspNetCore/Endpoints/DefinitionAndMessageEndpoints.cs) 和 [请求 DTO](../src/Approval.AspNetCore/Endpoints/Requests.cs)。以下使用默认前缀；Outbox 路由需启用 `EnableOutboxEndpoints`。GUID、用户和令牌为示例，使用前替换为宿主中的实际值，身份由宿主认证提供。

  ```http
  POST /api/approval/outbox/11111111-1111-4111-8111-111111111111/retry
  Authorization: Bearer <宿主访问令牌>
  Idempotency-Key: p4-outbox-retry-001
  ```

  ```http
  POST /api/approval/admin/tasks/22222222-2222-4222-8222-222222222222/reassign
  Authorization: Bearer <宿主访问令牌>
  Idempotency-Key: p4-task-reassign-001
  Content-Type: application/json

  { "toUserId": "carol", "reason": "原审批人停用，交接处理" }
  ```

  ```http
  POST /api/approval/admin/users/bob/reassign
  Authorization: Bearer <宿主访问令牌>
  Idempotency-Key: p4-user-handover-001
  Content-Type: application/json

  { "toUserId": "carol", "reason": "离职交接" }
  ```

  ```http
  POST /api/approval/admin/instances/33333333-3333-4333-8333-333333333333/terminate
  Authorization: Bearer <宿主访问令牌>
  Idempotency-Key: p4-instance-terminate-001
  Content-Type: application/json

  { "reason": "业务取消，经确认终止审批" }
  ```

  - 普通受管理写命令由 [ApprovalHttpCommands](../src/Approval.AspNetCore/ApprovalHttpCommands.cs) 使用 `Idempotency-Key` 调用命令执行器；请求参数、正文及 query 参与请求摘要。键必须为 1–200 个非控制字符，不能空白，只能发送一个 Header 值。
  - **批量改派是明确例外**：`ApprovalHttpCommands.Managed` 排除 `TaskReassignBulk`。`AdminEndpoints` 显式调用 `ApprovalHttpCommands.OperationId(http)` 读取 Header 并传给 `ReassignUserAsync` 的 `operationId`，后者执行候选 / 逐实例 / 汇总协调；不得认为已由通用 HTTP 命令过滤器包成一个事务，也不能删除 Endpoint 的显式读键。
  - 同一业务意图只生成一次键；超时、断线、双击、响应丢失、5xx 或 `CommitOutcomeUnknown` 时保留**原键和原请求内容**，重试原请求。禁止前端每次重试自动生成新键，也不能从刷新后的任务、用户或原因重建旧意图；结果未知不能标记成确定失败并换键。
  - 键身份为当前操作人、Kind、Scope、OperationId；同一范围和键已有记录但内容摘要不同，必须返回 `Conflict`，不执行新的业务操作。例如保留 `p4-task-reassign-001` 却把 `toUserId` 改为其他人必须拒绝。只有明确创建新业务意图才生成新键；确定回滚、未留下已提交记录时的修正提交遵守现有执行器规则。
  - 已提交的相同请求重放原结果（普通命令可重放 204），不再次执行命令。重放绑定实例的结果时，执行器检查当前 `InstanceRead`、资源范围及 `IApprovalAccessPolicy.GetAccessAsync(...)` 返回的 `CanView`；未绑定实例的结果按记录的资源复核原命令权限，不能概括成一律重新执行写权限检查。批量入口仍先检查 `TaskReassignBulk`，恢复中尚未提交项仍执行实际改派授权。
  - 批量交接后续失败可能已经部分提交，即使得到 4xx 也不能据此换新键；用原键和原内容恢复。切换身份后不得复用另一身份的恢复回调。审计和最终状态需要核实时沿用现有查询，不新增绕过授权的操作结果读取接口。

  **P4.5 事务、并发、审计与业务副作用约束**

  - 沿用 [ApprovalUnitOfWork](../src/Approval.Core/Persistence/ApprovalUnitOfWork.cs)：普通命令的宿主业务数据、审批实例 / 任务、业务占用、审计、Operation 结果及 Outbox 在同一 DbContext / 工作单元提交。Engine Flush 或 `SaveChanges` 不等于 Commit；不得提前提交、创建第二套事务或假设两个 DbContext 自动共享事务。
  - 批量改派以**单个实例**为一致性边界。失败实例不留下半次改派；其他实例已经成功的提交必须保留，不能按“失败操作无部分写入”要求撤销整批，也不能吞异常后继续复用失败的 scope / 实体图。
  - 保留实例与 Outbox 的 `ConcurrencyStamp`、EF 乐观并发、Operation 预约及业务 / 日志唯一约束。仅精确识别的竞争转为 Conflict；无关唯一约束、外键、连接或 SQL 错误不得统一包装为“已处理”。并发验收使用显式同步控制竞争点，不用随机概率或 `Task.Delay` 猜时序。
  - 改派和终止沿用 `ApprovalLog` 的动作、可信操作人、原因及关联资源；批量成功任务复用逐任务改派日志。Outbox 人工重试当前没有单独的 `ApprovalLog` 动作，应核对既有 `ApprovalOperation` 的 ActorId、RequestKey、RequestHash、CommittedAt、ResultJson 与 Outbox 状态 / 尝试记录，不能编造已有“重试审计日志”或另建审计系统。
  - 同步业务 / 生命周期回调不得自行 `SaveChanges`、Commit、改审批实体或重入审批命令；回调失败必须回滚当前命令的本地变更。提交后唤醒只使用原有 `AfterCommit` / `ApprovalOutboxSignal`，不启动第二个 Worker。
  - **HTTP 重试唤醒修正（2026-11-05）**：Outbox Endpoint 只调用 `RetryAsync`，已移除原先在通用 HTTP 命令委托内提前执行的 `signal.Notify()` 及其服务参数。唯一的重试唤醒由 Core 登记的 `AfterCommit` 执行；外层事务提交前不得唤醒，提交失败回滚后也不得留下唤醒信号。同键已提交结果重放不重新排队或唤醒。整条 HTTP 路径由 P4.7 的 S22 定点回归验证，不能以 Core 的单独用例代替。
  - Outbox 事件与审批事务共同落库，之后至少一次投递。投递处理器的本地宿主业务写入与成功确认共用其投递事务；处理器失败回滚这些本地写入，另以新事务记录失败。邮件等外部副作用不能由数据库回滚，继续由宿主按稳定 Event.Id 去重。

  **P4.6 异常类型与允许处理方式**

  下表使用现有 Findings Code 或报告字段；统计项不伪装成逐资源 Findings。处理前仍须读取实际资源并执行 P4.2～P4.5 的检查。

  | 异常 / 诊断线索 | 允许的处理方式 | 必须保留的边界 |
  | --- | --- | --- |
  | `FailedOutbox`，或需要人工重新排队的未成功 Pending 消息 | 经 Outbox 列表定位消息，授权后调用 `RetryAsync` | 不重发 Succeeded；不覆盖正在投递的状态；不调用 `ProcessDueAsync` 代替手动重试 |
  | `unavailable_assignee` | 关联实例 / 任务详情确认后调用 `ReassignAsync`；来源用户离职且符合批量条件时调用 `ReassignUserAsync` | 不从样本直接批量改数据；保留用户有效性、自审、重复及岗位资格检查 |
  | `OverdueTasks` / 超期实例查询 | 读取详情确认原因；确需换人时调用 `ReassignAsync`，确需取消业务时调用 `TerminateAsync` | 超期本身不授予权限，也不授权任意自动同意 / 驳回；不重置时限或绕过现有超时策略 |
  | 人工确认的离职交接（不要求存在独立诊断 Code） | 候选预览后调用 `ReassignUserAsync` | 固定候选实例、逐任务授权、逐实例提交与部分成功恢复 |
  | 人工确认需终止的 Running / Returned 实例 | 填写原因后调用 `TerminateAsync` | 必须能正常通过原命令的定义加载、权限及状态检查；不提供强制终态入口 |
  | `invalid_definition`、`missing_node` | 仅诊断、查看关联标识并人工定位定义 / 数据来源 | 不修补发布定义、实例快照或任务节点；不以强制改派 / 终止绕过原命令失败 |
  | `MissingBusinessSlots`、`StaleBusinessSlots` | 仅诊断与人工定位业务占用不一致 | 不补写、删除或改指向 BusinessSlot；正常终止时的占用清理由原命令完成，不是通用修复 |
  | `PendingMigrations`、`ModelDrift`，或 `Ready=false` / `DataAvailable=false` | 仅报告并交由宿主既有部署 / Migration 流程人工排查 | 不在异常中心运行 Migration、改模型或清库；数据不可用时不能把零统计显示为“无异常” |
  | 后台任务异常（`Heartbeats`、`Contributions`） | 仅展示诊断与人工定位信息 | 不新增修复 Worker、任意后台执行 / 重启接口或系统权限旁路 |

  **P4.7 安全验收要求与已有测试定位**

  以下为必须落实的验收矩阵；本次新增的 HTTP 唤醒定点回归只证明其明确断言的范围，不代表矩阵已全部覆盖，也不改变原实施报告中的执行记录。HTTP 与 Core 入口均应覆盖，断言至少包含数据库最终状态、关联审计 / Operation 和本地业务副作用，不能只检查 HTTP 状态码。拒绝路径以执行前快照对比，确认没有新增任务、处理人 / 状态变更、成功动作日志或业务事件；批量操作按实例边界分别断言。

  | 编号 | 场景与操作 | 必须验证的结果 |
  | --- | --- | --- |
  | S01 | 仅授予 `DiagnosticsRead`，读取诊断后直接请求四类处理命令 | 诊断可读；重试、单改派、批量交接和终止均被拒绝，无业务状态 / 成功审计 / 副作用变化 |
  | S02 | 仅授予 `OutboxRead`，读取失败消息后手动重试 | 列表可读，重试被拒绝；Status、Attempts、ConcurrencyStamp 不变，无重试成功 Operation |
  | S03 | 缺少 `TaskReassign`，直接改派任务 | 拒绝；原任务及处理人、时限不变，无接收任务、Reassign 日志或转交通知 |
  | S04 | 缺少 `TaskReassignBulk`，发起离职交接 | 拒绝；不创建候选计划 / 实例结果 / 汇总，不产生实际改派 |
  | S05 | 授予 `TaskReassignBulk`，对部分或全部任务拒绝 `TaskReassign` | 仅允许项成功；拒绝项进入 Skipped，任务及其日志 / 事件不变；全拒绝时 Count=0 |
  | S06 | 缺少 `InstanceTerminate`，请求终止 | 拒绝；实例、开放任务及 BusinessSlot 不变，不执行终止回调或写终止事件 |
  | S07 | 伪造任务 / 实例 / Outbox ID 操作 QueryScope 外资源，或伪造 operatorId | 沿用 NotFound / Forbidden；服务端解析真实归属并拒绝，目标资源及宿主业务无写入，诊断结果不提供旁路 |
  | S08 | 用新业务意图改派已完成、已取消或已转交任务；并覆盖已结束实例 | 状态检查拒绝，无新接收任务、Reassign 日志或通知；与同键历史结果重放区分 |
  | S09 | 用新键对 Succeeded Outbox 手动重试 | 拒绝；成功状态、完成时间、尝试数及消息 ID 不变，不再次调用处理器 |
  | S10 | 用显式屏障阻塞投递处理器，再手动重试 / 并发调度；另覆盖状态更新竞争 | 投递中重试返回 Conflict，原状态不被覆盖；乐观并发失败不覆盖获胜提交，处理器无重叠执行 |
  | S11 | 四类命令分别使用相同稳定键和相同内容重复 / 并发提交 | 已提交结果重放；业务任务转移 / 终止回调 / 人工重新排队各只提交一次，不因命令重放重复创建适用的操作日志、Operation 记录及 Outbox 事件记录。Outbox 仍保持至少一次投递，允许重复投递 / 处理尝试；宿主处理器必须按 `ApprovalEvent.Id` 对外部副作用去重，不承诺跨系统 exactly-once |
  | S12 | 在相同操作人、Kind、Scope、OperationId 下修改目标用户、原因等内容 | 返回 Conflict；原记录与结果保留，不执行修改后的操作；普通 HTTP 与批量显式传键入口均验证 |
  | S13 | 批量交接首实例提交后故障中断，再用原键和原内容恢复 | 保留首实例；固定原候选清单，已提交项不重做，未完成项继续，最终 Count / Skipped 正确且 Reassign 日志不重复；恢复前新增的候选实例不混入原计划 |
  | S14 | 分别终止 Running / Returned 实例，覆盖并行分支与加签任务 | 所有开放任务取消，BusinessSlot 释放，完成信息正确，原 OnTerminatedAsync / 默认回调执行，可信操作人和原因进入审计，Completed 事件及配置的通知随事务落库 |
  | S15 | 注入回调、保存或并发失败；批量在后续实例失败 | 普通命令 / 当前失败实例不留部分任务、日志、Operation、Outbox 或宿主业务变更；批量之前已成功实例保留；失败 scope 不可复用。外部消息副作用按 Event.Id 去重单独验证 |
  | S16 | 改派给停用用户、不允许自审时的发起人、同节点当前开放任务的审批人或岗位要求下的不合资格用户；改派 Resubmit；缺少岗位改派 / 终止必填原因 | 对应原业务检查拒绝；无任务转移、审计或通知泄漏；Waiting / Suspended 成功改派保持状态和时限，不提前通知 |
  | S17 | 在带键外层委托执行 Core RetryAsync 后故意回滚；再验证成功提交 | 回滚时 Outbox 原状态及 Attempts 保留，不执行 AfterCommit 唤醒；提交后才观察 Core 登记的唤醒和 Pending / Attempts=0，不要求 LastError 已清空 |
  | S18 | 无键直接调用普通 Core 命令、仅包裸 UnitOfWork，或把批量改派放入外层原子事务 / 脏 scope | 普通命令 OperationIdRequired；批量入口拒绝加入外层工作单元，宿主原有跟踪实体不被清空，无审批部分写入 |
  | S19 | 读取无效定义、缺失节点、BusinessSlot 不一致、Migration / Model Drift 或后台异常诊断 | 读取过程无审批 / 宿主业务写入，无修复命令或额外 Worker；正确显示样本截断及 DataAvailable，不能伪报健康 |
  | S20 | 普通 HTTP 与批量交接缺键、空白键、过长 / 控制字符键、多个 Header 值 | 按原协议拒绝且无业务变化；不把批量排除过滤器误当作允许无键 |
  | S21 | 响应丢失 / CommitOutcomeUnknown 后重发原请求；撤销结果读取权限再重放 | 原键可恢复已提交结果而不重做；权限撤销后按现有读取 / 资源规则拒绝结果读取，不因旧键泄露资源 |
  | S22 | HTTP Outbox Retry 保存后，在外层 Commit 前设置屏障，并分别完成提交 / 回滚 | 提交前无信号；回滚后 Outbox 原状态、次数、完成时间、并发令牌及 Operation 数量不变且无信号；成功提交后 Pending / Attempts=0 与 Operation 一起落库并唤醒一次；同键重放状态不变、不重复唤醒；不以 Core 的 S17 通过代替整条 HTTP 路径验收 |

  已核对的测试可作为补齐与回归入口，需在这些现有项目中补足矩阵，而非建立新的安全测试系统：

  | 测试文件 | 已有可复用用例 / 范围（不代表矩阵全覆盖） |
  | --- | --- |
  | [Core PermissionTests](../tests/Approval.Core.Tests/PermissionTests.cs) | `DirectManagement_DeniedWithoutMutations_ThenAllowedSeparately`、`DataScope_AppliesBeforePaginationCountAndBulkReassign`、`BulkGrant_DoesNotOverridePerTaskDenial`；管理权限、数据范围与逐任务拒绝 |
  | [HTTP PermissionEndpointTests](../tests/Approval.AspNetCore.Tests/PermissionEndpointTests.cs)、[DiagnosticsEndpointTests](../tests/Approval.AspNetCore.Tests/DiagnosticsEndpointTests.cs)、[AdminEndpointTests](../tests/Approval.AspNetCore.Tests/AdminEndpointTests.cs) | 权限撤销、端点权限元数据、诊断访问及维护路由缺失、管理入口拒绝 / 改派 / 终止；仍需按矩阵补足数据库与副作用断言 |
  | [OutboxRetryEndpointTests](../tests/Approval.AspNetCore.Tests/OutboxRetryEndpointTests.cs) | `OutboxRetry_CommitBoundaryControlsWakeupAndState(false / true)`：S22 的成功提交 / 提交失败屏障、Outbox 与 Operation 最终状态、回滚后原键恢复、同键重放不重复唤醒；使用现有内存 SQLite 宿主，未启动定时 Worker |
  | [StrictCommandBoundaryTests](../tests/Approval.Core.Tests/StrictCommandBoundaryTests.cs)、[OperationEndpointTests](../tests/Approval.AspNetCore.Tests/OperationEndpointTests.cs) | `KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion`、`BulkReassignHeader_ReplaysOriginalSummary_AndStrictModeRejectsMissingKey`；原键恢复、内容冲突与 HTTP 显式批量传键 |
  | [AdminTests](../tests/Approval.Core.Tests/AdminTests.cs)、[OperationTests](../tests/Approval.Core.Tests/OperationTests.cs)、[BulkReassignmentConflictTests](../tests/Approval.Core.Tests/BulkReassignmentConflictTests.cs) | 并行终止与默认回调、加签取消、改派时限 / 自审 / Waiting、逐任务日志、拒绝外层事务、Conflict 时保留成功实例 |
  | [NotificationTests](../tests/Approval.Core.Tests/NotificationTests.cs)、[ActionRollbackTests](../tests/Approval.Core.Tests/ActionRollbackTests.cs) | `Delivery_is_marked_failed_after_max_attempts_and_can_be_retried_manually`、事件处理器本地写入回滚、成功确认及完成回调失败回滚 |
  | [ApprovalWriteProtectionTests](../tests/Approval.Core.Tests/ApprovalWriteProtectionTests.cs)、[DiagnosticsAssigneeTests](../tests/Approval.Core.Tests/DiagnosticsAssigneeTests.cs) | 直接实体 / 批量 / SQL 写入被拒绝、Operation 防篡改、诊断复用处理人资格限制 |
  | [AtomicDatabaseContractTests](../tests/Approval.Database.Tests/AtomicDatabaseContractTests.cs) | `AtomicOutbox_RetryDuringDeliveryIsRejected_AndConcurrentDispatchDoesNotDuplicate`、`AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy`，以及提交未知恢复；服务数据库仅按现有 GitHub Actions 矩阵执行，不在本地启动数据库专项 |

  **2026-11-05 HTTP 重试唤醒定点验收记录**（基于上述 HEAD 的未提交工作区，不是 GitHub Actions 验收）：

  - 修复前新增 `OutboxRetry_CommitBoundaryControlsWakeupAndState` 两个用例，使用 `dotnet test tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj --configuration Release --filter FullyQualifiedName~OutboxRetryEndpointTests --verbosity minimal` 实际复现：通过 0、失败 2，均在提交屏障处发现提前唤醒。
  - 删除 Endpoint 额外 Notify 后定点回归通过 2、失败 0、跳过 0；随后使用可控 TimeProvider 精确断言重新排队时间。最终运行 `dotnet test tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj --configuration Release --no-restore --verbosity minimal`：通过 95、失败 0、跳过 0，包含上述两个用例；S22 的提交前、回滚后、成功提交后与重放断言均实际执行。
  - 契约工具通过 `dotnet build eng/Approval.ContractTool/Approval.ContractTool.csproj --configuration Release --no-restore -p:BuildProjectReferences=false` 编译，复用已构建的当前审批程序集及未修改的示例程序集；已核对工具目录中的 Approval.AspNetCore.dll 与当前构建 SHA-256 相同。运行 `pnpm --dir web contracts:check --no-build` 通过，全部公共 API / OpenAPI / JSON Schema / TypeScript 基线一致，未运行 contracts:update。
  - `pwsh -NoProfile -File ./eng/verify.ps1 -Scope Backend` 首次在解决方案构建阶段失败：运行中的 Certificate.Api（PID 56224）锁定 Release DLL，返回 MSB3027 / MSB3021；该次门禁未进入解决方案测试、Model Drift 或 ReferenceHost 演练。随后按用户明确指示核对并关闭该进程，使用同一命令重跑完整 Backend 门禁通过，退出码 0。
  - 重跑结果：Release 构建 0 警告、0 错误；Core 538、HTTP 95、数据库项目 61、Certificate 宿主 59 个测试全部通过，合计通过 753、失败 0、跳过 0；9 个 Migration / Model Drift 检查通过；ReferenceHost 演练通过 27 项检查，报告位于 `output/reference-host/2983cd9248cd473dac47b761ceb42419/smoke.json`。此记录仍对应上述 HEAD 的未提交修复工作区。
  - 本地数据库测试与演练使用 SQLite；MySql / SqlServer / PostgreSql 本次只完成模型一致性检查，服务数据库专项及本次 SHA 的 GitHub Actions 均未执行。

  验收记录必须注明新增 / 复用用例、实际执行命令与对应 SHA，区分仅编译、实际测试、失败及未验证 Provider。完成 P4 安全验收前逐项记录 S01～S22 的证据，不得以历史绿色 CI 或本次定点回归代替整个矩阵。

- [x] P5：全量审查 SDK 与 HTTP 写调用，键改为必需，修复批量改派 UI；覆盖未知结果、同键异内容、身份切换、存储不可用和重复点击。
- [x] 更新生成契约并审阅；执行 Backend / Frontend / Contracts / Packages 门禁，整理实施报告。

每个工作包完成后单独验证并记录命令与实际结果，最后整体回归。仅编译不能记作测试通过。

## 回滚与风险

共享导航变更影响正常运行，须先验证正式执行回归再接入模拟。公开 API、HTTP、SDK 变化同步全部仓库内消费者及生成基线。逐包保持可编译；回滚只撤回本任务的对应改动，不重置工作区或数据库。不自动提交或推送；服务数据库执行留给本次提交 SHA 的 CI，不能引用历史结果。

实施结果、命令、测试数量与未执行的 CI 范围见 [实施报告](../docs/approval-productization-report-2026-11-04.md)。包脚本的浏览器选择器失败已修正，并使用相同最终包重新通过全部 7 个浏览器用例。
