# 审查问题修复与验证报告

日期：2026-11-05（Asia/Shanghai）。仓库：`ttcc666/WorkflowDemo`。

## 基线与交付范围

- 开始分支：`main`；HEAD、审查基准和 merge-base 均为 `4d9dab101e6500d8f8b1e6a87de93824e85e6aba`，没有基准之后的提交。
- 本轮开始时只有上一轮生成的 `plans/2026-11-05-review-stability-fixes.md` 未跟踪；未覆盖用户改动。
- 用户授权“按计划修复”后执行两批：先正确性 / 报告修正，经本地回归与包浏览器验证后，再实施模拟资源预算。
- 以下结果为该基准上的提交前工作区验收快照，没有用历史 CI 代替本次验证。用户随后已授权按两个逻辑提交推送至工作分支；具体提交与 CI 链接见后续交付说明，不直接合并主分支。

## 问题、根因与修复

| 问题 | 是否存在 / 根因 | 实际修复与关键位置 |
|---|---|---|
| P1 角色任务异常漏报 | 存在。旧诊断只检查节点级合格用户集合；A 角色失效但 B 有成员时，集合非空掩盖 A。展开角色后丢失具体角色来源。 | `src/Approval.Core/Diagnostics/ApprovalDiagnosticsService.cs:63` 按任务分配对象查询；`Engine/ApprovalEngine.cs` 内部入口复用原权限及真实实例 / 节点重载；`Engine/Runtime/ApprovalAssigneeService.cs:159` 从未展开的真实 Resolver 输出确认具体 Role，再仅展开该角色，复用目录、状态、自审及跨节点历史约束。User 仍判断其具体用户资格，公共用户集合 API 不变。 |
| P1 超期边界不一致 | 存在。原审查中“统计为 <=”并不完全准确：统计总数和积压原本也使用严格大于 / 小于。前端同样漏掉相等。 | `Engine/ApprovalQueryService.cs:119`、`ApprovalStatisticsService.cs:126` 与积压两处分支统一当前待办 `DueAt <= now`；`web/packages/approval-ui/src/utils/labels.ts:87` 同步到点即超期。继续使用注入 TimeProvider 的 UTC 时刻；null 不超期。已正确的实例筛选、诊断、扫描无需改动。历史完成任务 `CompletedAt > DueAt` 的迟到统计保持原语义。 |
| P2 离职交接冲突汇总过宽 | 存在。Conflict 回查原来只有 InstanceId / AssigneeValue 条件，可能混入角色、历史、已结束流程。 | `Engine/ApprovalEngine.Admin.cs:121` 的私有 IQueryable 查询由候选计划和冲突回查共用，应用 QueryScope、User + 指定用户、Pending / Waiting / Suspended、Running / Returned；再限定争用实例。逐实例事务、稳定键、部分成功及最终汇总重放保持原样。 |
| P2 模拟累计资源预算缺失 | 存在。既有步数、单节点、单角色、单节点说明限制不能限制整个请求；资格过滤、展示也会重复展开角色。 | `Engine/Runtime/ApprovalSimulationBudget.cs` 新增请求局部内部计数；`ApprovalSimulationService.cs:54` 为全流程 / 分支 / 预览共享同一预算；通过 `ApprovalAssigneeService` 的内部参数覆盖解析、过滤和展示。先检查调用额度及返回列表 Count，再枚举、格式化、追加或读取目录。 |
| P3 报告误述 Reload | 存在。目标浏览器恢复用例没有 `page.reload()`；“重建页面”单测只是重建 manager 并重新提供正文。 | 修正 `docs/approval-productization-report-2026-11-04.md` 的行为和证据表；重命名单测以准确表达重建意图管理器；在 `web/tests/browser/package.spec.ts` 的恢复用例标注同页边界。原键 / 原正文断言保持，未增加正文持久化或虚构整页 Reload 能力。 |

## 模拟预算的具体语义

