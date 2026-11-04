# 开发期兼容清理执行报告

2026-11-04。依据用户确认：项目尚未发布，samples 仅为仓库示例，没有其他已部署宿主。本轮按 `plans/2026-11-04-remove-unreleased-compatibility.md` 实施，沿用并保留上一轮工作区的异步校验、严格幂等和统计预算变更。

状态：实施及两次补查完成。最新深度复查后端 667/0/0、三种服务数据库各 56/0/0，相关前端测试、类型检查、构建、契约及独立包消费者通过，见文末。浏览器及真实认证/恢复/TCP 演练的前轮结果单独保留；下文区分原完整脚本失败与修复后的单独成功阶段。

## 已删除及更新

| 范围 | 当前实现 |
| --- | --- |
| 幂等旧结果擦除 | 删除 IsTombstone 字段/映射、ReplayExpired 枚举、HTTP 410 映射与前端专用分支。操作结果保留完整 JSON；请求摘要、版本保护、原键重放、当前权限复核和未知提交核验保持 |
| 旧通知 Outbox | 删除裸 Notification 转换、OutboxKind 枚举及实体/表/DTO 的 Kind 字段。直接投递当前 ApprovalEvent，通知为 NotificationRequested + DeliveryTarget + Notification。缺失字段、无效 JSON、标识不一致和旧裸载荷在处理器调用前失败，沿用原退避/失败规则 |
| 旧构造入口 | 删除 ApprovalEngine、ApprovalDefinitionService、ApprovalOutboxService 的公开适配构造器，以及 Dispatcher/TimeoutScanner 两个忽略旧参数的重载；accessor 的授权、命令执行器和操作人依赖必填。删除无用 options/extensions 参数与 MissingApprovalAuthorization 占位，当前扩展统一由完整 DI 注入 |
| 隐式 JSON 版本 | 外部定义必须显式声明 schemaVersion=1；JsonRequired、前端校验和生成 JSON Schema 一致要求必填。C# 直接构建仍初始化当前版本，序列化显式写出。样例/fixture/动态 JSON 生产者已迁移，缺省版本负例保留 |
| 前端旧类型投影 | ApprovalTask 使用完整生成响应类型，DirectoryUser.isActive 必填；未解析用户明确 isActive=false，目录后续返回有效用户时恢复真实状态。更新组件测试数据，未增加传输补字段兼容器 |
| 初始化模型 | EF 重新生成九套 InitialApproval/Designer/Snapshot（27 个文件）；新基线无墓碑列和 Outbox Kind，不提供旧库转换或增量兼容迁移 |
| 演练 | 不再制造墓碑或裸通知；使用保留的完整操作结果和当前事件，恢复后原键返回原 ID 且完整持久化快照不变。种子准备采用私有离线上下文并复用 loopback/approval_test 数据库校验；HTTP/应用继续使用带写保护的 ReferenceDbContext |

Certificate 初始迁移为 `20261104103442_InitialApproval`。其余八套由对应 Provider 的 EF 设计时工厂生成，具体 ID 以各自 Migrations 目录为准。

## 保留的当前功能

FieldPermissions/FieldAccess/HiddenFields 是实际字段权限、投影和附件授权数据；通知渠道是当前路由提示；业务流程发布版本仍绑定在途实例。这些不是旧软件兼容层。

HashVersion/ResultVersion 仍只接受当前一种格式，用于错误格式拒绝；没有旧格式读取器。通知默认规则、浏览器存储不可用时保留意图键、无超时策略时清理异常调度、HostServices 的安全包装及 EF 生命周期能力继续保留。

## 要求与回归证据

