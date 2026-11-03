# 第三轮审批边界改造报告

本文记录第三轮提交的实际结果。RC 阶段根据项目未发布、无生产历史数据的事实，删除了旧维护字段与迁移残留，直接修正 Scope 命名；本文关于保留 ArchiveId／DeletionPending、旧参数名称和旧迁移的说明不代表当前要求。当前状态与验证见 [RC 报告](rc-report.md)。

日期：2026-11-03。基线：`cdb25539da587cea33f2fd50324e9c5f3f2e77a1`。目标分支：`codex/engineering-extensibility`。本地完整验证已成功，结果如下；最终 Commit SHA 与匹配 HEAD 的 CI 链接由交付回复给出。

## 改造结果与兼容性

| 原有问题 / 根因 | 方案与主要文件 | 结果 / 兼容性 |
| --- | --- | --- |
| Task/Instance/Operation 等普通跟踪写入没有内部来源校验；UOW 活跃不等于权限 | `src/Approval.Core/Persistence/ApprovalWriteAuthorization.cs`、`ApprovalPersistence.cs`、`ApprovalUnitOfWork.cs` | 按实体、所有者、属性和预期值授权；拒绝宿主修改/新增/删除、夹带修改、伪造 Attach；业务实体不受此所有权限制 |
| Core、治理、消息、Outbox、Forms 写入职责不同 | Core Engine/Definition/Message/CarbonCopy/Outbox 服务；Forms Draft/File 服务 | 权限分开；只追加历史规则不因内部授权失效；Forms 不获取核心状态权限；Core 不引用 Forms |
| SaveChanges 无法覆盖 bulk，旧 QueryGuard 只覆盖三类实体且沿第一个 root 推断 | QueryGuard 按真实 EF 方法及泛型目标；`ApprovalSqlWriteGuard.cs` 执行时检查 | 普通审批 bulk 拒绝，匿名投影/缓存路径执行时也保护；消息全部已读使用有界批次，业务 bulk 可读审批表作筛选 |
| 原 SQL 保护仅 non-query/Contains(tableName)，误拦读取、遗漏返回行路径 | CommandGuard 覆盖同步/异步 reader/scalar/non-query，按普通 DML 标识符分析 | 保护可识别 DML，不声称完整 SQL/数据库权限隔离；业务 SQL 只读引用、字面量/注释不误拦 |
| 宿主回调、DTO getter、延迟集合/时钟读取可能继承写权限 | `ApprovalHostServices.cs`、Engine 输入/定义快照和 CommandExecutor 结果序列化边界 | 回调暂停授权；允许当前申请的窄快照准备；拒绝任务/历史/操作记录篡改，不把序列化变成 DI 绕过入口 |
| 后置宿主 SavingChanges 可在第一次校验后引入变化；Save(false) 的手动 Accept/池化生命周期需兼容 | 保存后/命令前快照；已保存未 Accept 的凭据；DbContextId 租期状态重置 | 命令前拒绝新变化；保留原 override、并发、Save(false)/AcceptAllChanges、池化新租期与失败 scope 规则 |
| Handler 可选解析使配置错误延迟到投递时才暴露 | `Hosting/ApprovalHostValidation.cs`、注册、Worker/Dispatcher、Outbox options | 开启投递时启动检查真实注册/构造；新增 EnableDispatcher；无空处理器。BackgroundServices=false 仍允许手动投递 |
| 严格模式只检查 UOW.IsActive，可被公开 UOW 代替 OperationId | CommandExecutor 预约成功后记录内部带键来源，UnitOfWork 检查 | 直接服务/HTTP 一致；默认 RequireOperationId=false 保留，严格宿主需要完整带键委托 |
| 批量改派 AllowUnkeyed，重试重新查询已改派候选会丢结果 | `ApprovalEngine.Admin.cs` 的兼容重载、持久候选清单/逐项键/汇总；HTTP Header | 逐实例事务不变；部分完成原键恢复；相同键不同内容冲突；原签名不删除 |
| 身份/权限安全默认需要固定 | 启动必需身份检查；保留权限/QueryScope 超时、actor 等值与 system 能力上限 | 普通角色 Admin 不隐式授权，缺 Provider 人工拒绝；缺身份/冒充/权限异常不允许写入 |
| 字段权限和 channels 仍是已发布契约；直接拆掉会破坏历史 JSON | Forms 扩展职责、现有 DTO/JSON 保留；文档/注释更新 | 实际字段规则归 Forms；版本、旧 JSON、历史实例、公共类型不迁移。本轮只做兼容收尾 |
| Core-only 消费者未像其他消费者一样核对包字节 | `eng/verify-packages.ps1`、EmbeddedHost | 每个 Core 包核对 sha512，并通过实际包运行直接写入/bulk 拒绝及正常审批/业务保存 |