| 维度 | 默认上限 | 计数规则 |
|---|---:|---|
| 请求解析对象累计 | 2,000 项 | Resolver 原始返回项 + 角色展开生成的 User 项；去重 / 过滤不退还。 |
| 请求角色成员累计 | 5,000 项 | 每次引擎展开获得的原始成员项；解析、资格过滤、展示中的重复展开和重复项也计费。 |
| 请求 Resolver 调用 | 500 次 | 每次规则回调前检查并扣费，空输出及不自报说明的 Resolver 同样受限。 |
| 请求解析说明累计 | 2,000 条 | 引擎输入 / 输出说明和宿主自报 Trace 都计费，保留每节点 200 条上限。 |

保持原有 200 自动步数、单节点 200 分配对象和单角色 200 个不同成员。请求累计值分别允许多个正常节点，并通过每项上限前一项 / 恰好上限 / 后一项回归验证；没有新增公开配置选项。它们是合理的防护默认值，不代表已经进行生产容量压测。

超限复用 InvalidDefinition 的现有模拟错误路径，结果为 `isValid=false` / `isComplete=false`，当前步骤 Failed，包含稳定中文维度与上限。达到上限且没有后续消耗仍可完成。预算超限状态不能被宿主捕获 Trace 异常后清除；下一请求重新创建预算。

`FlowDefinitionValidator` 保持结构 / 规则校验职责。模拟请求执行预算没有扩大成发布限制，也不拒绝单节点预览中未执行的其他节点。正式审批不传预算，沿用既有运行语义。

现有 Resolver / Identity 返回完整列表。引擎可在拿到 Count 后停止自己的后续工作，不能阻止宿主在返回前分配超大列表、构造超大单条说明，或忽略取消令牌。这是本轮有意保留的扩展边界，不宣称任意宿主代码具有强制内存 / CPU 沙箱。

## 回归证据

表中列出实际测试名。新增回归先在旧实现失败，再修复；未删除旧测试或放宽断言。

