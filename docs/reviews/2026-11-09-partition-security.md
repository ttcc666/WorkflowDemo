# WorkflowDemo 多租户安全加固报告

基线：`ttcc666/WorkflowDemo` 的 `main` / `origin/main`，`de663808f9fa6d66649512a8d3e45a62ced26b77`。开始时工作区干净，已 fetch 并核对远端。本报告记录推送前的本地验收；2026-11-10 用户另行授权提交、推送并检查对应 SHA 的 CI。

**验收边界：EF 访问层的 SQL 和批量写入缺口已修复；本次不能宣称实现数据库级强隔离。直接 ADO.NET / 独立连接仍不经过 EF 拦截器，服务数据库也尚未完成本次修改的实际验证。**

## A. 问题清单

| 问题 | 原因与影响 | 修复方式 | 代码 |
| --- | --- | --- | --- |
| P0：EF 原生 SQL 可跨租户读写宿主业务表 | QueryFilter 不覆盖 SqlQueryRaw；原命令保护主要识别审批表写入；普通宿主表原生 SQL 无范围证明 | 分区 Context 拒绝 SQL 查询根、SQL 映射和不可信命令来源，覆盖同步/异步、FromSql、SqlQuery、ExecuteSql、编译/组合查询；不检查 SQL 是否包含 TenantId | `ApprovalPersistence.cs`、`ApprovalPartitionGuard.cs` |
| P0：ExecuteUpdate 可修改分区字段 | 批量 API 绕过 SaveChanges；EF 10 的批量调用表达式位于 EntityFrameworkQueryableExtensions，旧方法集合也未覆盖它 | 检查实际 setter 表达式；依据模型分区注解拒绝 CLR、EF.Property、投影与编译路径的分区赋值；未知形状拒绝；业务字段批量写入保留过滤 | `ApprovalPersistence.cs` |
| P1：通用分区 Context 的跟踪写入依赖宿主重复防护 | 示例有校验，但模型扩展没有统一配置写入条件、核心保存保护没有检查宿主分区实体 | 统一比较当前/原始分区；分区属性设为 concurrency token、AfterSaveBehavior.Throw；伪造自身分区的外租户 ID 在数据库条件处失败并回滚 | `ApprovalPartitionGuard.cs`、`ApprovalPartitionModelExtensions.cs` |
| P1：缺少、只含软删除或用 OR 放宽的过滤器 | “存在过滤器”不代表租户条件不可绕过 | 对过滤表达式结构验证不可绕过的 `实体分区 == 当前 Context.PartitionKey`，允许 AND 附加限制；其余 fail closed | `ApprovalPartitionGuard.cs` |
| P1：Context 范围改变/宿主范围缺失/池化复用 | 只在命令执行器入口校验不能覆盖普通查询和已编译查询；池化不重建构造器中的租户依赖 | 每次 EF 命令及保存验证绑定的非空 PartitionKey；DI Context 同时核对宿主 Capture；分区池化拒绝 | `ApprovalPartitionGuard.cs` |
| P1：未分区引用方仍可引用分区实体主键 | 旧模型检查仅覆盖待扩宽候选键的一部分引用 | 任何未分区引用方指向分区实体均在模型修改前拒绝，含 Owned；本次不引入另一套 Owned 隔离模型 | `ApprovalPartitionModelExtensions.cs` |
| P1：重复/乱序 Dispose 可恢复错误租户 | AsyncLocal 只存字符串，无嵌套 frame 身份或重复释放检查 | 不可变 frame、按嵌套顺序释放、重复释放无副作用；访问当前租户时复核启用状态 | `TenantScopes.cs` |
| P1：同名用户与目录服务跨范围误用 | Actor 只检查当前目录中存在相同 user ID；目录服务缓存构造时租户但不检查调用范围 | Actor 的可信 tenant claim 必须等于当前范围；目录服务每次访问检查绑定范围 | `TenantIdentity.cs` |
| P0 补充：内置 SQL 动作绕过 EF | 独立只读 ADO.NET 连接仍可读取所有租户；现有公开注册契约没有可信租户绑定 | 分区工作流在打开 SQL 动作连接前拒绝；非分区工作流保留该功能 | `SqlActionHandler.cs` |
| **未关闭：直接数据库命令** | `GetDbConnection().CreateCommand()`、独立连接、持有共享账号权限的代码不经过 EF；无法靠 EF 拦截器约束 | 已保留真实复现并在接入文档说明 RLS/账号隔离的部署前提；未以字符串匹配、可伪造白名单或连接包装声称已解决 | `DirectAdoNet_IsOutsideEfInterceptionBoundary`、`docs/host-data-partitioning.md` |