## 安全与宿主行为证据

新测试使用真实 SQLite DbContext；数据库矩阵病例通过各 Provider 的正常注册运行。准备损坏/历史数据的低层 fixture 使用独立离线 context，安全测试不使用内部写入豁免。

| Requirement | Evidence |
| --- | --- |
| 宿主 Task/Instance/Definition/Operation/Log/Slot/Outbox/Governance 写入拒绝且数据不变 | `HostTrackedWrites_AreRejected_AndDatabaseRemainsUnchanged`；`HostCannotAlterCommittedOperation_AndOriginalKeyStillReplays` |
| 普通/投影/匿名投影与缓存 bulk 拒绝 | `HostBulkWrites_AreRejected_BeforeExecutingSql`，分别测试 update/delete/projected/anonymous，并断言是审批保护错误 |
| 原生 DML 的同步/异步/返回行拒绝，合法业务 SQL 保留 | `HostSqlDml_IsRejected_InEveryExecutionPath`；`BusinessRawSql_MayMentionAndReadApprovalTables` |
| 公开 UOW、调用前/后修改、伪造 Attach、后置拦截器无绕过 | `PublicUnitOfWork_DoesNotAuthorizeHostApprovalChanges`；`ForgedUnchangedAttachment_CannotChangeEngineAssigneeValidation`；`HostInterceptorAfterSaveGuard_CannotIntroduceApprovalChanges` |
| 数据编辑及输入/结果 getter 不获得写权限 | `PublicDataEditCallback_CannotChangeTaskStateOrPersistedHistory`；`InputGetter_CannotInheritEngineWriteAuthority`；`ResultGetter_CannotInheritOperationWriteAuthority` |
| Forms 正常操作与宿主修改 Draft/File 拒绝 | `HostCannotModifyFormsData_WhileNormalFormsServicesWork`；现有 Draft/File/FieldPermission/Form 回归 |
| 宿主业务、Identity/custom context、显式 Accept 和并发继续正常 | `BusinessSaveAndBulkWithApprovalReadFilter_ContinueToWork`；`ExistingHostContext_PreservesBusinessSaveApprovalTransactionConcurrencyAndOutbox`；`PooledHostContext_NewLeaseDoesNotInheritFailedTransactionOrWriteAuthority` |
| 缺 Handler/不可构造 Handler/缺 Identity 启动失败；显式关闭保留 Pending | `EnabledDispatcherWithoutHandler_FailsApplicationStartup`；`RegisteredHandlerThatCannotBeConstructed_FailsStartup`；`MissingIdentityProvider_FailsStartupInsteadOfWaitingForCommands`；`ExplicitDispatcherConfiguration_PreservesOutbox_AndDeliversOnlyWhenEnabled` |
| 严格键不能被裸 UOW 代替；批量部分恢复/重放/冲突与 HTTP Header 一致 | `StrictMode_PublicUnitCannotReplaceAKeyedCommand`；`KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion`；`BulkReassignHeader_ReplaysOriginalSummary_AndStrictModeRejectsMissingKey` |
| 跨 Provider 的拒绝与正常引擎成功 | `ApprovalWriteProtection_RejectsHostTrackedBulkAndSqlButPreservesEngine`，已加入数据库脚本必需执行病例 |
| 幂等版本、事务/未知 COMMIT、并发、Outbox 重试/恢复、消息已读继续受保护 | 原 Operation/Atomic/Parallel/Notification/Permission/Cancellation/Recovery 套件保留，未跳过或降低数量 |

