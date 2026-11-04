# 异步校验、统一严格幂等与统计预算

> 历史记录：本文保留该轮次当时的实现和验证事实。旧构造器、消息/幂等兼容、JSON 缺省版本及初始化迁移的当前状态，以[开发期兼容清理报告](unreleased-compatibility-cleanup.md)为准；本文不构成保留旧软件接口的要求。

本轮按 `plans/2026-11-04-async-validation-idempotency-statistics.md` 实施。未修改数据库模型、迁移、包版本或审批运行协议。

## 行为变化

- 删除 `ApprovalDefinitionService.Validate`；调用方使用 `await ValidateAsync(flow, ct)`。授权先完成，随后执行原校验扩展；拒绝、调用方取消、权限超时和扩展写边界保持。
- 删除 `ApprovalOptions.RequireOperationId` 开关及命令无键分支；缺少稳定键的受管理写操作统一返回 `OperationIdRequired`。批量改派只保留带键入口；HTTP 生成契约将幂等头标为必填。只读、定义校验、显式系统能力及文件上传维持原边界。
- 统计实例按 CreatedAt、Id 选择最早 100,000 条，关联任务在 SQL 中 join 同一受限实例子查询，消除同时间戳越界读取。任务必要列流式累计，不再创建完整 TaskRow 列表及二次过滤副本。
- 任务/积压在数据库查询中限制为预算 + 1 行；第预算 + 1 行触发 `StatisticsLimitExceeded`，HTTP 400，整次请求失败。原 Truncated 仅表示实例截断，不表示任务或积压的部分结果。
- 积压仍按 Ordinal 分组，保留大小写、重音、尾空格、用户/角色同名区别；统计界面重查时清除旧数据，失败后不展示上一请求指标。

## 宿主配置

```csharp
services.AddApprovalWorkflow<AppDbContext>(options =>
{
    options.Statistics.MaxTaskRows = 250_000;
    options.Statistics.MaxBacklogRows = 100_000;
});
```

两个预算必须大于零且小于 int.MaxValue，注册时验证。证书示例也绑定 `Approval:Statistics:MaxTaskRows` / `MaxBacklogRows`。

任务超限可缩短时间范围或按流程筛选；当前积压不受日期限制，应按流程筛选或使用宿主报表。预算限制应用读取和聚合量，不保证数据库执行计划没有物理扫描，也不表示已经完成生产规模压测。

## 回归证据

| Requirement | Evidence |
| --- | --- |
| 异步授权等待、允许、拒绝 | `AsyncValidationTests.ValidateAsync_WaitsForPermissionWithoutRunningValidation`：权限 Task 未完成时方法返回未完成 Task，扩展调用为零；允许后返回精确校验错误，拒绝返回 Forbidden |
| 调用方取消与权限超时 | `AsyncValidationTests.ValidateAsync_CancellationAndPermissionTimeoutDoNotRunValidation`：取消传播，权限超时映射 AuthorizationUnavailable，两条路径均不调用扩展 |
| 默认 HTTP 严格幂等 | `OperationEndpointTests.DefaultPolicy_RejectsUnkeyedWriteWithoutSideEffects_AndReplaysKeyedWrite`：缺键无审批变更/Outbox/操作残留，同键只提交一次，不同内容 Conflict，撤销读取权限后 Replay 返回 Forbidden |
| SDK 严格幂等与非法键 | `StrictCommandBoundaryTests.DefaultPolicy_DirectStartRequiresKey_AndKeyedStartReplays`、`CommandExecutor_InvalidKeyRejectsBeforeDelegateAndPersistence` |
| 宿主配置绑定 | `CertificateOperationsTests.HostBindsStatisticsBudgets` |
| 空集合、恰好限额及超限 | `StatisticsBudgetTests.Statistics_ExactBudgetReturnsCompleteResults_OverflowRejects`：分别验证任务与积压，检查 SQL 上限参数和受限实例 JOIN；`Statistics_InvalidBudgetsFailRegistration` 验证非法配置 |
| 同时间戳实例边界 | `StatisticsBudgetTests.Statistics_InstanceCutoffWithEqualTimestampsDoesNotReadExcludedTasks`：100,001 条实例，未选实例任务不会消耗关联任务预算；全局积压仍包含它 |
| 指标、分组及 Top 10 | `DatabaseContractTests.Statistics_StreamsNodeMetricsAndOrdinalBacklogAtExactBudget` 验证平均时长、系统/重新提交任务排除、超时边界及 Ordinal 分组；`Statistics_BacklogReturnsTopTenByCountThenOldestArrival` 验证前十排序 |
| 授权/流程筛选先于 SQL 预算 | `DatabaseContractTests.Statistics_BudgetsAreAppliedInSqlAfterScopeAndFlowFilters`：SQLite、MySQL、SQL Server、PostgreSQL 本轮均实际通过 |
| HTTP 错误及前端旧数据 | `OperationEndpointTests.Statistics_OverflowReturnsProblemDetailsInsteadOfPartialMetrics`；`useAsyncData.test.ts` 的“统计重新查询超限时清除上一次结果并展示错误” |