没有修改审批业务状态机、OperationId 规则、事务提交方式、Worker 数量、Outbox 投递语义或通知渠道。

## B. 修改文件清单

| 文件 | 目的 |
| --- | --- |
| `src/Approval.Core/Persistence/ApprovalPartitionGuard.cs`（新增） | 分区模型、Context 与 SaveChanges 统一校验 |
| `src/Approval.Core/Persistence/ApprovalPersistence.cs` | 查询根、批量 setter、命令来源安全规则；同步/异步复用同一规则 |
| `src/Approval.Core/Persistence/ApprovalPartitionModelExtensions.cs` | 分区 concurrency token / 不可变性；拒绝未分区引用 |
| `src/Approval.Core/Actions/SqlActionHandler.cs` | 拒绝无法证明租户范围的内置 SQL 动作 |
| `samples/Approval.MultiTenantHost/TenantScopes.cs` | 嵌套恢复、重复释放与目录状态检查 |
| `samples/Approval.MultiTenantHost/TenantIdentity.cs` | 身份 claim 与目录服务的当前范围检查 |
| `tests/Approval.Core.Tests/PartitionSecurityTests.cs`（新增） | 双租户真实数据库攻击测试及直接 ADO.NET 边界证据 |
| `tests/Approval.Core.Tests/ApprovalPartitionModelTests.cs` | 主键/Owned 未分区关联的模型拒绝测试 |
| `tests/Approval.Core.Tests/ApprovalPartitionQueryTests.cs` | 同步 SQL 默认拒绝契约，分区关联模型；加强非分区行为验证 |
| `tests/Approval.AspNetCore.Tests/MultiTenantSecurityTests.cs`（新增） | 真实示例数据上的攻击、身份、作用域和后台恢复测试 |
| `tests/Approval.AspNetCore.Tests/MultiTenantHostTests.cs` | 测试类拆为 partial；原生 SQL 正向预期改为拒绝 |
| `tests/Approval.Database.Tests/PartitionSecurityDatabaseTests.cs`（新增） | 同步/异步 14 个关系数据库安全用例 |
| `tests/Approval.Database.Tests/PartitionDatabaseTests.cs` | 同一模型提供 guarded 运行期 Context 和独立离线建表 fixture |
| `eng/verify-databases.ps1` | 服务数据库门禁要求全部 14 个安全用例实际执行 |
| `docs/host-data-partitioning.md` | 接入要求、行为变化、RLS 评估与直接数据库连接边界 |
| `samples/Approval.MultiTenantHost/README.md` | 示例的安全能力和生产限制 |
| `plans/2026-11-09-partition-security.md`（新增） | 实施范围、不变量、验证及回滚约定 |
| 本报告（新增） | 工程交付与验收证据 |

## C. 安全测试与证据

新增 **84 个执行用例**：Core 安全类 53 个、模型负向 2 个、示例宿主 15 个、数据库矩阵 14 个。其中 1 个是明确展示直接 ADO.NET 边界的证据测试，不能计为“直接 ADO.NET 已拒绝”的安全能力。原有安全断言未删除或放宽。

各攻击测试先写入 Alpha / Bravo 数据，再执行攻击，再使用独立 Context 核对双方数据及查询可见性。模型配置测试则断言拒绝发生在模型修改前。数据库 fixture 使用独立临时 SQLite 或脚本拥有的 `approval_test_*`，不修改用户数据库。

