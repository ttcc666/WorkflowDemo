# 多租户生产能力：支持与验收

日期：2026-11-02（Asia/Shanghai）。阶段 6 的验证入口、最低场景与支持边界如下；实际运行数量、runId、制品摘要和最终状态记录在[实施记录](../plans/2026-11-02-multitenant-production-hardening-progress.md)。本文件不代表已部署或已向 NuGet/npm 发布。

## 支持矩阵

| 项目 | 验收组合 / 语义 |
|---|---|
| Runtime / SDK | .NET 10、ASP.NET Core 10、SDK 10.0.401；固定中央依赖 |
| MySQL | MySQL 8.4.10、MySql.EntityFrameworkCore 10.0.12、EF Core 10.0.14；TLS + utf8mb4；REPEATABLE READ |
| SQL Server | Server 16.00.4255、Provider 10.0.14；READ COMMITTED；不启用 MARS |
| PostgreSQL | Server 17.11、Npgsql Provider 10.0.3、EF Core 10.0.14；READ COMMITTED |
| SQLite | Provider 10.0.14，快速回归和参考宿主本机冒烟；串行写入，不代替三库证据 |
| 租户 | 共享库共享表、可信且不可变的作用域；显式 FixedTenantId 兼容旧单企业接入；不支持自动跨租户/分库路由 |
| 权限 | Core/HTTP 同一权限门禁；第三方允许仍须符合资源范围、审批资格和字段规则；失败/超时拒绝 |
| 事务 | 一个关系数据库中的完整操作原子提交；宿主准备/审批/命令结果/业务占用/Outbox 同事务；跨库/远程服务使用宿主适配或 Outbox |
| 重放 | 稳定键 + 原内容返回原结果；不同内容 409；结果清理后原键 410 且永久防重；每次核对当前读取资格 |
| 治理 | 独立草稿、并发发布计数和同事务审计；发布版本不可变；停用阻止新发起，原实例保持原版本 |
| 维护 | 默认关闭维护和删除；预览、保留、权限/策略复查、持久游标、租约 fencing、附件删除任务；活跃实例及未完成投递不按统一年龄删除 |
| 前端与包 | `@approval/ui` 类型/行为/构建和独立 tarball 浏览器；两个框架 NuGet 包独立消费者；证照与多租户宿主不可打包 |

Provider 的真实报告由 `eng/database-images.json` 的镜像摘要及 `packages.lock.json`、Server/Provider 版本、TRX 和 manifest SHA256 关联。未测试的数据库/驱动版本、不同 collation、亚秒级精度、DbContext pooling 和分布式事务不纳入一致性承诺。

## 最低验收矩阵

“三库”指每个真实引擎均运行对应测试；“受控故障”指真实数据库事务/查询上的拦截器或任务屏障，不把它解释为生产网络或进程故障演练。