流程算法夹具使用显式系统作用域；真实用户的权限、治理、HTTP 和数据库集成测试使用带键命令。缺键回归构造原始请求，验证拒绝且无副作用。统计服务仅保留接收宿主 ApprovalOptions 的构造器。

底层陈旧跟踪图与日志竞争用例使用只授予所需能力的显式系统测试作用域，以保留原 DbContext 的预读状态；带键预约会在 SQLite 提前串行化写入。真实用户的带键竞争、重放和下游任务唯一性由 `Parallel_ConcurrentBranchCompletionAndReplay_CreateOnlyOneDownstreamTask` 覆盖，四 Provider 均通过。

## 续作验证（2026-11-04，已完成）

本轮完成删除开关、无键入口及旧统计构造器；未提交、推送或发布，未修改数据库模型及 Migration。

| Requirement | Evidence |
| --- | --- |
| 缺键、空白、超长及控制字符在业务委托前拒绝，用户与系统 executor 均无副作用 | `StrictCommandBoundaryTests.CommandExecutor_InvalidKeyRejectsBeforeDelegateAndPersistence`，5 个具名安全用例 |
| 直接 SDK 写入要求稳定键；裸工作单元不能绕过 | `DefaultPolicy_DirectStartRequiresKey_AndKeyedStartReplays`、`StrictMode_PublicUnitCannotReplaceAKeyedCommand` |
| HTTP 缺键拒绝，同键只提交一次，内容冲突与权限撤销仍生效 | `OperationEndpointTests.DefaultPolicy_RejectsUnkeyedWriteWithoutSideEffects_AndReplaysKeyedWrite` |
| 批量改派保留原结果与中断恢复 | `KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion`，完整执行和中断恢复两种情况 |
| 陈旧日志竞争、实例令牌冲突与完整回滚 | `SameTask_SynchronizedApprovalsCommitExactlyOneDecisionAndRollbackLoser`、`OriginalStaleApproval_PreservesCommittedWinnerWithoutLeakingDatabaseException`、`RealApproval_RejectsStaleStampAndRollsBackSideEffectsOnEachProvider`；四 Provider 均通过 |

- `eng/verify.ps1 -SkipRestore` 完整执行成功，退出码 0；Release build 0 warnings / 0 errors。
- 后端 Passed 641 / Failed 0 / Skipped 0：Core 443、HTTP 83、SQLite Database 56、Certificate 59。
- 前端 UI 181、demo 6、边界 7 用例全部通过；typecheck、库/demo 构建、API/OpenAPI/TypeScript 契约检查通过。
- 九项模型漂移检查、ReferenceHost 集成、四个 NuGet 包与 npm 包的独立消费者验证通过，Core-only 消费者 18 checks 通过。
- 浏览器 5 passed / 0 failed / 0 skipped，JSON stats 为 expected=5、unexpected=0、skipped=0、flaky=0。