### 修复前失败证据

证据目录：`output/reviews/partition-security/`。

| 报告 | 结果 | 证明的原始缺口 |
| --- | --- | --- |
| `before.trx` | 30 failed / 8 passed / 0 skipped | EF SQL、分区批量迁移、伪造写入、Context 范围与缺失范围 |
| `scopes-before.trx` | 2 failed | 重复/乱序释放 |
| `sql-action-before.trx` | 1 failed | SQL 动作已调用独立连接工厂 |
| `additional-before.trx` | 缺少过滤器用例失败；SQL 动作 fixture 首轮设置错误单独记录 | 未配置过滤器仍可读数据；不将 fixture 错误当产品漏洞证据 |
| `filter-scope-before.trx` | 4 failed | 软删除替代、OR 放宽、宿主范围改变/缺失 |
| `foreign-before.trx` | 2 failed | 主键与 Owned 未分区引用 |
| `identity-before.trx` | 2 failed | 同名用户身份/目录服务跨范围复用 |
| `child-scope-before.trx` | 1 failed | 修复迭代中发现共享 disposed 标志会阻止父异步调用恢复；最终改为按当前 ExecutionContext 的 frame 链释放 |

### 需求与具体测试

| Requirement | Evidence |
| --- | --- |
| Alpha 正常读取且 Bravo 不可见 | `BulkBusinessWrites_KeepFiltersAndCannotTouchBravo`；`TenantFilterWithSoftDelete_PreservesBothBoundaries` |
| 跨租户跟踪修改/删除、无部分写入 | `TrackedWrites_RejectForeignPartitionWithoutPartialWrites`；`AttachedForeignId_WithForgedOwnPartitionFailsDatabasePredicate` |
| 原生 SQL 同步/异步与编译入口 | `RawSql_RejectsEveryEfEntryAndPreservesBothTenants`；`MappedSqlQuery_IsRejectedBeforeReadingStoredData` |
| ExecuteUpdate / Async 分区修改、组合/投影/编译 | `BulkUpdate_RejectsPartitionSettersAndPreservesBothTenants` |
| 普通业务字段安全更新；不能批量删除 Bravo | `BulkBusinessWrites_KeepFiltersAndCannotTouchBravo`；`CompiledDelete_RetainsTenantFilterAcrossContexts`（匹配不到 Bravo 时返回 0，不泄露其存在性） |
| IgnoreQueryFilters 同步/异步/命名批量操作 | `BulkFilterRemoval_RejectsAndPreservesBothTenants`；已有 `PartitionContext_RejectsFilterRemovalForEveryQueryShape` |
| 错误/变更/缺少 Context 范围 | `ContextPartitionChange_RejectsCachedOperations`；`MissingPartition_RejectsInsteadOfReturningUnscopedData`；`HostScopeChange_RejectsFixedContextEvenForCachedQuery` |
| 池化不能残留租户 | `PooledContext_RejectsEvenTheFirstLease`（本轮策略为拒绝分区池化） |
| 缺少租户过滤器、只有软删除或 OR 放宽 | `MissingFilter_FailsClosedBeforeReadingStoredData`；`SoftDeleteFilterAlone_CannotSubstituteForPartitionFilter`；`DisjunctivePartitionFilter_CannotMakeForeignRowsVisible` |
| 用户伪造 TenantId / 停用用户 / 同名用户混用 | 已有 `HttpBoundary_RejectsCrossTenantResourcesAndMissingPermissions`、`TrustedClaims_RejectInvalidTenantIdentity`；新增 `DisabledIdentity_CannotReadStoredTenantData`、`ReusedIdentityServices_CannotCrossTenantWithSameUserId` |
| 同业务幂等键跨租户隔离 | 已有 `SharedDatabase_IndependentKeysReplayAndApprovalOnlyChangesOwnTenant` |
| 复合 FK、唯一键、迁移往返及回滚 | 已有 `PartitionModel_MigrationRoundTripEnforcesTenantConstraints`、`PartitionModel_AllProvidersGenerateCompositeConstraintMigrations` |
| 未分区主键/Owned 关联 | `PrimaryKey_NonpartitionedReferenceRejectsBeforeChangingModel`；`OwnedReference_WithoutPartitionRejectsBeforeChangingModel` |
| 并行请求不串租户 | 已有 `ConcurrentCommands_PreserveTenantAndConvergeForSameOperation`（显式屏障同步） |
| 后台独立 Context、异常继续、恢复/重试原范围 | `BackgroundScopes_UseIndependentContextsRestoreAndRetryOriginalTenant`；已有 `Outbox_FailedTenantDoesNotBlockOtherTenantAndReceiptsDoNotDuplicate`、`Dispatcher_ValidationFailure_ContinuesOtherPartitionsAndRecoversNextRun` |
| 嵌套释放、重复释放与父子异步流 | `TenantScopes_InvalidDisposalCannotRestoreAnotherTenant`；`TenantScope_DisposeInChildFlowCannotPreventParentRestoration` |
| 内置 SQL 动作 | `SqlAction_PartitionedContextRejectsUnscopedRegisteredQuery`；原有 `SqlWorkflowActionTests` 验证非分区动作继续工作 |
| 原有非分区查询/原生 SQL/批量功能 | `NonpartitionedContext_CanRemoveHostFilters` 与全量后端回归 |
| 三服务数据库使用同一攻击测试 | `PartitionSecurity_ProviderRejectsAttacksAndPreservesOtherTenant` 共 14 个数据行；本机仅在 SQLite 执行 |
| 直接 DbCommand | `DirectAdoNet_IsOutsideEfInterceptionBoundary` 实际读取到两租户总行数；**要求未满足** |

