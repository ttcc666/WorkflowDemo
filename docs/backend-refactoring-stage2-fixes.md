# 后端渐进式重构——第二阶段缺陷修复与收尾

> 历史记录：本文保留该轮次当时的实现和验证事实。旧构造器、消息/幂等兼容、JSON 缺省版本及初始化迁移的当前状态，以[开发期兼容清理报告](unreleased-compatibility-cleanup.md)为准；本文不构成保留旧软件接口的要求。

基线 `73559be`（最后代码提交 `f6342b9`）；分支 `codex/backend-progressive-refactoring`。本次仅修复两个 P1 和一个 P2。数据库专项通过 GitHub CLI / Actions 执行，本地不运行 `Approval.Database.Tests`，不启动 Docker。

## 根因与最小修复

1. **只读检查修改并发令牌**：`LoadTaskAsync` 默认 `write = true`，`CanHandleAsync` 和超时无操作路径提前 Touch 跟踪实例。改为显式 `write: false`；超时状态、任务状态和到期检查全部通过后才 Touch。没有 Timeout Policy 的遗留 `NextRemindAt` 清理仍递增令牌并 Flush。既有 UnitOfWork、CommandExecutor、冲突异常转换、日志、通知、Outbox 和 AfterCommit 保持。
2. **Resolver 表单数据可变污染**：Parse 返回可变 Dictionary / ExpandoObject / object 数组，规则共用 Context。每次 Resolver 调用在既有 FormData 类内递归复制动态对象和数组，顶层用 `ReadOnlyDictionary`。保留大小写不敏感顶层键、decimal / double、布尔、字符串、null、动态属性和数组读取；嵌套对象允许修改本次快照。Parse 和条件表达式使用的原数据不变，重复 Resolver 和通知收件人解析也经过同一隔离入口。
3. **治理列表重复授权**：DefaultApprovalAuthorization.RequireAsync 内部调用 CanAsync，允许资源在循环中被判断两次。移除已允许资源的 RequireAsync；全局 RequireAsync、每 Key 的 CanAsync、拒绝后 continue 和每允许资源三项能力检查保持，不缓存授权。三资源全允许时从16降为13次。`ListCategoriesAsync` 保留既有列表与能力调用，不扩大优化范围。

## LoadTaskAsync 调用审查

| 调用入口 | 读写判定 |
| --- | --- |
| CanHandleAsync | 显式 write:false；权限与状态检查，不 Touch |
| GetReturnTargetsAsync | 原有显式 write:false 保持 |
| HandleTimeoutAsync | 显式 write:false；真正到期处理或遗留清理前 Touch |
| ApproveAsync / ApproveWithDataAsync / RejectAsync / TransferAsync | 保留默认写加载和既有 Touch |
| ReturnAsync / AddSignAsync / ResubmitAsync | 保留默认写加载和既有 Touch |
| ReassignAsync | 保留默认写加载和既有 Touch |

FindHandleableTaskAsync 等其他读取路径不通过该写加载方法；现有 AsNoTracking 保持。没有将默认值改成 false。无操作超时虽然保留既有事务入口，但不制造审批实体修改。

## 修改文件

- `src/Approval.Core/Engine/ApprovalEngine.cs`、`ApprovalEngine.Timeouts.cs`：只读加载和超时 Touch 时点。
- `src/Approval.Core/Conditions/FormData.cs`：internal 快照与递归复制方法。
- `src/Approval.Core/Engine/Runtime/ApprovalAssigneeService.cs`：逐 Resolver 新 Context / FormData 快照。
- `src/Approval.Core/Abstractions/Abstractions.cs`：FormData 快照契约注释，公共签名不变。
- `src/Approval.Core/Engine/ApprovalDefinitionService.Governance.cs`：移除资源重复 RequireAsync。
- `tests/Approval.Core.Tests/ReadOnlyConcurrencyTests.cs`：13项新用例。
- `tests/Approval.Core.Tests/ResolverFormDataIsolationTests.cs`：5项新用例。
- `tests/Approval.Core.Tests/DefinitionQueryOptimizationTests.cs`：2项新测试及既有精确调用次数修正，原结果与单查询断言保留。
- `tests/Approval.Database.Tests/ReadOnlyOperationDatabaseTests.cs`：10项跨 Provider 新用例。
- `tests/Approval.Database.Tests/QueryOptimizationDatabaseTests.cs`：既有真实 Provider 测试增加全局/资源授权次数与拒绝资源能力断言。
- `docs/backend-context-api-migration.md`、`backend-refactoring-final-report.md`、本文：补充表单隔离契约及本次验收，区分历史 CI。

## 需求与回归证据

测试名下表均为精确方法名；参数化用例使用稳定状态/动作标签。