| Provider | Passed | Failed | Skipped | 本轮报告目录（相对仓库根） |
| --- | ---: | ---: | ---: | --- |
| MySQL | 56 | 0 | 0 | `output/database-tests/MySql/60d27e3dcbe441cbba24aa07dbf0941b` |
| SQL Server | 56 | 0 | 0 | `output/database-tests/SqlServer/859f2e14b8de48a9bb953a23d472d4af` |
| PostgreSQL | 56 | 0 | 0 | `output/database-tests/PostgreSql/5fe168f8d3fd455ab1d6a9fc8c8adf34` |

三份 TRX 均已解析，manifest 的 validated=true / testExitCode=0，临时测试容器均已由脚本清理。汇总为 `output/strict-database-evidence.json`。

完整成功日志为 `output/strict-verify-final.log`，真实数据库日志为 `output/strict-mysql.log`、`output/strict-sqlserver.log`、`output/strict-postgresql.log`。独立消费者及浏览器证据位于 `output/engineering/consumer-5d93f35dcd1f4f498f21d9a3ad7ab7e0`，包含 `verification.json`、`browser/results.json` 和 `browser/passed.json`。

本轮首次完整运行的 `output/strict-verify.log` 保留了调用迁移未完成时的失败；修复后重新执行整个 verify 成功，最终结果以上述 final 日志为准。前一轮 Docker 不可用的限制已解除。

复现本轮验证（仓库根目录，已有还原依赖及 Release 构建）：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
$env:DOTNET_PROCESSOR_COUNT = '4'
pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql' -SkipRestore -SkipBuild
```

全新环境可去掉 SkipRestore / SkipBuild，让脚本还原依赖和构建。

## 前一轮验证（统一严格幂等变更前）

- Release solution build：0 warnings / 0 errors。
- 后端：Passed 637 / Failed 0 / Skipped 0（Core 439、HTTP 83、SQLite Database 56、Certificate 59）。
- 前端：UI 181、demo 6、边界检查用例 7，全部通过；typecheck、库/demo 构建及契约检查通过。
- 九项迁移模型漂移检查、ReferenceHost 27 checks、独立 NuGet/npm 消费者构建及 Core-only 18 checks 通过。
- 浏览器最终 5 passed / 0 failed / 0 skipped。完整脚本最初在协议拒绝用例失败：它缺少幂等键，先被严格默认策略拒绝。补充稳定键后仅重跑浏览器阶段成功；后端、前端和包产物均未发生后续修改，不重复运行已通过阶段。
- 本机 `docker version` 报 dockerDesktopLinuxEngine pipe 不存在，MySQL / SQL Server / PostgreSQL 的真实数据库执行尚未完成。不以 SQL 翻译断言或历史 CI 替代真实数据库运行。

根目录完整验证命令：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
# 本轮验证的子进程设置了 DOTNET_PROCESSOR_COUNT=4，控制并行资源占用。
pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore
```

本轮完整日志：`output/async-statistics-verify.log`。需要重新还原依赖时去掉 `-SkipRestore`。Docker daemon 可用后分别执行 `eng/verify-databases.ps1 -Provider MySql`、`SqlServer`、`PostgreSql`。

原完整日志保留浏览器初次失败，成功复跑命令与日志为：

```powershell
node './web/scripts/browser-smoke.mjs' --manifest './output/engineering/consumer-1c2aaa8c6cdf446698307110b3673b17/verification.json'
```

成功日志 `output/async-statistics-browser.log`；JSON 报表及 passed.json 位于同一消费者的 `browser/` 目录，最终报表应为 expected=5、unexpected=0、skipped=0。

## 调用规则与回滚

服务级校验使用异步入口；HTTP/SDK 写调用提供稳定键，重试复用原键和原内容。项目处于开发阶段，不提供旧版本兼容分支。所有预算内响应结构保持。无数据库结构变化，回滚无需数据迁移；不扩大预算，不操作用户数据库。