### 执行结果

本地 `eng/verify.ps1` 于 2026-11-10 00:00（Asia/Shanghai）结束，**Verification succeeded: All，exit 0**。最终作用域修复另有 HTTP Release 全套复验。

| 检查 | 实际结果 |
| --- | --- |
| Release 解决方案构建 | 0 warning / 0 error |
| Core 全量测试 | Passed: 792, Failed: 0, Skipped: 0 |
| HTTP 最终代码全量 Release 复验 | Passed: 201, Failed: 0, Skipped: 0；`http-final.trx` |
| 数据库契约（SQLite） | Passed: 106, Failed: 0, Skipped: 0 |
| Certificate API | Passed: 59, Failed: 0, Skipped: 0 |
| 最终代码后端合计 | **1158 passed / 0 failed / 0 skipped**，比基线 1074 增加 84 项 |
| Core 分区专项 | Passed: 98, Failed: 0, Skipped: 0；包含新安全类 53 项；`core-partition.trx` |
| 示例多租户最终专项 | Passed: 53, Failed: 0, Skipped: 0；`host-security.trx` |
| SQLite 分区数据库专项 | Passed: 19, Failed: 0, Skipped: 0；含新攻击矩阵 14 项；`database-partition.trx` |
| Provider 模型与 DDL 生成 | SQLite / MySQL / SQL Server / PostgreSQL 均通过；不代表服务数据库执行通过 |
| 仓库所有 Context model drift | 全部通过，包括 MultiTenantHost 与四 Provider 模型 |
| ReferenceHost | 源码和独立包消费者各 27 checks passed |
| 前端单元测试 | 440 passed：shared 17、Vue UI 256、React UI 156、Vue Demo 6、React Demo 5 |
| 前端边界与 typecheck、库和 Demo 构建 | 通过；仅现有 bundle size 提示 |
| contracts:check | checked-in baselines 全部匹配 |
| NuGet / Vue / React 独立包消费者 | 通过 |
| 独立包浏览器 smoke | 12 passed |
| React / Vue packed E2E | 68 passed；完整门禁 exit 0 |
| 服务数据库 SQL Server / MySQL / PostgreSQL | **测试未执行**：脚本在环境准备阶段失败，原因如下 |