| Requirement | Evidence |
| --- | --- |
| CanHandle 允许与拒绝都不改令牌，宿主保存后重新读取不变 | `ReadOnlyConcurrencyTests.CanHandle_DoesNotChangeStampWhenHostSaves`（bob / other） |
| 退回目标只读检查不改令牌 | `ReadOnlyConcurrencyTests.ReturnTargets_DoesNotChangeStampWhenHostSaves` |
| 未到期超时返回false，共享 UnitOfWork 提交后令牌不变 | `ReadOnlyConcurrencyTests.TimeoutNoop_DoesNotChangeStampWhenHostUnitCommits`（not-due） |
| 已处理、非运行、等待、无提醒安排的超时检查不改令牌/日志/Outbox | 同方法（handled / not-running / waiting / unscheduled） |
| 到期提醒更新令牌、次数、时间、下一提醒、日志、消息和 Outbox | `ReadOnlyConcurrencyTests.DueTimeout_ChangesStampAndPersistsDecision`（Remind） |
| 自动通过/驳回更新令牌，状态、日志、Outbox 和任务时间正确 | 同方法（AutoApprove / AutoReject） |
| 无策略仍清理遗留 NextRemindAt 并修改令牌，不制造提醒 | `ReadOnlyConcurrencyTests.TimeoutWithoutPolicy_ClearsLegacyReminderAndChangesStamp` |
| 真实审批识别陈旧令牌，回滚失败操作并保留赢家 | `ReadOnlyConcurrencyTests.RealApproval_RejectsStaleTrackedInstanceAndPreservesWinner`；`DatabaseContractTests.RealApproval_RejectsStaleStampAndRollsBackSideEffectsOnEachProvider` |
| 顶层 IDictionary 不能 Set/Add/Remove/Clear | `ResolverFormDataIsolationTests.Resolvers_ReceiveReadOnlyRootsAndIndependentNestedSnapshots`（set / add / remove / clear） |
| 两个 Resolver 及同一 Resolver 重复调用的动态对象/数组均递归隔离 | 同方法，覆盖对象内数组、数组内对象、对象内对象 |
| 原读取类型、顶层大小写和条件表达式行为保持，保存后 FormDataJson 不变 | 同方法，验证 decimal/double/bool/null/ExpandoObject/object数组、条件走正确分支、重新读取 JSON |
| Resolver 失败不保存数据，也不污染下一次操作 | `ResolverFormDataIsolationTests.ResolverFailure_DoesNotPolluteTheNextOperationOrPersistFormData` |
| 保留全局权限，拒绝时不查询治理数据 | `DefinitionQueryOptimizationTests.GovernanceAndCategories_GlobalDenialStopsBeforeResourceQueries` |
| 每资源独立授权一次，单治理投影，能力检查保持 | `DefinitionQueryOptimizationTests.GovernanceSummary_LoadsOneProjectionAndPreservesPerResourcePermissionChecks` |
| 同一作用域撤销即生效，拒绝资源跳过，摘要、能力、分类/未分类正确 | `DefinitionQueryOptimizationTests.GovernanceSummary_RechecksEveryResourceAfterRevocationAndPreservesCapabilities` |
| Provider 实际 SQL 下只读与超时写入/清理、保存和重读正确 | `DatabaseContractTests.ReadOnlyTaskChecks_PreservePersistedStampWithSharedHostSaveOnEachProvider`；`TimeoutWrites_PersistStampAndDecisionOrLegacyCleanupOnEachProvider` |
| Provider 治理投影/资源隔离/精确权限调用保持 | `DatabaseContractTests.GovernanceSummary_TranslatesNullableDraftChangesAndFiltersResourcesOnEachProvider` |

原实现实际复现：CanHandle 两种结果及五种超时无操作场景均提前递增令牌；Resolver 的顶层字典非只读。真实审批冲突测试在原代码和修复后都通过。所有旧业务断言保留；既有授权次数16→13及每Key 2→1是针对明确重复检查缺陷的期望修正。

跨 Provider 的陈旧审批测试用真实遗留提醒清理命令作为赢家：该写命令递增令牌但不生成日志，随后陈旧审批必须准确报 Conflict，且任务、日志和业务回调写入全部回滚。首个 Run 41753208870 中 MySQL / PostgreSQL 的两个同意命令竞争先触发日志序号唯一约束；因此调整测试竞争场景以独立验证令牌，没有放宽异常断言或改变生产并发机制。首个 Run 不作为最终验收。

## 验证记录

本地阶段独立构建/执行：Concurrency + 原超时测试 Passed19 / Failed0 / Skipped0；Resolver + 扩展/组织/条件相关测试 Passed28 / Failed0 / Skipped0；Governance + 权限相关测试 Passed41 / Failed0 / Skipped0。TRX 已核对执行数与结果，位置 `output/backend-fixes/local/`。

