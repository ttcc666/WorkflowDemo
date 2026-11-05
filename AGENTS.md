# AGENTS.md — WorkflowDemo 项目开发指南

本文件是仓库级开发指引，适用于在本仓库工作的 AI coding agent 和开发者。若子目录以后增加更具体的 `AGENTS.md`，在该子目录内同时遵守更具体的规则。

## 1. 项目定位与当前阶段

- 本项目是一个**可嵌入现有业务系统的通用审批引擎**，基于 .NET 10、EF Core，提供可选 ASP.NET Core HTTP 集成和可选动态表单模块。
- 当前处于开发阶段，尚未正式发布。仓库中的 `samples/` 是集成示例和验证消费者，不代表已有外部生产宿主；目前没有需要维护的外部宿主兼容负担。
- 因此，可以在发布前主动调整不合理的公共 API、JSON 契约、数据库初始模型和内部设计。**不要为了兼容从未发布、没有外部使用者的旧实现而保留无用兼容层。**
- 破坏性调整仍必须是有意的：先说明改变了什么，再同步更新所有仓库内消费者、测试、契约、示例和文档。不得把“删除兼容代码”当成改变审批业务语义的理由。
- 首要目标是正确、简单、低耦合、低冗余、易于宿主集成和长期维护。优先做最小充分设计，不为假设中的未来规模预建基础设施。
- 当前产品补齐路线见 `plans/2026-11-04-approval-productization.md`；历史多租户和工作流编排计划不再作为当前开发指令，实施与验收记录保留为历史证据。

## 2. 产品边界与明确非目标

### 产品边界

审批引擎负责流程定义、流程运行、任务与状态流转、审批审计、查询、幂等、并发控制和 Outbox。宿主负责提供：

- 用户身份与可信当前操作人；
- 用户目录、组织架构及用户状态；
- 权限判断及宿主自己的角色 / policy；
- 业务数据与业务生命周期；
- 邮件、短信、企业消息等通知渠道；
- 附件实际内容存储。

Core 不应自行建立宿主用户表、组织架构、角色体系、管理员角色、通知发送渠道或业务数据模型。默认人工授权应保持拒绝；不能因为用户名、角色名或 HTTP/UI 按钮可见就推断用户有权限。

### 当前运行模型

- 当前目标是**单宿主、单应用实例的嵌入式审批引擎**，由宿主提供组织架构和权限。
- 这不代表禁止正常并发 HTTP 请求。数据库事务、乐观并发令牌、唯一约束、幂等键及冲突处理必须继续正确工作。
- 不要自行引入多租户 / TenantId 体系、租约 / Lease、分布式 Worker 选主、分布式调度、额外后台服务、独立消息平台、Redis、MediatR、通用 Repository 或微服务拆分。
- 不要增加审批归档、历史清理、附件删除、数据保留期等后台职责。当前 ApprovalWorker 只负责 Outbox 投递和审批超时扫描。
- 只有明确的产品需求、可验证的业务场景和架构决策，才能改变上述边界；不要因“通用框架通常会有”而添加功能。

## 3. 解决方案结构与依赖方向

主要项目：

- `src/Approval.Core`：审批领域、流程执行、任务协调、查询、事务、持久化边界、权限抽象、事件和 Outbox。
- `src/Approval.AspNetCore`：ASP.NET Core 身份上下文、能力策略、HTTP Endpoint、ProblemDetails 映射。
- `src/Approval.Forms`：可选动态表单 Schema、校验、公式、草稿、字段权限和附件引用。
- `src/Approval.Forms.AspNetCore`：可选 Forms HTTP 与数据操作适配。
- `tests/`：Core、HTTP、数据库 Provider 和 Certificate 示例宿主测试。
- `samples/`：ReferenceHost、Certificate 业务示例及其 API 宿主；用于演示和集成验证，不是框架核心，也不是外部生产宿主。
- `contracts/`：公共程序集 API 基线、OpenAPI、流程和表单 JSON Schema。
- `web/`：Vue 3 / TypeScript UI、SDK、设计器和 Demo。
- `eng/`：验证脚本、独立包消费者、契约工具和集成演练工具。

