# 多租户最终收尾审查

基线：检查时最新 `main` / `origin/main` 均为 `e19620205ea2733f39aebbc9283acd80d23331f4`，开始时工作区干净。本报告记录推送前的本地验收；用户随后明确授权提交并推送，远端结果须以新提交 SHA 的 CI 为准。

## 实际问题与最小修复

发现一个实际配置缺口：`TenantDirectory` 原先只用 `Ordinal` 判断 ID 重复，并只限制空 ID 和长度。因此 `alpha` / `ALPHA` 可以被注册成两个租户，` alpha`、`alpha `、Unicode 空格及内嵌控制字符也被接受。这与本次租户标识规范不一致，迁移到比较规则不同的数据库时容易引入歧义。

仅修改 `samples/Approval.MultiTenantHost/TenantScopes.cs` 中目录构造校验：

- 拒绝 `OrdinalIgnoreCase` 相等的重复租户 ID。
- 拒绝空白/控制字符，保留 1–64 长度约束。
- 保留合法现有 ID，包括大写及 Unicode；不 Trim、不改大小写、不改写数据库。
- 可信 claim 与目录仍作精确匹配；目录重复检查更严格不等于允许请求使用大小写别名。

示例的 SQLite 分区列使用默认 BINARY 比较，已有模型与精确查询一致，无须改变 collation 或 Migration。自定义宿主接入 MySQL / SQL Server / PostgreSQL 时仍需用实际 ID 集合核对自己的 collation（包括大小写、重音、尾部空格），Core 不接管宿主目录或统一转换其标识。本轮未发现仓库已映射模型需要调整数据库配置的证据。

## 已检查且保持不动

| 路径 | 结论与证据 |
| --- | --- |
| `ApprovalPartitionGuard` / `ApprovalPersistence` | 仍校验 Context 固定范围、保存值/原始值、查询根、批量 setter 和过滤器；同步/异步共用规则。原有 `PartitionSecurityTests` 随完整门禁执行 |
| `ApprovalPartitionModelExtensions` | 复合索引、候选键及 FK 已纳入分区；未分区引用方被拒绝，GUID 主键不变 |
| `ApprovalCommandExecutor` | 捕获租户和操作人，新尝试使用新 scope / Context；新增实际写入后故障重试测试证明原租户和幂等结果保持 |
| `TenantDatabase` / `TenantIdentity` | Context 固定值、命令级范围检查、可信 claim 和用户启用状态已正确；Header / Query / DTO 不用于切换租户 |
| 业务回调 | 注入其他租户 Context 或在当前 Context 上切换 ambient 范围时，已有保护拒绝，原事务回滚；新增 HTTP 审批回归测试，无需改 Core |
| Outbox / Timeout / Worker | 通过现有 Runner 逐租户进入范围并新建 scope；异常继续其他租户；现有后台、恢复、并发测试继续执行 |
| Core ADO.NET | `ApprovalTransaction` 的 SQLite deferred transaction、模型/错误识别等可信基础设施使用保留 |
| 宿主 ADO.NET | 受控迁移、数据库集成仍可使用；不新增连接框架或全局禁令 |
| 租户可触达的内置 SQL Action | 已在分区流程中打开连接前拒绝；新增实际 HTTP 流程证明拒绝且没有部分写入。单租户 `SqlWorkflowActionTests` 保留正常 SQL 集成 |

本轮未发现需要修改审批状态机、事务、幂等、Persistence 分层、公共 API 或主键设计的新问题。

## 修改文件

| 文件 | 目的 |
| --- | --- |
| `samples/Approval.MultiTenantHost/TenantScopes.cs` | 租户目录 ID 校验；唯一生产代码改动 |
| `tests/Approval.AspNetCore.Tests/MultiTenantSecurityTests.cs` | 在现有宿主 fixture 上增加 22 个执行用例，无新测试框架 |
| `docs/host-data-partitioning.md` | 精确 ID / collation 语义，可信 ADO.NET 与租户可触达扩展的区别 |
| `samples/Approval.MultiTenantHost/README.md` | 示例 ID 约束及不自动转换说明 |
| 本报告 | 验收范围、证据与合并建议 |

## 测试

新增 7 个测试方法 / **22 个执行用例**。配置负向测试核对输入未被规范化；数据库场景验证 HTTP 结果、真实持久化状态、审批日志、操作键和其他租户数据。