完整 Release Solution Build 已实际执行：0 warnings / 0 errors。本地三个业务测试项目共 Passed555 / Failed0 / Skipped0（Core416、AspNetCore81、Certificate58），各项目 TRX 的执行数与结果已核对。Contracts 已通过，所有已提交 HTTP / C# / TypeScript 基线匹配；前端 Boundary（95源文件）、7项 Boundary 测试、Typecheck、180项UI测试、6项Demo测试、库和Demo Build 全通过。GitHub CI 的最终证据如下，本地业务测试与远端数据库实际执行明确区分。

数据库专项在本地仅编译，实际测试在 GitHub 执行。最终测试提交 `553a1e8968d865fb35a1e2f5d57c72e9b8214bfe`，生产修复提交 `c10113d74fdecfbda8349c5c32c8a42ae9088890`；[GitHub Run 41753786611](https://github.com/ttcc666/WorkflowDemo/actions/runs/41753786611)。

| Provider | Passed | Failed | Skipped | 核验 |
| --- | --- | --- | --- | --- |
| MySql | 43 | 0 | 0 | HEAD一致、validated=true、testExitCode=0、3份artifact SHA256一致 |
| PostgreSql | 43 | 0 | 0 | 同上 |
| SqlServer | 43 | 0 | 0 | 同上 |

三个 Provider 每个43项均实际执行，包括10项新增用例；TRX准确用例数与状态、manifest和哈希已核对。证据目录 `output/backend-fixes/ci/41753786611`，汇总 `database-evidence.json`。

整个 Run 已 completed / success，四个作业全部 success，HEAD精确匹配553a1e8。Windows verify 实际执行 `./eng/verify.ps1`，最后输出 `Verification succeeded: All`：

- Release Solution Build 0 warnings / 0 errors；后端 Passed598 / Failed0 / Skipped0（Core416、AspNetCore81、Certificate58、SQLite数据库专项43）。所有旧测试与新增回归均保留通过；SQLite专项也仅在 GitHub 执行。
- 九项 EF Migration Model Drift 检查全部通过；HTTP/C#/TypeScript Contracts、前端 Boundary/Typecheck/Tests/Build 全通过。
- 独立 NuGet / npm、Certificate / ReferenceHost / Core-only 消费者通过，release-manifest.Commit匹配验收SHA。
- Core-only verification.json 为passed=true，18个真实检查；两个ReferenceHost smoke.json各passed=true / checks=27。
- Browser results.json为expected=5 / unexpected=0 / skipped=0 / flaky=0 / errors=0，metadata.ci.commitHash匹配验收SHA。

完整Windows日志 `output/backend-fixes/ci/41753786611/windows.log`，状态 `run.json`，产物核验汇总 `windows-evidence.json`。验收期间没有本地运行数据库专项或 Docker；仅本地编译的数据库测试最终均有本次修复提交的实际 CI 结果。CI中的既有Actions Node20提示和前端chunk提示不属于C#编译警告。

最终报告文档提交只记录上述实际验收，代码/测试以553a1e8为准。本阶段无未完成验收事项；未创建新分支、合并main、重写历史或强制推送。

## 最终审查与边界

- 生产修改不新增服务、Manager/Provider/Coordinator、程序集、依赖或公共 API。
- Snapshot 仅复制 Parse 已有的可变类型，不序列化往返、不新增数据库查询；代价为每 Resolver 一份快照，符合隔离要求。
- 没有改状态机、Any/All/Sequential、Parallel/Join、退回/加签/转交/重提、幂等、事务入口、Outbox 投递或 AfterCommit。
- 没有改变数据库模型、Migration、HTTP JSON、OpenAPI、TypeScript 基线。
- 没有缓存授权或削弱拒绝行为；没有扩大公共 Entity API 或同步 Validate 的改造范围。

## 复验命令

仓库根目录执行；数据库测试只使用 GitHub 的本次 Run。

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
dotnet build 'Workflow.slnx' --configuration 'Release' --no-restore --verbosity 'minimal'
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --no-build --no-restore --verbosity 'minimal'
dotnet test 'tests/Approval.AspNetCore.Tests/Approval.AspNetCore.Tests.csproj' --configuration 'Release' --no-build --no-restore --verbosity 'minimal'
dotnet test 'tests/Certificate.Api.Tests/Certificate.Api.Tests.csproj' --configuration 'Release' --no-build --no-restore --verbosity 'minimal'
pnpm --dir 'web' 'contracts:check' --no-build
pwsh -NoProfile -File 'eng/verify.ps1' -Scope 'Frontend' -SkipRestore
gh run list --repo 'ttcc666/WorkflowDemo' --branch 'codex/backend-progressive-refactoring' --limit 5
gh run view '41753786611' --repo 'ttcc666/WorkflowDemo' --json 'headSha,status,conclusion,jobs,url'
```