依赖原则：

- Core 不依赖 ASP.NET Core、Forms、宿主项目或具体身份 / 权限 / 通知 / 文件存储提供者。
- AspNetCore 依赖 Core；Forms 依赖 Core；Forms.AspNetCore 组合 Forms 与 AspNetCore。
- 不要为了调用方便把可选 Forms 类型或 HTTP 类型引入 Core。
- Forms 是可选模块。仅使用基础审批的宿主不应被迫安装 Forms 或创建 Forms 表。
- 宿主扩展通过现有 Abstractions、Context、Snapshot 和 Provider 接口进行；不要把 EF 跟踪实体作为新的宿主扩展契约。

新增类型或抽象前，先检查现有 Service、Coordinator、Bridge、Context、Provider 和 Extension 是否已能承担职责。不要仅为了拆文件、转发方法或“看起来分层”而增加空壳服务。

## 4. 审批业务语义必须保持

除非需求明确要求改变业务规则，否则重构不得改变以下行为：

- 或签、会签、依次审批；
- 条件分支、并行分支与 Join 汇聚；
- 加签、转交、管理员改派；
- 退回、重新提交、撤回、终止；
- 抄送、逐人已读、审计日志；
- 超时提醒及显式允许时的超时自动决策；
- 流程定义发布后不可变，运行实例绑定其发布版本；
- Forms 字段权限、隐藏字段投影、公式计算和附件访问授权。

涉及状态机或流程推进的修改，必须追踪正常、拒绝、退回、取消、重复调用、并行分支、嵌套分支和并发路径。不要只验证“主流程成功”这一条路径。

不要通过删除测试、放宽断言、改变测试竞争场景或吞掉异常来获得绿色 CI。并发测试应使用显式同步，不依赖随机概率或 `Task.Delay` 猜测时序。

## 5. 持久化、事务与写入保护

- 宿主使用自己的 `DbContext`。在 `OnModelCreating` 中调用 `ConfigureApproval()`；可选 Forms 再调用 `ConfigureApprovalForms()`。DI 注册应按项目现有方式安装 `UseApproval()` 拦截器。
- 宿主负责创建和应用 EF Core Migration。不要在运行时自动迁移宿主数据库。
- 业务数据、审批实例 / 任务、审计、幂等操作结果和 Outbox 必须遵循现有同一 DbContext / UnitOfWork 事务边界。
- 审批实体由其所属内部服务写入。宿主不得通过直接修改跟踪实体、`Attach` 伪造实体、`ExecuteUpdate`、`ExecuteDelete`、原生 SQL 或公开 UnitOfWork 绕过写入授权。不要移除、绕开或弱化 `UseApproval()` 及其保存、查询、命令和事务拦截器。
- 宿主业务实体仍可按正常 EF Core 方式保存。不要让审批写保护误伤宿主业务实体或宿主已有的 `SaveChanges` override。
- Engine 的 Flush 不等于事务 Commit；不要提前提交、擅自创建第二个 DbContext 事务，或假设两个 DbContext 会自动共享事务。
- 操作失败后当前 scope / 实体图按现有约定作废；不要捕获异常后继续复用失败的 UnitOfWork 或 DbContext。
- 同步业务 / 生命周期回调不得自行 `SaveChanges`、Commit 或修改审批实体。事件处理器遵守事件文档中的事务和重入边界。

修改持久化模型时，检查所有相关 Provider 的模型配置、索引、约束、迁移和 Model Snapshot；不能只更新一个数据库 Provider。

## 6. 幂等、并发与重试规则