| Requirement | Evidence |
|---|---|
| A 失效 / B 有效仅报 A；均有效无异常；均失效都报告；自定义 Resolver 返回角色也准确 | `DiagnosticsAssigneeTests.RoleDiagnosticReportsOnlyUnavailableRole`，4 个参数化病例，正常建任务后撤销角色成员，比较精确 TaskId。 |
| 自审、目录停用、宿主状态、跨节点审批历史、不同成员不能分别满足目录 / 状态；User 原行为 | `RoleDiagnosticUsesExistingEligibilityConstraints`，5 个病例；`UserDiagnosticStillChecksTheAssignedUser`；已有 `DiagnosticFindingsCarryScopedResourceLinks_AndExposeSampleTruncation`。 |
| DueAt 早于 / 等于 / 晚于 / null；待办、实例、统计、积压、诊断一致；积压新分组及累计分支 | `OverdueBoundaryTests.PendingOverdueBoundaryIsConsistentAcrossQueriesStatisticsAndDiagnostics`，4 个病例，每例同处理人两条任务，断言精确实例集合及各指标。 |
| 历史恰好截止时完成不算迟到 | `CompletedAtDeadlineKeepsHistoricalStatisticsMeaning`；既有 `Statistics_StreamsNodeMetricsAndOrdinalBacklogAtExactBudget` 仍断言历史指标，当前待办部分按新边界精确更新。 |
| SQL 翻译、范围、分页、角色待办边界；UI 相等时刻正确 | 扩充 `OperationsOverdueFilter_TranslatesScopeAndPaginationOnEachProvider` 的 GetTodo 分页 / 精确集合断言；`PagingTests.Todo_includes_role_tasks_supports_filters_and_count`；Vitest `dueText uses inclusive expiry: before deadline / at deadline / after deadline`。 |
| Conflict 后只有指定用户的三种开放任务；活动流程；排除角色、历史、他人和范围撤销 | `BulkReassignmentConflictReportsOnlyOpenUserTasksAndKeepsCommittedInstances`，7 个病例覆盖 Running、Returned、四种结束状态及当前范围撤销；保存时注入 EF 并发异常，回滚后改变状态 / 范围。 |
| 冲突不回滚其他实例；原键汇总重放不重复写 | 同一交接回归断言成功实例仍属于新处理人、恰一条改派日志、失败实例无改派写入、Replay 结果相同；同时保留 `KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion`。 |
| Provider 版本查询 | `BulkReassignmentConflictQuery_TranslatesOnEachProvider`，4 个病例，使用相同 Provider 测试宿主和真实 EF 查询；本地实际只执行 SQLite，其余待 CI。 |
| 解析人数跨节点计费且重复项不能绕过 | `SimulationResolvedAssigneeBudgetCountsRawOutputsAcrossNodes`，1,999 / 2,000 / 2,001；`SimulationExpandedUsersConsumeTheRequestAssigneeBudget`，All / Sequential；`SimulationNodeAssigneeLimitRemainsIndependentFromRequestBudget`，200 / 201 个不同用户。 |
| 请求角色成员累计与每种模式提前检查 | `SimulationRoleMemberBudgetCountsRepeatedExpansionsAndRawDuplicates`，4,999 / 5,000 / 5,001，检查成员接口和目录调用次数、后续分支未调用；`SimulationRoleLimitStopsBeforeDirectoryInEveryMode`，Any / All / Sequential，断言目录 / 状态调用为零。 |
| Resolver 和 Trace 各自累计边界、超额回调不再执行 | `SimulationResolverBudgetStopsBeforeTheNextCallback`，499 / 500 / 501，实际最多 500 次；`SimulationTraceBudgetAccumulatesAcrossNodes`，1,999 / 2,000 / 2,001。 |
| 在枚举 / 格式化前拒绝大列表，宿主吞 Trace 异常仍失败 | `SimulationRejectsOversizedResolverOutputBeforeEnumerationOrTraceFormatting`、`SimulationTraceBudgetFailureCannotBeSwallowedByResolver`，用禁止枚举的返回列表验证检查顺序。 |
| 同服务请求隔离、单规则预览、失败无数据 / 业务副作用 | `SimulationBudgetIsLocalToEachRequestAndFailuresHaveNoEffects`：同 scope 先失败，再连续两次恰好 2,000 项成功，再单规则预览，数据库及外部记录均为空。 |
| HTTP 预算失败稳定且无需幂等键，无持久化写入 | `SimulationEndpointTests.SimulationBudgetExceededReturnsExplicitErrorsWithoutWrites`；既有权限 / 无键 / 可信申请人病例一并通过。 |
| 正常模拟、顺序、角色语义、并行 / 嵌套 Join、Forms、只读及取消 | 完整运行既有 `SimulationTests`、`AssigneePreviewTests`，包括 `RolePreviewPreservesRuntimeTaskAssignment`、`NestedParallelBranchesUseSharedJoinRules`、`SimulationHasNoPersistentOrBusinessEffects_AndHonorsCancellation`、`InFlightResolverObservesCallerCancellation`。 |
| 未知提交结果同页原键 / 原正文恢复，身份和改正文保护 | 既有命令意图单测及浏览器 `packaged approval recovers the original key and payload after the server commits but response is lost`；报告明确此用例未执行整页 Reload。 |

静态断言复核覆盖精确集合、计数、错误内容 / 步骤、负向调用、数据库最终状态和外部副作用；没有无断言或仅 presence / truthiness 的新测试。检查了把边界改回严格比较、移除角色区分、扩大回查及移除各预算的可观察影响；没有执行 mutation testing，也不声称执行过。

## 实际执行的命令与结果

工作目录均为仓库根目录，前端命令另有注明。原开发服务继续运行；完整构建 / 解决方案测试使用隔离输出，避免覆盖它占用的 Release DLL。以下日志在 `output/review-fixes/`。

### 第一批