| Requirement | Evidence |
| --- | --- |
| 外部 JSON 只接受显式当前版本 | `DefinitionContractTests.Shared_definition_contract_requires_explicit_current_version`；`DefinitionGovernanceEndpointTests.Publication_RequiresExplicitCurrentSchemaVersion_WithoutPartialWrites`；前端 `shared definition contract: missing-version` 与同一共享数据 |
| 无效/旧事件不被确认、不调用处理器 | `NotificationTests.Invalid_event_payload_fails_without_acknowledging_or_calling_handler`：18 个安全具名用例，包括旧裸通知、字段缺失、错误 ID、无效状态及通知内层标识不一致 |
| 当前通知、事务和重试保持 | `Enter_rule_writes_in_app_messages_for_role_members_and_outbox_for_external_channel`、`Messages_and_outbox_roll_back_with_the_approval`、`Pending_delivery_survives_restart_and_waits_until_scheduled_retry` |
| 当前 DI 扩展实际执行 | `AsyncValidationTests.ValidateAsync_WaitsForPermissionWithoutRunningValidation`、`FieldPermissionTests.同意时修改可编辑字段_按新值重新计算与流转_标题更新_轨迹记录差异` 及独立 Core-only 消费者 |
| 幂等、权限撤销与未知结果保持 | `DefaultPolicy_DirectStartRequiresKey_AndKeyedStartReplays`、`OperationEndpointTests.DefaultPolicy_RejectsUnkeyedWriteWithoutSideEffects_AndReplaysKeyedWrite`、`AtomicCommands_UnavailableCommitVerification_ReturnsUnknownAndOriginalKeyRecovers` |
| 字段与附件授权保持 | `FieldPermissionTests.隐藏字段_只以隐藏节点参与的人看不到_发起人与管理员可见`、`修改轨迹的字段差异按隐藏规则过滤` 以及原附件权限回归 |
| 未解析目录用户明确不可选且可重新解析 | 前端 `未解析用户明确不可用，后续目录返回当前状态时恢复可选` |
| 新模型可初始化、回滚重建且无漂移 | `Module_contains_only_current_initial_migration_and_model`、`Initial_migration_can_roll_back_empty_schema_and_recreate_without_model_drift`、`Migrations_RollBackAndReapply_WithoutModelDrift` |
| 恢复后重放完整保留结果、当前事件不变 | RehearsalTool 检查 `RestoredRetainedResultReplaysWithoutWrites`、`RestoredOutboxKeepsCurrentEventIdentityAndPayload`，须以本轮演练 JSON 为完成证据 |

## 本轮验证

- 当前源码基线定向测试：Passed 30 / Failed 0 / Skipped 0，日志 `output/compat-baseline.log`。
- 先执行新增拒绝回归，实际观察到旧实现的失败：缺省版本被接受，部分不完整事件被确认成功，日志 `output/compat-red.log`。
- 第一轮完整门禁 `output/compat-verify.log` 记录了尚未迁移的样例 JSON、动态 JSON 和固定旧迁移 ID；均已在真实生产者/fixture 中更新。一次门控授权测试受短截止时间影响；该用例改为较长独立预算，专门的授权超时回归仍使用原短期限。
- 初轮完整后端：Passed 661 / Failed 0 / Skipped 0（Core 460、HTTP 86、SQLite Database 56、Certificate 59）。九项 EF model drift 检查通过。
- Release solution 与更新后的 RehearsalTool 构建均为 0 warnings / 0 errors。ReferenceHost 集成、九项模型漂移、前端 typecheck、UI 181 + demo 6、边界 7、库/demo 构建及契约检查通过。
- 四个 NuGet 包与 npm 包的独立消费者构建和运行通过；Core-only 消费者 18 checks 通过。消费者产物目录：`output/engineering/consumer-535f42bc872a478b8cd62413771eee7d`。
- `output/compat-verify-final.log` 完整脚本最终在浏览器分类 fixture 缺少 schemaVersion 时退出 1；修正该测试生产者后，单独完整重跑浏览器阶段，5 passed / 0 failed / 0 skipped，日志 `output/compat-browser.log`。同一消费者的 `browser/results.json` 为 expected=5、unexpected=0、skipped=0、flaky=0，并生成 `browser/passed.json`。未将原完整脚本非零退出码描述成零，也未重复已通过且未改动的后台、前端和打包阶段。

| Provider | 数据库 Passed / Failed / Skipped | 本轮数据库报告目录（相对仓库根） |
| --- | --- | --- |
| MySQL | 56 / 0 / 0 | `output/database-tests/MySql/d7f8383bdf194e6d8e50cfa8c3dcf29e` |
| SQL Server | 56 / 0 / 0 | `output/database-tests/SqlServer/b8cd33f0173d4a509430c33447617e7a` |
| PostgreSQL | 56 / 0 / 0 | `output/database-tests/PostgreSql/5c6a297ddb6b4daa9970c6dc00f39d6c` |

三份 TRX 均已解析，manifest 为 validated=true / testExitCode=0。日志 `output/compat-mysql.log`、`output/compat-sqlserver.log`、`output/compat-postgresql.log`；汇总 `output/compat-database-evidence.json`。

完整演练入口退出码 0，日志 `output/compat-rehearsals-final.log`，产物 `output/rehearsals/95b97a700d9d455997adb81611690ef8`。实际执行的是隔离环境，不是生产环境。

| Provider | seed checks | auth checks | native restore checks | real TCP network checks |
| --- | ---: | ---: | ---: | ---: |
| MySQL | 20 | 11 | 23 | 12 |
| SQL Server | 20 | 11 | 23 | 29 |
| PostgreSQL | 20 | 11 | 23 | 12 |

已解析 12 份阶段 JSON，所有 checks 均成功；每种恢复报告都包含 `RestoredRetainedResultReplaysWithoutWrites` 和 `RestoredOutboxKeepsCurrentEventIdentityAndPayload`。演练 manifest 的工具程序集 SHA256 与本次实际 DLL 一致。