- 所有受管理的业务写命令都要求稳定 `OperationId`；HTTP 写请求使用 `Idempotency-Key`。不要重新引入可关闭严格幂等的配置或无键写入旁路。
- 一次业务意图生成一个键。超时、断线、提交结果未知或客户端重试时，必须复用**相同键和相同请求内容**；只有明确创建新业务意图时才生成新键。
- 宿主业务写入与审批调用需要原子提交时，使用 `IApprovalCommandExecutor` 的完整委托。在委托传入的 `IServiceProvider` / 新 scope 中解析 DbContext 和 Engine；不要捕获原请求 scope 中的已跟踪实体或 scoped 服务。
- `IApprovalUnitOfWork` 不能替代命令执行器已经预约的稳定操作键。批量改派等逐实例提交操作遵守其独立事务和稳定键规则，不要擅自包装成整批原子事务。
- 保留 `ConcurrencyStamp`、EF 乐观并发令牌、业务唯一索引、日志序号唯一索引和精确冲突识别。不要把所有 `DbUpdateException` / 唯一约束异常都转换成 Conflict。
- 预期并发冲突应转换为正确的业务错误码；无关唯一约束、外键、连接失败和普通 SQL 错误必须保留其真实错误语义。
- `CommitOutcomeUnknown` 不等于确定失败。无法确认 COMMIT 结果时，调用方必须保留原键和原请求恢复结果。

## 7. 事件、Outbox 与外部副作用

- 业务事件在审批事务中写入 Outbox，由当前单个 ApprovalWorker 至少一次投递。
- Core 不直接发送邮件、短信、Webhook 或企业消息。通知规则产生当前事件（例如 `NotificationRequested`），实际发送由宿主 `IApprovalEventHandler` 完成。
- 宿主必须按稳定的 `Event.Id` 对外部副作用去重；不要宣称跨数据库与外部消息系统 exactly-once。
- 事件处理器可按现有约定保存宿主业务数据，但不得修改审批实体、自行 Commit 或重入审批命令。异常和确认遵守现有 Outbox 回滚 / 重试语义。
- `EnableBackgroundServices`、`Outbox.EnableDispatcher` 的含义不同：不要把关闭定时 Worker 等同于关闭 Outbox 投递，也不要添加第二个 Worker。
- 文件上传和宿主文件存储是外部副作用，不属于数据库事务可自动回滚的范围；保留现有补偿和授权边界。

## 8. 流程 JSON、Forms 与公共契约

- 外部流程定义 JSON 必须显式包含当前 `schemaVersion`（当前为 1）。更新流程 Schema 时同步更新后端解析 / 校验、JSON Schema、前端设计器和所有 JSON fixture。
- 已发布定义和运行实例快照不可被后续编辑覆盖。
- Core 只保存可选 Forms JSON 快照，不解释动态表单内部字段。表单校验、字段权限、数据投影和附件访问由可选 Forms 模块负责。
- 修改公共 C# API、HTTP 路由 / DTO、ProblemDetails 错误码、OpenAPI、JSON Schema、TypeScript 类型或 npm SDK 时，检查并同步 `contracts/`、`web/`、`samples/`、`tests/` 和相关文档。
- 只有在有意改变契约时才运行 `pnpm contracts:update`；随后必须审阅生成的全部差异。常规验证使用 `pnpm contracts:check`。不要手工绕过契约基线检查。
- 项目尚未正式发布，允许删除无使用者的旧 API 或旧格式兼容代码；但必须同步所有仓库内消费者并增加当前契约的正向 / 负向测试。不要保留没有实际消费者的兼容重载，也不要在没有需求时随意改变现有宿主可见行为。

## 9. 数据库与迁移约定

支持 SQLite、MySQL、PostgreSQL 和 SQL Server。SQLite 数据库测试包含在默认解决方案测试中；三个服务数据库通过独立 CI 矩阵验证。