1. Core 新回归基线：

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~DiagnosticsAssigneeTests|FullyQualifiedName~OverdueBoundaryTests' --logger 'trx;LogFileName=batch1-before.trx' --results-directory 'output/review-fixes'
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~BulkReassignmentConflictTests' --logger 'trx;LogFileName=batch1-conflict-before.trx' --results-directory 'output/review-fixes'
```

原实现分别失败 8 / 15、7 / 7，符合复现预期。修复后同三类测试合并执行，`batch1-after.trx`：Passed 22、Failed 0、Skipped 0。数据库新定向病例 `batch1-provider-sqlite.trx`：Passed 5、Failed 0、Skipped 0。

2. 前端与契约 / 包：

```powershell
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
```

通过。UI 单测 242、Demo 单测 6；边界检查 / 边界测试、TypeScript、库构建及 Demo 构建通过。新增 UI 截止时刻测试在原实现上 1 失败 / 3，修复后 3 通过。日志：`batch1-frontend.log`。

3. 完整解决方案：

```powershell
dotnet build 'Workflow.slnx' --configuration 'Release' --artifacts-path 'output/review-fixes/artifacts'
dotnet test 'Workflow.slnx' --configuration 'Release' --artifacts-path 'output/review-fixes/artifacts' --no-build --no-restore --logger 'trx' --results-directory 'output/review-fixes/batch1-tests'
```

构建通过，0 警告 / 0 错误。第一批全量回归发现两处旧边界断言：Core 515 通过 / 1 失败，数据库 60 通过 / 1 失败；HTTP 92、Certificate 59 全通过。按 `<=` 更新精确预期后，这两项分别定向复测通过，TRX 为 `batch1-paging-updated.trx` / `batch1-statistics-updated.trx`；没有改变历史完成迟到断言。后续第二批最终全量门禁再次验证全部。

第一批包验证 `pwsh -NoProfile -File './eng/verify-packages.ps1' -SkipBuild -RunBrowser` 通过，包括独立 NuGet / npm 消费者、嵌入宿主和 7 个浏览器用例。第一批 `pnpm contracts:check --no-build` 使用上述隔离输出，通过全部基线，未执行 contracts:update。

### 第二批及最终门禁

预算基线命令：

```powershell
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --filter 'FullyQualifiedName~SimulationBudgetTests' --logger 'trx;LogFileName=batch2-before.trx' --results-directory 'output/review-fixes'
```

原实现 12 失败 / 8 通过。修复后连同已有模拟 / 预览测试 `batch2-after.trx`：Passed 49、Failed 0、Skipped 0；随后补充两项单节点人数边界，纳入最终全量测试。HTTP 定向 `SimulationEndpointTests`：Passed 4、Failed 0、Skipped 0（`batch2-http.trx`）。

最终命令（PowerShell；环境变量只在该验证进程内设置）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
dotnet build 'samples/Approval.ReferenceHost/Approval.ReferenceHost.csproj' --configuration 'Release'
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = 'D:\Study\CSharp\Workflow\output\review-fixes\artifacts'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend' -SkipRestore
```

ReferenceHost 先构建到默认路径供脚本直接启动；解决方案及 EF 工具使用隔离输出。模型漂移检查只比较模型，不连接三个服务数据库；不能当作真实数据库测试通过。

最终 Backend 退出 0，日志 `final-backend.log`：

| 检查 | 实际结果 |
|---|---|
| Release 完整解决方案构建 | 通过，0 警告 / 0 错误。 |
| Approval.Core.Tests | Passed: 538, Failed: 0, Skipped: 0。 |
| Approval.AspNetCore.Tests | Passed: 93, Failed: 0, Skipped: 0。 |
| Approval.Database.Tests（本地 SQLite） | Passed: 61, Failed: 0, Skipped: 0。 |
| Certificate.Api.Tests | Passed: 59, Failed: 0, Skipped: 0。 |
| EF Model Drift | Certificate + ReferenceHost / ContractContext 的四种 Provider，共 9 项通过；仅模型比较。 |
| ReferenceHost 实际 SQLite smoke | 27 checks，通过；证据 `output/reference-host/d99ba5d12115425d9e135a7cfb959a28/smoke.json`。 |