改动前完整后端基线：504 passed / 0 failed / 0 skipped。新增安全用例的首次运行：14 failed / 2 passed，真实复现旧缺口；这些预期失败不计入最终通过结果。最终只采用结束且非空、无跳过的报告/脚本结果。

## 实际验证

| 项目 | 当前结果 / 实际证据 |
| --- | --- |
| Windows Release / 全后端 / 九套 Migration | Release 0 警告/0 错误；Core 378、ASP.NET 81、数据库 SQLite 31、Certificate 58，共 548 passed / 0 failed / 0 skipped；九套模型均无漂移；ReferenceHost smoke 27 checks。四个最终 TRX 在 `output/round3/acceptance/`，已解析非空/全部执行/无失败无跳过 |
| SQLite / SQL Server / MySQL / PostgreSQL | SQLite 31/31；SQL Server 31/31（run e8773565812549098df40215b81882ee）、MySQL 31/31（ddd81aabe965427aa6566f3640e0ac1c）、PostgreSQL 31/31（22073d3dad4a474ea00c4829436e5fa3），全部 0 failed / 0 skipped。TRX/manifest 在各 `output/database-tests/<Provider>/<runId>/` |
| 前端 | frozen install、95 源文件 boundary、7 个 boundary 测试、typecheck、186 个单元测试、lib/demo build 在最终完整脚本全部成功 |
| 契约 / OpenAPI | 一个配置属性、一个兼容重载、批量 Header 元数据及 TS 已逐项审核；最终 contracts:check 成功，无其他 schema/公共签名漂移 |
| 独立消费者 | consumer-74229085574540f79b92c9e5e0931df6：Forms/Certificate、ReferenceHost、Core-only NuGet 与 npm tarball 均成功，5 个包摘要校验；Core-only 18 checks；浏览器 5 passed / 0 skipped / 0 flaky，ReferenceHost 包 smoke 27 checks |
| CI | `.github/workflows/ci.yml` 的 Windows verify 与三库 matrix 以最终 HEAD 的真实 run 为准，链接见最终交付回复；不使用基线旧绿色 run 代替本轮结果 |

上述本地结果已汇总到 `output/round3/verification-summary.json`，包含报告路径、TRX 摘要和计数。本地验证在尚未提交的当前工作树执行，包消费者逐包核验当前构建字节；最终提交后由 CI 再核验相同代码。SQL Server 曾在一次本地并行负载运行中出现迁移锁/超时错误，失败报告保留；新隔离容器完整 31/31 重跑成功，未跳过/降低任何用例或修改历史迁移。

复现命令（仓库根目录 PowerShell）：

```powershell
pwsh -NoProfile -File './eng/verify.ps1'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
```

## 最终边界与后续

仍为宿主现有 Context、共享数据库、单 Worker、Core 单向模块依赖；没有新增 Redis、租约、分布式锁、维护/归档/保留体系、自动清理或第三方发送器。没有改动历史 Migration/表、ArchiveId/DeletionPending、存储键、HashVersion/ResultVersion/Tombstone 或旧发布 JSON。

原生 SQL 防护不是完整 SQL 解释器；直接连接、数据库管理权限、复杂动态 SQL/过程/视图/触发器等属于可信宿主边界，见 [写入边界](persistence-write-boundaries.md)。文件上传继续作为 Stream/FileStore 的非请求幂等例外并保留补偿，不能宣称跨系统 exactly-once。

P2 保留 FieldPermissions/FieldAccess/HiddenFields、channels/ChannelInfo 兼容结构；后续独立契约版本可采用模块 snapshot 双读适配，先验证旧定义/历史实例，再迁移。部分公开构造参数旧命名为 tenants 仍保留以兼容命名参数调用，其类型已是单宿主 IApprovalScopeFactory。

本轮之后进入稳定期；后续由实际业务缺口驱动，不继续改写引擎或基础事务系统。