| 编号 | 实际行为证据（精确测试名） | 验证层与边界 |
|---|---|---|
| I01 | `AtomicCommands_TwentyConcurrentIdenticalRequestsAcrossTwoHosts_HaveOneCommittedResult`；`ReferenceHost_TwentyHttpRequestsAcrossTwoHostsHaveOneProviderResult` | 三库：两个独立宿主、20 同键命令及真实 HTTP 请求，单一提交结果 |
| I02 | `AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent`；`ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges` | 三库：同键改内容冲突；无新增结果 |
| I03 | `AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent` | 三库：不同请求竞争同一业务，只保留一份 Running/占用 |
| I04 | `ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges`；`FormIntent_ReplaysBeforeLatestSchemaValidation_AndChangedBodyConflicts` | 三库 + HTTP：丢弃首响应再取回；发布新 Schema 后仍返回原 ID/DefinitionId；没有模拟传输层丢包 |
| I05 | `AtomicCommands_TenantIsolationAndReplayAuthorizationRemainMandatory`；`ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges` | 三库：相同键/人员/业务在不同租户独立；当前撤权拒绝读取重放 |
| T01 | `AtomicCommands_CallbackFailureRollsBackAndSameKeyCanRetry`；`FinalAcceptance_SynchronousActionFailureRollsBackHostApprovalOperationAndOutbox` | 三库：业务回调及同步动作失败回滚，原键可恢复；同步外部写不在本地回滚保证中 |
| T02 | `AtomicCommands_OuterRollbackUndoesHostAndMultipleEngineCalls_AndKeyCanRetry`；`HostUnitOfWork_CommitsMultipleEngineCallsAndNotifiesOnlyAfterCommit`；`OuterRollback_RemovesBusinessWorkflowAndSlots_WithoutClearingHostTrackerOrNotifying` | 三库持久状态 + Core 提交通知；外层回滚无业务/实例/命令/占用/Outbox |
| T03 | `AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` | 三库：异步动作提前 Save 后失败，动作数据回滚，保留正确投递状态和原 ID |
| T04 | `AtomicCommands_CommitFaultIsVerified_AndRetriesUseFreshContexts`；`AtomicCommands_UnavailableCommitVerification_ReturnsUnknownAndOriginalKeyRecovers` | 三库受控故障：commit 前/后异常、核验失败、干净作用域重试；未制造真实网络分区 |
| T05 | `AtomicCommands_OuterRollbackUndoesHostAndMultipleEngineCalls_AndKeyCanRetry`；`FinalAcceptance_BulkReassignmentReportsPerInstancePermissionAndKeepsOtherTenant` | 三库：两次引擎操作同工作单元；改派逐实例成功/跳过，不能放入外层原子工作单元 |
| N01 | `TenantIsolation_ModelReuseForeignIdsBulkWritesAndConstraints`；`ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges`；`HeaderCannotOverrideClaimsAndAdministratorCannotReadOtherTenant` | 三库实体/count/关联 + 真实宿主 HTTP 实例/任务；HTTP 附件/定义拒绝另由接口回归验证 |
| N02 | `TenantIsolation_ModelReuseForeignIdsBulkWritesAndConstraints`；`ReferenceHost_DirectoryRejectsAmbiguousDisabledAndNonmemberClaims`；`ReferenceHost_AuthenticationMembershipAndPermissionConsoleFailClosed` | 三库模型复用与写入边界；HTTP Claim/成员/伪造请求头；UI 切换缓存由前端回归验证 |
| N03 | `ReferenceHost_BackgroundScopesSkipDisabledAndIsolateTenantFailure`；`Rotation_SkipsDisabledTenantAndFailureCannotStarveLaterTenant`；`TenantBudget_CancelsHungWorkAndContinuesWithNextTenant` | 三库后台独立 DbContext、失败继续、停用跳过；Core 受控时间预算/轮转 |
| A01 | `DirectManagement_DeniedWithoutMutations_ThenAllowedSeparately`；`ExplicitPolicy_CanDesignAndValidateButCannotPublish`；`DiagnosticsPermission_DoesNotRequireAdminOrGrantRetry` | Core + HTTP：独立权限，不能通过前端或旧 Admin 参数绕过 |
| A02 | `PermissionFailureOrTimeout_FailsClosed`；`ResourceCapabilitiesAndDirectHttpCalls_ReflectRevocation`；`ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges` | Core 超时/失败 + 三库真实宿主撤权/恢复；未连接生产权限服务 |
| G01 | `Governance_ConcurrentDraftsAndPublishCounter_DoNotOverwriteOrDuplicateVersions` | 三库：双宿主陈旧草稿冲突、版本计数、审计 |
| G02 | `Governance_DisabledFlowKeepsOriginalInstanceAndTenantDraftsIndependent` | 三库：停用后新发起受阻，原实例继续；原租户草稿隔离 |
| O01 | `AtomicOutbox_ReplacedLeaseRejectsLateWorkerAndRollsBackItsWrites`；`AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` | 三库屏障/持久状态：迟到 worker 的本地写回滚，重新领取保持原消息 ID；未 kill 宿主/数据库进程或真实远程渠道 |
| L01 | `Lifecycle_TwoWorkersAndTransientBatchRetry_KeepOneArchiveAndDurableCursor`；`Lifecycle_FailedCommitLeavesNoArchiveOrCursor_OriginalJobCanResume`；`Lifecycle_PendingOutboxAndHoldBlockPurge_ThenOriginalKeyExpiresWithoutCrossTenantImpact`；`Lifecycle_OperationRetentionPersistsTombstoneThroughScopedSubqueries`；`Lifecycle_ArchivedAttachmentExpiryCommitsDeletionIntent_StorageFailureRetriesSafely` | 三库：批次恢复、保留/活跃保护、永久 tombstone、归档附件删除意图和存储失败重试；没有业务库清理 |
| D01 | `Migrations_RollBackAndReapply_WithoutModelDrift`；`LegacyUpgrade_RequiresMappingPreservesIdsAndContinuesPendingApproval`；`ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges` | 三库：旧链往返、显式映射升级/旧待办继续、参考宿主空库迁移重跑/回退；不能以 reference 初始迁移替换旧库历史 |
| C01 | `Three_reviews_are_required_before_real_certificate_is_created`；`ReferenceHost_DualTenantApprovalRevocationAndOriginalVersionReplay` | 固定租户证照三审、HTTP 参考宿主；独立 NuGet/npm 消费者/浏览器及公共 API/OpenAPI/TS/示例契约见完整验证入口 |