后端测试合计 751 项，全部通过；其中新增 Core 回归 44 项、HTTP 1 项、Provider 公共回归 4 项，另扩充既有数据库分页断言。前端新增 3 项截止边界回归，已纳入 UI 242 项。

最终包与契约检查在独立验证进程执行，避免将隔离输出环境传入包脚本的独立消费者：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify-packages.ps1' -SkipBuild -RunBrowser
```

退出 0（`final-packages.log`）。重新生成的四个 NuGet 包及 UI npm 包被独立消费者构建 / 验证，嵌入宿主 18 项、ReferenceHost 27 项、7 个 Playwright 浏览器用例全部通过。实际消费者：`output/engineering/consumer-134c786dcd4749afaadf6e4b6b80c84e`；浏览器证据为其中 `browser/results.json`，包证据见 `output/packages/0.3.0/release-manifest.json`。

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow\web'
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = 'D:\Study\CSharp\Workflow\output\review-fixes\artifacts'
pnpm contracts:check --no-build
```

退出 0（`final-contracts.log`），所有 checked-in 基线匹配；没有执行 contracts:update。最终前端沿用第一批已通过的 Frontend 门禁，之后没有前端运行代码修改。没有运行默认输出的整条 All 脚本；分别执行其 Backend、Frontend、Contracts 与 Packages 检查，Backend 使用上述隔离输出以避开开发服务文件锁。

### 执行中遇到并已定位的环境问题

- 首次普通完整构建与 Demo 构建并行，Demo 更新 Certificate.Api/wwwroot 导致 StaticWebAssets 引用旧 hash 资产；停止并行后顺序重试。
- 顺序重试发现昨日启动的 Certificate.Api 开发服务锁定默认 Release DLL；未停止该服务，改用上述隔离产物目录成功构建。没有修改 csproj 规避资源校验，也没有删除开发数据库。
- 首轮完整回归的两处旧断言按新业务边界精确调整并复测；不是以 build 成功代替 tests passed。

## 未验证项与风险

- 本地验收时 PostgreSQL、SQL Server、MySQL 的真实数据库测试尚未执行。按仓库规定由现有 GitHub Actions 矩阵执行；后续用户已授权推送并核验对应提交，未在本地执行 Docker / 服务数据库脚本。
- 推送后需对包含本次改动的确切提交 SHA 检查三个 databases job 及 Windows verify job；CI 状态以后续交付说明为准，当前基准的历史绿色结果不构成本次验收。
- 没有实现或声称通过整页 Reload 后自动恢复敏感原正文。存储仍只有原键及摘要，真正 Reload 后须重新提供原内容或核对结果，再显式开始新意图，不静默换键。
- 模拟预算限制引擎可控的累计工作，不强制限制宿主回调内部在返回前的分配和 I/O。没有执行生产容量压测。

## 契约、数据库与 Git diff

- 公共 C# API、HTTP / JSON / TypeScript、错误枚举、流程 Schema 均未改变；内部新增预算对象与任务资格入口。
- 数据库模型无变化，Migration / Snapshot 数量为零；未重置开发数据库；无新增 NuGet / npm 依赖。
- 关键生产变更集中于原诊断、查询、统计、管理、审批人解析和模拟服务；新增测试采用现有 xUnit / EF / Vitest 基础设施；恢复正文设计保持不变。
- diff 涉及 25 个文件：16 个既有文件修改（+118 / -32）、9 个新增文件共 1,037 行（包含上一轮计划与本次报告），合计 +1,155 / -32；生产代码约束在原职责内，新增一个内部预算类型，新增五个 C# 回归测试文件与一个 UI 测试文件。已按仓库行尾规则检查 tracked diff 与全部新增文件，无空白错误。所有改动保留在工作区，未自动提交。