完整门禁首轮只有 1 个旧测试失败：更早拒绝审批表批量写入后的消息不一致；已统一生产代码错误消息与失败作用域语义，保留原断言，后续 Core 全量通过。完整门禁第二轮之后还修正了父子异步 Dispose；对应最终专项 53 项与 HTTP 全量 201 项已单独复验。Core/数据库/前端产品代码此后没有变更。日志：`output/partition-verify-all.log`、`output/partition-http-final.log`。TRX 计数、执行结果与所选目标数已解析校验，没有用 build 结果代替 tests passed。

三条服务数据库命令均实际调用，均在 `docker info` 阶段 exit 1：Docker Desktop Linux Engine named pipe 不存在。没有数据库容器启动，也没有数据库测试被执行。日志：`output/partition-db-SqlServer.log`、`output/partition-db-MySql.log`、`output/partition-db-PostgreSql.log`。

```powershell
# D:\Study\CSharp\Workflow
pwsh -NoProfile -File './eng/verify.ps1'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
```

推送前核对的 GitHub main CI 为 [42626555629](https://github.com/ttcc666/WorkflowDemo/actions/runs/42626555629)，SHA `de663808f9fa6d66649512a8d3e45a62ced26b77`，completed / success。**这是基线结果，不包含本次修改**。本次修改的远端验收须使用推送后新提交 SHA 对应的运行记录，不得引用本基线替代。

## D. 契约、配置与迁移说明

- 未新增或修改公开 C# 方法签名、HTTP/JSON/TypeScript 契约，没有引入依赖或兼容重载；契约检查使用 check，不更新基线。
- 非分区 Context 的原生 SQL、过滤器 API、业务批量写入与 SQL 动作继续工作；审批写保护保持拒绝。
- 多租户运行行为有意收紧：EF 原生 SQL 与内置 SQL 动作拒绝、分区字段批量修改拒绝、分区池化拒绝、未证明安全的过滤表达式拒绝。迁移继续走 EF Migration，不能用业务 Context 的 ExecuteSqlRaw 充当维护入口。
- 宿主需实现固定、可信的 `IApprovalPartitionContext.PartitionKey`，注册 `IApprovalHostScopeProvider` 并使用普通 scoped Context。非 DI 构造仍须由宿主绑定当前可信范围。
- 所有需要隔离的宿主业务实体必须加入 ConfigureApprovalPartition；全局共享实体不得引用分区实体。Owned 关联当前需显式建模为分区实体，不能默默留下不含分区的外键。
- 分区列现在统一具有并发与不可变元数据；仓库示例原本已这样配置，不产生新 DDL。仓库所有 Context 的 model drift 检查通过。本轮没有重建 Migration，也没有清库。
- 宿主需为 TenantId 配置合理长度及严格匹配目录标识的 collation。仅有 EF 的 `HasMaxLength` 并不意味着 SQLite 会强制长度；示例目录限制 64 字符，写入只能使用可信目录值。
- 多租户共享数据库在同一账号下仍是可信宿主模型。若威胁包含能执行任意 .NET/ADO.NET 或获取连接凭证的宿主扩展，需要独立权限边界，不能以本次拦截器替代。

## E. 最终安全结论

1. **两个 P0 的 EF 入口已加固**：原生 SQL 默认拒绝；分区字段批量赋值拒绝，安全普通字段更新和受过滤删除保留。
2. **P0 原生 SQL 目标未完全关闭**：直接 DbCommand / 独立连接仍可跨租户读取。本次报告明确保留此风险，不宣称“数据库访问层不可绕过”。
3. **不满足本任务定义的生产准入条件**：需要数据库级账号/RLS/物理隔离决策及验证，并完成 SqlServer、MySql、PostgreSql 真实数据库测试和包含本地改动的 CI。
4. PostgreSQL 与 SQL Server 可评估 RLS，但必须控制 policy 管理权、应用账号权限、可信租户身份和连接池会话；MySQL/SQLite 没有等价通用 RLS，应考虑租户独立数据库/账号等边界。详细对比见接入文档。本次未擅自改变部署模式。
5. 本地修改可供人工工程审查；不能作为“所有跨租户通路均已关闭”的安全证明。