## 发布、升级与回退

1. **基线与发布版本**：保留当前未提交基线和旧迁移 ID。`CHANGELOG.md` 记录新增必填配置与行为变化；本地验收包版本沿用 0.2.0，它只是验证产物。正式发布须选用未发布的新版本，重建并验证，不能覆盖原包。`release-manifest.json` 关联源文件指纹、契约及包校验和。
2. **停写与备份**：维护窗口暂停旧写服务和 worker，备份数据库、附件与外部对账位置。先在副本演练，见[备份运维](backup-and-maintenance-operations.md)。不承诺零停机双写。
3. **存量预检**：明确单企业 FixedTenantId 或逐行归属映射；报告重复 Running、孤立关联、键冲突及旧同步外部动作，不猜测归属或自动终止冲突实例。
4. **迁移及适配**：宿主 Context 继承 ApprovalDbContext，调用 ConfigureApproval(this)，保留自身迁移历史并追加扩展/回填/约束、AtomicOperations、DefinitionGovernance、LifecycleMaintenance；接入可信主体、独立权限及 scope，业务准备纳入同一个可重试操作委托。参考宿主四套初始迁移只用于新库。
5. **协议切换**：客户端按逻辑意图保留稳定键，409 不能替换正文重试，结果待确认保留原键，410 不自动换键重做。过渡期无键配置保障范围写明；参考宿主严格拒绝无键写入。
6. **上线冒烟**：先关闭维护/删除，检查匿名 Ready、企业隔离、当前权限、在途原版本继续、业务占用及 Outbox；通过后开放新写入。告警关注授权失败、冲突、积压、租约与持久游标。
7. **回退边界**：新版本尚未写入可恢复旧二进制和已核验备份；开放新写入后优先前向修复。不能把旧程序直接指向新增租户模型或删列回退；必须恢复备份时记录截止点并补录/对账期间数据及远程副作用。

配置及接入细节：[租户](multitenancy.md)、[权限](permissions.md)、[事务](transactions-and-idempotency.md)、[治理](definition-governance.md)、[生命周期](diagnostics-and-lifecycle.md)、[参考宿主](../samples/Approval.MultiTenant.Api/README.md)。

## 真实服务演练补充

已完成本机隔离 Keycloak 26.4.7 HTTPS 认证、三库原生备份到新库还原、附件核对，以及实际 TCP 中断/commit ACK 丢失和原键恢复。完整结果及可复验入口见[真实服务演练](live-service-rehearsals.md)。这使用 Production JWT 管线和真实服务，未连接用户现有生产认证中心。

## 未运行边界

- 没有触发远端 CI、连接业务数据库、提交/推送代码、发布包或部署服务。
- 已连接隔离部署的真实 Keycloak HTTPS 发行者，验证签名/issuer/audience/expiry/成员边界；用户确认没有现成生产中心配置，未验收既有生产 OIDC/权限 SDK、组织同步或多实例权限传播。
- 已在三库链路实际停止 TCP 转发及丢弃 COMMIT 下行确认，核对直连提交记录并恢复原键；未制造生产网络拓扑分区、完整真实死锁、数据库进程崩溃、宿主断电或真实外部投递重复对账。
- 已执行三库原生备份文件的新库完整恢复、迁移历史/全部实体属性/附件 SHA256 核对及原审批/维护继续；未演练生产账号权限、加密密钥、生产备份及外部业务对账。迁移往返不是备份恢复的证据。
- 生命周期默认关闭；保留期、对象存储权限与恢复 RPO/RTO 由宿主明确配置并在自身环境演练。这里不公布未经测量的吞吐、容量、时间精度或可用性指标。

## 可复验命令

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $provider -SkipRestore -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw ('数据库验收失败：' + $provider) }
}
```

统一入口包含后端、四 Provider 参考模型漂移、真实 Kestrel 双租户、前端、契约及独立包浏览器。三库脚本创建带所有权标记的临时容器/数据库，顺序运行以避免启动争用；报告中零测试、失败或跳过均视为验收失败。