演练第一次在种子准备阶段因直接修改受保护实体而失败，保留日志 `output/compat-rehearsals.log` 和 `output/rehearsals/8f2e629d36b34c9ea48c5dfc162a399b`。修复种子上下文后重新执行所有 Provider、所有阶段成功；没有放宽应用写保护或变更生产 DI。

本轮测试及演练临时容器均已由脚本按所有权校验清理。最终汇总 `output/compat-validation.json` 记录各阶段成功及完整脚本的原始退出码。最终源码引用检查与 `git diff HEAD --check` 通过。

复验入口（仓库根目录）：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
$env:DOTNET_PROCESSOR_COUNT = '4'
pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-rehearsals.ps1' -Provider 'All' -SkipBuild
```

Skip 参数要求已有对应构建/依赖；全新环境去掉它们。测试和演练只创建自己的隔离数据库/容器，不修改用户已有数据库或附件。

本轮未提交、推送或发布。旧本地开发库由开发者按需重建；程序不自动清库或篡改迁移历史。旧软件兼容已删除，当前业务、安全、版本绑定与可靠性能力继续保留。

## 遗漏复查与补齐（2026-11-04）

用户追问遗漏后，按构造转发、实际参数使用及调用图补查，发现第一轮遗漏三个入口：

- `Background/BackgroundServices.cs` 的 ApprovalOutboxDispatcher 旧四参数构造器忽略 signal/options/logger，转发当前构造器；已删除。
- 同文件 ApprovalTimeoutScanner 的旧五参数构造器忽略 options；已删除。当前 DI 直接选择四参数构造器。
- `Persistence/ApprovalDbContextAccessor.cs` 的授权/命令/操作人三项可缺省；仓库只有完整 DI 调用。改为必填，删除无实际消费者的 MissingApprovalAuthorization 占位及 Engine 中不再可达的缺少命令执行器分支。实际权限、可信身份和严格幂等检查继续执行。

这次仅涉及构造/API 与无用分支，无数据库模型、HTTP、UI 或消息协议变化。

补齐后重新运行整个后端 solution：Passed 661 / Failed 0 / Skipped 0（460 + 86 + 56 + 59），退出码 0，日志 `output/compat-omissions-backend.log`。既有 `SingleWorker_RetriesOutboxAndScansTimeoutsAtTheirOwnIntervals`、`RegistersExactlyOneHostedWorker_AndCanDisableIt`、权限/身份拒绝及严格幂等回归均在其中执行。

重新生成后核对公共 API 差异：仅两个旧后台构造器删除，以及 accessor 三个参数由可空/可缺省变为非空/必填；其余程序集/OpenAPI/Flow/TypeScript 契约无新增变化。`contracts:check --no-build` 通过，日志 `output/compat-omissions-contracts-check.log`。

重新打包并验证独立 NuGet/npm 消费者通过，Core-only 18 checks 通过，日志 `output/compat-omissions-packages.log`；本轮消费者目录 `output/engineering/consumer-d72aa80582c54dce85c08558f3d584ad`。本轮未重复没有相关变化的浏览器、三种服务数据库及认证/恢复/TCP 演练，上一节明确记录它们上一轮的结果和产物。

该次复查集中于构造转发及其依赖；下一节继续补查方法重载、运行态字段和 JSON 入参。历史报告、异常数据测试描述、业务发布版本绑定、当前通知默认规则与完整性/权限保护均按其真实用途保留。样例及测试的独立流程 JSON 文件也复查了显式当前版本。

## 深度复查与补齐（2026-11-04）

这次按声明、调用点、序列化生产者/消费者及 Git 历史逐项核对，不仅匹配“兼容”注释。确认并处理以下遗漏：

| 范围 | 修改与依据 |
| --- | --- |
| 治理列表旧重载 | 分类筛选在 76631ca 中新增后，原 CancellationToken-only 签名继续转发。删除转发重载，当前签名的 category 默认 null；ListCategoriesAsync 使用命名 ct 传递取消令牌 |
| 旧校验组合入口 | 删除 Core 的 knownApproverTypes-only 重载及 Forms 的同形静态组合方法。只有仓库测试使用后者，已迁移为 FlowDefinitionValidator.Validate(flow, context)，显式配置 Forms 扩展。当前宿主发布继续通过带权限的 ValidateAsync/PublishAsync 路径 |
| 冗余并行状态 | ActivationId 在生产代码中只有声明、默认生成与序列化，没有读取者；Arrived 与 ArrivedBranches.Count 表达同一数据。删除两个字段，汇聚直接依赖去重集合的 Count；Expected 与 ArrivedBranches 继续持久化，等待全部分支、嵌套、退回及取消语义保持 |
| 前端旧任务形状 | 当前 HTTP 响应及 ApprovalTask 类型要求 round，删除 flowRuntime.ts 对缺失 round 补零的分支，直接按真实轮次筛选 |
| 非对象 JSON 异常 | 直接解析及 HTTP 回归确认 null、数组、数值根节点抛 InvalidOperationException，发布接口原返回 500。先检查根节点为对象，统一返回 InvalidDefinition/400；新增 6 个回归用例，并检查无定义、治理、审计、幂等、实例或 Outbox 残留 |

JSON 根节点问题的失败证据：`output/compat-deep-json-red.log`（3 failed）和 `output/compat-deep-http-red.log`（3 failed / 3 passed）。修复后对应日志 `output/compat-deep-json-green.log`（3 passed）和 `output/compat-deep-http-green.log`（6 passed），均退出 0。用例参数使用安全 caseId，没有将 JSON/连接信息放入测试名称。

| Requirement | Evidence |
| --- | --- |
| 非对象输入统一拒绝 | Non_object_root_is_rejected_as_invalid_definition：断言具体异常类型与 InvalidDefinition 错误码 |
| HTTP 拒绝且不留下写入 | Publication_RequiresExplicitCurrentSchemaVersion_WithoutPartialWrites：断言 HTTP 400、错误码和六类表状态；显式当前版本成功的对照仍通过 |
| 并行状态保持真实分支语义 | Repeated_branch_completion_cannot_release_join_and_rejection_cancels_siblings：断言预期两条分支、唯一已到达分支为 1、未提前生成终审，驳回后取消兄弟分支；Nested_parallel_and_condition_inside_branch、Return_inside_a_branch_keeps_sibling_branch_progress 覆盖嵌套与退回 |
| 治理查询语义与权限保持 | GovernanceSummary_LoadsOneProjectionAndPreservesPerResourcePermissionChecks；GovernanceAndCategories_GlobalDenialStopsBeforeResourceQueries |
| 前端使用真实轮次 | flowModel.test.ts 的 uses the latest round after a return：再次提交后显示新轮次的待办状态 |

此次没有 EF 模型、迁移或宿主配置变化。不会删除已有开发数据库或附件；当前可选字段、元数据默认描述、业务版本绑定、通知路由、安全写包装和稳定键恢复均保留。

深度复查的最终验证：

- 定向后端 65 passed；修复 JSON 根节点后重新执行完整 solution：667 passed / 0 failed / 0 skipped（Core 463、HTTP 89、SQLite Database 56、Certificate 59），退出 0，日志 `output/compat-deep-backend-final.log`。
- 前端 flowModel.test.ts 21 passed；UI/demo typecheck 与库构建通过。日志 `output/compat-deep-ui.log`、`output/compat-deep-typecheck.log`、`output/compat-deep-build-lib.log`。
- 契约重新生成及 `contracts:check --no-build` 通过。对照补查前 API 快照，差异只有三个旧转发入口删除、治理参数默认 null，以及两个运行态属性删除；没有额外公共 API 变化。日志 `output/compat-deep-contracts-update.log`、`output/compat-deep-contracts-check.log`。
- 四个 NuGet 包及 npm 包的独立消费者全部通过，Core-only 18 checks 包含 JoinWaitsForOtherBranch、ParallelJoinWithoutForms、StableOperationReplay。日志 `output/compat-deep-packages.log`，消费者 `output/engineering/consumer-ce373ce2322b4106ba706f86f0ac5919`。已核对包与契约的实际 SHA256，匹配本次 manifest。

| Provider | Passed / Failed / Skipped | 本次报告目录 |
| --- | --- | --- |
| MySQL | 56 / 0 / 0 | `output/database-tests/MySql/4646007d8a3a465688ce9506042c2934` |
| SQL Server | 56 / 0 / 0 | `output/database-tests/SqlServer/dd4a1afa01124cdf91a2e8799b3b4bb9` |
| PostgreSQL | 56 / 0 / 0 | `output/database-tests/PostgreSql/36ba5999dc0545389a97f77bf68bb7e0` |

三份 TRX/manifest 已解析验证，全量结果均 Passed，validated=true、testExitCode=0；每份报告均实际执行 Parallel_ConcurrentBranchCompletionAndReplay_CreateOnlyOneDownstreamTask。脚本完成后按所有权校验清理了各自的临时容器。

本次未重跑无相关变更的浏览器及认证/原生恢复/TCP 故障演练，前轮结果见上文。最新汇总 `output/compat-deep-validation.json`，并记录到 `output/compat-validation.json` 的 deepAudit 节；历史阶段的原始退出码未覆盖。最终 `git diff HEAD --check` 通过。

对当前源码、实际生产者/消费者、公开签名和相关回归的本轮检查，未再确认其他旧软件版本兼容入口。此结论限定于本报告的审查范围；未将正常可选配置、业务版本或错误恢复当作兼容代码删除。本轮未提交、推送或发布。