| Requirement | Evidence |
| --- | --- |
| 大小写冲突拒绝 | `TenantDirectory_RejectsCaseEquivalentIds`（3 项） |
| 空白/控制字符/空值/超长拒绝且不转换 | `TenantDirectory_RejectsInvalidIdsWithoutNormalizing`（8 项） |
| 合法已有 ID 保持原值 | `TenantDirectory_PreservesValidExistingIds`（4 项，含 Unicode 和 64 字符边界） |
| 非规范认证 claim 被拒绝，双方数据完整 | `TenantIdentity_RejectsNoncanonicalClaimsAndPreservesStoredRows`（3 项） |
| 错误租户业务回调拒绝并回滚 | `BusinessHandler_WrongTenantRejectsAndRollsBackApproval`（2 项） |
| 重试固定租户、新 Context、原键重放不重复 | `WorkflowRetry_KeepsOriginalTenantAndUsesFreshContext`（1 项） |
| 租户请求触发 SQL 动作，在开连接前拒绝 | `SqlAction_TenantRequestRejectsBeforeOpeningConnectionAndRollsBack`（1 项） |
| 查询隔离、跨租户修改/删除、批量写入与过滤器 | 原有 `TenantDatabase_AttacksAreRejectedAndBothExpensesRemainIntact`、`TrackedWrites_RejectForeignPartitionWithoutPartialWrites`、`BulkUpdate_RejectsPartitionSettersAndPreservesBothTenants`、`BulkBusinessWrites_KeepFiltersAndCannotTouchBravo`、`BulkFilterRemoval_RejectsAndPreservesBothTenants` |
| 同业务 Key 与并发执行 | 原有 `SharedDatabase_IndependentKeysReplayAndApprovalOnlyChangesOwnTenant`、`ConcurrentCommands_PreserveTenantAndConvergeForSameOperation` |
| 后台切换、异常恢复、嵌套/异步隔离 | 原有 `BackgroundScopes_UseIndependentContextsRestoreAndRetryOriginalTenant`、`Outbox_FailedTenantDoesNotBlockOtherTenantAndReceiptsDoNotDuplicate`、`TenantScope_DisposeInChildFlowCannotPreventParentRestoration` |
| FK / 唯一约束 / Provider 模型 | 原有 `PartitionModel_MigrationRoundTripEnforcesTenantConstraints`、`PartitionModel_AllProvidersGenerateCompositeConstraintMigrations` |

复现阶段 `ids-before.trx`：18 项中 8 failed / 10 passed / 0 skipped，失败对应大小写冲突与空白/控制字符。最终专项 `final-targets.trx`：**22 passed / 0 failed / 0 skipped**。重试 fixture 首次将多次 Flush 误计成多个尝试，已改为每个 Context 注入/观测一次，没有改变重试次数或放宽断言。

### 验证命令与状态

在 `D:\Study\CSharp\Workflow` 执行：

```powershell
pwsh -NoProfile -File './eng/verify.ps1'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
```

- 完整本地门禁：**exit 1，未通过**。后端、迁移漂移、ReferenceHost、前端类型检查通过，随后 React 既有用例 `FormDesigner.mobile.test.tsx` / `mobile field selection opens an inspector and edits survive closing and reopening it` 在默认 5000ms 超时，导致脚本中止。本轮没有修改前端，单独按原始超时复验该用例仍失败；没有放宽时限、删除断言或跳过用例。后续检查单独继续执行，不将其通过结果替代完整门禁失败。
- Release 构建：0 warning / 0 error。后端：HTTP 223、Core 792、SQLite 数据库契约 106、Certificate API 59，共 **1180 passed / 0 failed / 0 skipped**。相比基线增加的 22 项全部执行。所有 Context model drift 通过，ReferenceHost smoke 27 项通过。
- 前端单测：shared 17、Vue UI 256、React UI 155、Vue Demo 6、React Demo 5 项通过；React UI 1 项超时。总计 **439 passed / 1 failed**。两个 Demo 测试因原脚本提前停止而单独补跑。
- 前端构建：`pnpm build:lib`、`pnpm build:react-lib`、`pnpm build`、`pnpm build:react` 全部 exit 0。`eng/verify.ps1 -Scope Contracts -SkipRestore` exit 0，全部契约基线匹配。见 `contracts.log` 与 `post-stop-builds.txt`。
- `eng/verify-packages.ps1 -SkipBuild -RunBrowser`：exit 0，独立 NuGet/Vue 消费者通过，包浏览器 **12 passed**，包括正常单租户 SQL / HTTP / 通知动作；见 `packages.log`。
- `pnpm verify:react-package --skip-build`：exit 0，独立 React 包消费者通过；见 `react-package.log`。
- `pnpm test:e2e:packed`：exit 0，**68 passed**；见 `packed-e2e.log`。后续门禁均已完成，但不改变原始完整门禁 exit 1 的结论。
- 三条服务数据库命令：均已调用，均在 `docker info` 阶段 exit 1；Docker Desktop Linux Engine named pipe 不存在，**本轮数据库测试未执行**。没有启动/清理用户数据库，没有将基线 CI 通过当作本轮结果。
- 日志和 TRX：`output/reviews/partition-final/`。
- 基线 `e196202` 的远端 CI 已通过三数据库；本报告不以基线结果替代本轮验收。用户授权推送后，需检查新提交 SHA 对应的 CI。

## 接入影响与剩余边界

单租户接入、正常 SQL/ADO.NET 集成不受本轮修改影响。现有合法多租户 ID 不改变；如目录中原有大小写冲突或含空白/控制字符，宿主启动会明确拒绝，需宿主人工解决目录配置，不能静默改写生产 ID。无公共 API、HTTP/JSON/TypeScript、数据库 DDL、Migration、依赖或状态机变化。

宿主仍是可信代码边界。租户可触达的自定义扩展必须使用捕获的租户范围，不得把用户输入当作连接、原生 SQL 或其他租户身份；不会把任意持有数据库凭证的宿主代码视为可由 EF 拦截器隔离的不可信沙箱。本次不强制 RLS，不把可信基础设施正常使用 ADO.NET 作为漏洞或合并阻塞。

建议：当前可人工审查此最小修复，**暂不直接合并**；需解决或在目标 CI 中复核前端既有超时，并完成包含本轮修改的标准 CI 三数据库矩阵。没有发现需要扩大多租户架构修改范围的证据。