- 宿主拥有 Migration 的创建和应用职责。
- 仓库内不同 Provider / Host Context 各有自己的 Migration 与 Snapshot；不得跨 Provider 复制 Snapshot。
- 当前未发布开发阶段按仓库现有初始迁移基线演进。若确需重建初始迁移，先确认所有受影响 Context、设计时工厂、Model Snapshot、样例和迁移测试，并在变更说明中列明。
- 不要把开发数据库重置或删除作为代码修复的一部分；不要对用户数据库执行清库操作。
- 数据库专项脚本会创建并清理隔离的 `approval_test_*` 数据库 / 容器。遵守脚本的数据库名和资源所有权保护，不要修改脚本来允许连接任意数据库。

## 10. 代码与测试工作方式

开始修改前：

1. 检查当前分支、HEAD、工作区和已有未提交改动；不要覆盖用户的工作。
2. 阅读与任务直接相关的架构 / 事务 / 权限 / Forms / 事件 / 数据库文档和调用点。
3. 先定位真实根因和所有调用路径，区分业务缺陷、契约变化与纯重构。
4. 提出最小修改方案；避免顺手扩大范围或重做已经稳定的架构。

实施时：

- 遵守 `.editorconfig`、`Directory.Build.props`、`Directory.Packages.props` 和现有代码风格；目标框架与 SDK 以 `global.json` 为准（当前 .NET 10）。
- 保持 nullable 与现有异步约定；不要用同步阻塞包装异步权限 / IO 操作。
- 优先复用现有抽象和职责边界。没有真实业务需求，不增加新的程序集、包、服务层、缓存、后台 Worker 或基础设施。
- 新增测试应验证外部可观察行为、数据库最终状态、权限和事务边界，而不只是私有方法实现。
- 对安全、授权、幂等、并发、事务和数据迁移修改，必须增加对应负向测试，证明非法路径确实被拒绝且没有部分写入。
- 修改结束后审阅完整 diff，检查是否误改公共 API、状态机、迁移、契约或示例；更新必要文档。

不要仅因任务完成就自动提交、推送、合并或发布；只有用户明确要求时才执行这些 Git 操作。

## 11. 验证命令

在仓库根目录执行。优先使用项目脚本，不要只凭 IDE 编译结果判断通过。

完整门禁：

```powershell
pwsh -NoProfile -File ./eng/verify.ps1
```

按范围验证：

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Scope Backend
pwsh -NoProfile -File ./eng/verify.ps1 -Scope Frontend
pwsh -NoProfile -File ./eng/verify.ps1 -Scope Contracts
pwsh -NoProfile -File ./eng/verify.ps1 -Scope Packages
```

前端单独开发时，在 `web/` 下使用 `pnpm install --frozen-lockfile`、`pnpm check:boundaries`、`pnpm test:boundaries`、`pnpm typecheck`、`pnpm test`、`pnpm build:lib` 和 `pnpm build`。以 `web/package.json` 中脚本为准。

**服务数据库专项按当前仓库约定在 GitHub Actions 执行；不要在本地擅自启动 Docker 或运行数据库专项脚本。** 推送后检查与本次确切 HEAD 对应的 CI：

- `.github/workflows/ci.yml` 的 MySql、SqlServer、PostgreSql 数据库矩阵；
- Windows `verify` Job（包含 Release 构建、SQLite、Model Drift、ReferenceHost、前端、契约、包消费者和浏览器验证）。

CI 结果必须对应本次提交 SHA。不得用其他分支、旧提交或历史绿色 Run 代替本次验收。报告时区分实际执行、仅编译、未执行和失败的检查；不得把 build 成功描述成 tests passed。

## 12. 完成任务时的交付说明

完成代码任务后，简要说明：

- 根因与实际行为变化；
- 修改的关键文件和设计取舍；
- 是否改变公共 API、HTTP / JSON / TypeScript 契约或数据库模型；
- 新增 / 调整的回归测试；
- 实际运行的验证命令与结果（注明本地或 GitHub Actions、对应 SHA）；
- 未完成事项、已知风险和需要用户决定的事项。

如果没有证据证明某个 Provider、场景或测试已经执行，应明确说未验证，不要猜测或夸大结论。
