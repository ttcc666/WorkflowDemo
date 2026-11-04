# 生产加固实施记录

日期：2026-11-02（Asia/Shanghai）。阶段 0–6 已完成本机验收。各阶段历史记录保留，最终交付、证据和未运行的生产环境边界见本文末尾。

## 基线

- 实际 HEAD：`8924e91d42119bd53efa204faaf270148707bdb1`。计划中 `f79b142` 已不是执行基线。
- 开始时无暂存/未暂存跟踪文件变更，只有未跟踪的原计划文件。保留当前示例拆分，不做 reset/restore，不创建提交。
- 原有测试：Core 268、ASP.NET Core 46、Certificate API 64，全通过，无跳过。
- 首次 Backend 命令与新增 solution 项目重叠，导致 `--no-build` 找不到新 DLL；此轮命令失败。完成构建后重新运行整个 Backend 入口，最终成功。

公共契约未修改，执行前摘要：

| 文件（contracts/） | SHA256 |
|---|---|
| Approval.Core.api.txt | 2EBAD42DC60D377FF1316F3D1C9B309A66F844F3A0D5BC877FDD0951B609BCDF |
| Approval.AspNetCore.api.txt | A9A48F6230D2EFC25B15ECDB1D7F57C7ACE9BB9E979CC6451C7A039910BA6BF0 |
| approval.openapi.json | 85CE6A3F9D42119D70D5F04A596EA08A2E38FB19E8136C3462CCC139E2B71810 |
| flow.schema.json | AF20EF2178191461C9E1A52B3843482EDE1EAF06DFB63BFC524620377AFBAEFD |
| form.schema.json | D468EB827519152971A8DADE86B6C40E252608E9172994858A107F4EBBB2CF54 |

## 阶段 0 交付

- 新增 `tests/Approval.Database.Tests`，引用真实 Core 模型、独立四套迁移/快照，加入 `Workflow.slnx`；普通 solution 测试使用临时 SQLite 文件。
- 中央依赖加入计划指定三库 Provider。测试锁文件 SHA256：`75DC8BAEFA292A1E77745B9BB52502E9EA071D880083A9A6637C3FFFDF1D235A`。
- 新增 `eng/database-images.json`（三个 Linux x64 镜像摘要）和 `eng/verify-databases.ps1`；CI 添加 Linux 三库矩阵，保留 Windows 作业。
- 脚本生成随机独立容器/测试库并核对所有权清理；真实 TRX 检查拒绝零用例和跳过；产物包含 SQL、版本、隔离级别、锁文件及制品摘要。
- 文档与行为证据表见 [数据库验证指南](../docs/database-validation.md)。没有修改 Core 运行时模型或证照示例迁移。

## 实际运行结果

| 环境 | Server | Passed / Failed / Skipped | runId / 制品位置 |
|---|---|---|---|
| MySQL | 8.4.10 | 7 / 0 / 0 | `output/database-tests/MySql/e23e79ea0e2d45518b5cb0deeaa1dfee/` |
| SQL Server | 16.00.4255 | 7 / 0 / 0 | `output/database-tests/SqlServer/a085d311723a4dd89f104edde63b98a1/` |
| PostgreSQL | 17.11 | 7 / 0 / 0 | `output/database-tests/PostgreSql/12f2e4ef87264c03878757a5aacf4e9e/` |
| SQLite 单独执行 | EF Core 10.0.14 | 7 / 0 / 0 | `output/database-tests/Sqlite/sqlite.trx` |
| 完整 Backend 入口 | .NET 10 / Release | 385 / 0 / 0 | 四个测试项目；证照迁移无模型漂移；退出 0 |

成功执行：

```powershell
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'
dotnet test 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj' --configuration 'Release' --verbosity 'minimal' --logger 'trx;LogFileName=sqlite.trx' --results-directory 'output/database-tests/Sqlite'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer' -SkipRestore -SkipBuild
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql' -SkipRestore -SkipBuild
```

另外，在 `web/` 执行 `pnpm 'contracts:check' '--no-build'` 通过，所有契约匹配；PowerShell AST 解析及 `git diff --check` 通过。用 `Database=master` 调用 `-UseExistingDatabase` 的负向验证在连接前被拒绝，退出 1。最终没有遗留带 `approval.test-run` 标签的容器。

## 已解决的实际问题

1. Restore 报告 SQL Server 传递依赖 Pkcs 9.0.11 漏洞；测试项目显式固定 10.0.14 后构建零警告。
2. MySQL 首次连接关闭 TLS，SHA2 认证失败，7 项失败；强制 TLS 后重新执行。
3. MySQL 第二次运行发现中文名称和处理人被写成 `?`，5 项通过、2 项失败；显式设置连接字符集 `utf8mb4` 后，7 项全部通过。失败 TRX 同样保留，不把失败运行覆盖成绿色。

测试质量复核：去掉定义唯一索引会使重复写入断言失败；移除并发令牌会使陈旧更新断言失败；去掉 UTC 转换会使 Kind 断言失败；回滚遗漏会被新 DbContext 的数据库结果断言发现。这些是静态变异推理，未运行 mutation 工具。保存点用例使用独立上下文核实提交结果，没有用内存跟踪状态代替持久化验证。

## 阶段 0 会话的接续范围（历史）

阶段 1 起实施可信租户/操作人上下文、固定租户兼容、九类实体过滤和写入保护、复合关联、旧库回填预检、后台/文件/UI 缓存隔离。后续仍须独立完成权限、工作单元与幂等、治理、诊断与生命周期、参考宿主及最终验收。

本机三库测试只证明阶段 0 的当前模型和七项基础契约，不证明整个最低验收矩阵通过。未运行远端 CI、完整前端/包消费者验证、旧库升级、三库故障注入、跨租户场景、备份恢复。

## 阶段 1 交付

沿用上述 HEAD 和阶段 0 工作区，不提交、不重置历史。变更覆盖：

- `src/Approval.Core/Abstractions/`、`Tenancy/`：不可变租户/操作人上下文、可信解析器、租户目录、独立后台作用域及受控租户模板配置。
- `src/Approval.Core/Domain/`、`Persistence/`：九类实体 TenantId；命名过滤器 ApprovalTenant；新增行填充、跨租户跟踪解除、原租户校验、并发条件；批量归属修改和审批表原始写保护；同租户复合外键与租户索引。
- `src/Approval.AspNetCore/`：唯一已认证租户 Claim，缺失/冲突拒绝，未知/停用企业拒绝，请求头/请求体不覆盖归属；`/me` 增加 tenantId。
- `Background/`、`Files/`、模板/表单：按启用企业限量轮转并隔离异常，默认本地存储键含租户 SHA256、旧键受元数据约束；支持租户时区、链接和模板变量。
- `web/packages/approval-ui/src/`：租户+用户缓存键，旧请求取消及迟到响应拒绝，用户/目录/页面数据与未提交表单/设计内容清空，避免确认弹窗期间切换后把旧内容提交给新企业。
- `eng/Approval.TenantTool/`、独立 Provider 与证照迁移：Expand → 显式映射/报告 → Harden；ID/历史定义不变，旧在途实例继续流转。回填工具默认预览，执行时重新预检、Serializable 事务、幂等重入、不允许改属；报告孤立/跨企业引用、重复进行中业务、潜在大小写/空白碰撞。支持显式保留 SingleRunningInstancePerBusiness=false 语义。
- 更新 README、CHANGELOG、[租户接入指南](../docs/multitenancy.md)、公共 API/OpenAPI/TypeScript 和示例契约。宿主须迁移到 ApprovalDbContext；单租户显式配置 FixedTenantId。现有 Admin 服务未自动改动或部署。

## 阶段 1 验收证据

| Requirement | Evidence |
|---|---|
| 固定作用域、缺失身份、停用企业 | `BackgroundScopes_AreImmutableAndDisabledTenantIsRejected`、`FilterBypass_IsRejectedAndMissingContextNeverFallsBack`、`MissingOrAmbiguousTrustedTenant_ReturnsUnauthorized` |
| 九类实体、同用户/业务键/流程键、模型缓存与 Find | `NineEntities_ModelCacheAndSameKeysRemainTenantIsolated`、`TenantIsolation_ModelReuseForeignIdsBulkWritesAndConstraints` |
| 跟踪、伪造更新/删除、批量写入、复合引用 | `ForeignAttachAndOwnershipChange_AreRejected`、`ForgedCurrentTenantEntity_CannotUpdateOrDeleteForeignId`、`BulkTenantMutationAndRawSql_AreRejectedBeforeWriting`、`CompositeForeignKey_RejectsForeignTenantDefinition` |
| 宿主附加命名过滤器保持租户条件 | `HostNamedQueryFilter_ComposesWithTenantFilter` |
| HTTP 管理员不能扩大租户边界、忽略伪造头、数量隔离 | `HeaderCannotOverrideClaimsAndAdministratorCannotReadOtherTenant`、`TenantSwitchWithSameUser_HasIndependentTodoAndDefinitions` |
| 后台公平性、失败隔离、停用企业不处理 | `BackgroundDelivery_FailureAndBacklogDoNotBlockOtherTenants`、`TimeoutScanner_ProcessesEachEnabledTenantAndLeavesDisabledTenantPending` |
| 附件物理键及访问隔离 | `LocalFiles_UseTenantStorageKeysAndRejectOtherTenantKeys`、`HeaderCannotOverrideClaimsAndAdministratorCannotReadOtherTenant` |
| 租户模板与安全选项边界 | `TenantTemplateSettings_RenderOnlyCurrentTenantLinkAndVariables` |
| 升级要求映射、拒绝跨租户关联/改属、可重入、旧审批继续 | `LegacyUpgrade_RequiresMappingPreservesIdsAndContinuesPendingApproval`（四库）及 `Legacy_upgrade_builds_a_ready_copy_without_changing_source_or_inventing_history` |
| 碰撞报告、并行模式兼容、全批预检拒绝 | `PotentialNormalizationCollisions_AreReportedWithoutRewritingKeys`、`ParallelBusinessSetting_IsExplicitAndNeverTerminatesLegacyInstances`、`OrphanAndUnknownMappings_BlockTheEntireBackfill` |
| 前端租户切换与迟到提交拒绝 | Vitest `isolates identical users when only the tenant changes and rejects late submission responses` |
| 独立包消费者 | `packaged designer publishes a workflow and three approvals activate the certificate`、`packaged endpoints reject anonymous access and unsupported protocol versions` |

最终 Backend 入口：Core **285**、ASP.NET Core **52**、证照 **64**、SQLite 数据库契约 **9**，合计 **410 passed / 0 failed / 0 skipped**；Release 构建零警告/错误，证照迁移无模型漂移。前端类型检查、UI **161** + 示例 **6** 测试、库/示例构建通过；边界检查 86 文件及 7 项边界测试通过，契约检查通过。独立 NuGet/npm 消费者构建和两项真实浏览器冒烟通过，最终消费者位置 `output/engineering/consumer-72afe93e56064874b1e1c924c07aa38e/`。

CLI 独立烟测：在随机 SQLite 测试文件迁移至 Expand，插入一条未映射草稿；预览 changed=0，执行 changed=1，重跑 changed=0，改属被拒绝（退出 1）。报告位于 `output/stage1/cli-*.json`。没有连接或迁移任何业务数据库。

三库每库 **9 passed / 0 failed / 0 skipped**，包含实际迁移、旧库升级及继续审批。最终制品位于 `output/database-tests/<Provider>/<runId>/`：

| Provider | Server | 最终 runId |
|---|---|---|
| MySql | 8.4.10 | `932f16337cf443cfa9993728b4c8dcd7` |
| SqlServer | 16.00.4255 | `a929197cf91648de8a483e859de91fea` |
| PostgreSql | 17.11 | `93eac68aa4c047d89a200505cb640910` |

远端 CI、真实业务库上线、备份还原及阶段 2–6 故障矩阵未运行。临时容器全部清理。

## 阶段 1 复核与边界

- 首轮三库测试抓到了测试宿主覆盖 OnConfiguring 时漏装基类拦截器的问题；最终基类封闭配置与 SaveChanges，并提供 ConfigureApprovalOptions 扩展点，三库重跑通过。
- SQLite 新复合键需要调整重建顺序；迁移前关联校验、事务外外键开关和完整 Expand 目标模型修复了重建/回退问题；原迁移 ID 和旧数据不重写。
- 命名过滤器避免宿主覆盖租户条件；附加过滤器也须命名，EF 会拒绝匿名/命名混用。原始 ADO 和宿主数据库凭据仍属特权边界，框架不是数据库 RLS。
- 静态变异复核：去掉过滤会破坏九类实体数量/跨 ID 断言；去掉跟踪或批量保护会破坏明确异常断言；去掉租户并发条件会破坏伪造实体的并发异常断言；接纳旧请求响应会破坏前端 AbortError 与缓存结果断言。未运行 mutation 工具。
- 新测试使用新 DbContext 验证持久化结果及精确错误类型，未以类型存在或镜像实现断言代替业务行为。内部测试研究/计划/复核记录留在 `.git/testagent/`，不进入提交。
- 维护工具单次上限 100000 行；更大数据量需宿主分批适配。潜在键归一化碰撞只报告，保留原值；完整跨 Provider 键语义和后续业务占用验收仍按计划推进。
- 下一阶段为阶段 2：独立权限码/provider、资源范围、可信操作人参数校验、能力响应、端点元数据及第三方权限替身验收；当前不宣称这些已经完成。


## 阶段 2 交付：权限接口

沿用原 HEAD 与阶段 0/1 工作区；未提交或重置历史。没有连接真实业务库，本阶段没有新增 schema 或迁移。

- 新增 `Abstractions/ApprovalPermissions.cs`：40 个稳定权限码（含未来停用/归档及三个系统任务权限）、可信主体、规范资源、权限 provider、查询范围接口。
- 新增 `Authorization/ApprovalAuthorizationService.cs`：Core 强制门禁；操作人参数匹配；先验证租户和资源范围，再调用外部权限；数据库规范化流程/业务信息；默认不缓存授权；provider/query scope 超时或异常返回 AuthorizationUnavailable/503，调用方取消保持取消。
- Core 覆盖发起、审批协作、管理、完整定义/实例读取、表单/草稿、附件、消息、投递和统计；查询范围在分页/count/聚合之前应用，批量改派再逐任务检查。默认业务资格、自审、字段及附件限制继续生效，伪造 ApprovalViewer.IsAdmin 不产生特权。
- ASP.NET 默认角色/DefinitionReaders/AdminPolicy 兼容，PermissionPolicies 按权限码覆盖；第三方 provider 注册不被默认适配替换。显式端点元数据和启动时路由表构建，使未分类端点在启动时失败。
- `/me`、实例和当前任务返回 capabilities；前端按钮、退回与驳回选项、设计/校验/发布、改派、监控、投递重试及证照诊断按实际能力显示。只读定义用户不显示设计入口。
- 系统作用域显式限定权限。证照启动发布与诊断监控分别获得最小权限；正常发起前预检使用发起权限和规范定义，避免为普通用户授予完整定义读取权限。新增 GetStartEligibleApproversAsync 及宿主 ScopeInstancesAsync 入口。
- 接入文档 `docs/permissions.md`、README、CHANGELOG、计划状态、C# 公共 API/OpenAPI/TypeScript/示例契约同步。阶段 3–6 尚未完成。

## 阶段 2 验收证据

| Requirement | Evidence |
|---|---|
| Core 直接管理调用允许/拒绝，无未授权写入 | `DirectManagement_DeniedWithoutMutations_ThenAllowedSeparately` |
| 表单、草稿、附件、消息、校验入口同样受限 | `FormDraftFileAndMessageEntries_DenyBeforeSideEffects` |
| 可信操作人，不允许传参冒用；规范化资源 | `ActorSpoofAndMissingIdentity_AreRejectedBeforeProvider`、`ProviderReceivesCanonicalResource_RevocationHasNoCachedAllow` |
| 独立设计/校验/发布、默认角色与 ASP.NET policy | `DefaultRoleMapping_SeparatesDesignAndPublish`、`ExplicitPolicy_CanDesignAndValidateButCannotPublish` |
| 诊断不需要 Admin，诊断不授予重试 | `DiagnosticsPermission_DoesNotRequireAdminOrGrantRetry` |
| 范围先于分页/角标、业务查询、消息/投递列表与批量改派 | `DataScope_AppliesBeforePaginationCountAndBulkReassign`、`BulkGrant_DoesNotOverridePerTaskDenial` |
| 权限服务错误/超时 503，查询范围失败不返回未过滤数据，调用方取消不伪装授权错误 | `PermissionFailureOrTimeout_FailsClosed`、`UnavailablePermissionService_Returns503WithoutPublishing`、`UnavailableQueryScope_DoesNotReturnUnfilteredRows`、`CallerCancellation_IsNotReportedAsAuthorizationFailure` |
| 撤权/租户切换不复用旧授权，跨企业资源不发送外部权限服务 | `ResourceCapabilitiesAndDirectHttpCalls_ReflectRevocation`、`ThirdPartyPermissions_ReevaluateTenantSwitch_AndNeverReceiveForeignResources` |
| 授权不能越过任务资格/自审，viewer 标志不能伪造 Admin | `AllowDoesNotBypassAssignee_AndForgedViewerCannotBecomeAdmin` |
| 系统主体最小权限；预检不冒用发起人、不创建实例 | `RestrictedSystemActor_CannotPublishOrImpersonateUser`、`StaffingPreflight_RequiresStartPermissionAndTrustedInitiator` |
| 全部端点元数据完整；新增遗漏导致启动失败 | `EveryMappedEndpointHasKnownPermission_AndNewUnclassifiedRouteFailsStartup` |
| 前端没有能力默认拒绝；退回/驳回区分；撤权/切租户清空 | Vitest `只显示单独允许的动作，未提供能力时拒绝显示`、`允许退回不代表允许驳回，节点禁止退回时仍不显示`、`权限使用服务端能力并在撤权和租户切换时立即清空` |

最终项目结果：Core **299**、ASP.NET Core **58**、证照 **65**、SQLite 数据库契约 **9**，合计 **431 passed / 0 failed / 0 skipped**。统一 Backend 入口构建零警告/错误、四项目回归及证照迁移模型漂移检查通过；最后补入一个直接扩展入口测试后单独复跑 Core，299 项通过，TRX counters 已核对。中间失败记录保留在 output/stage2，不能误用其中较早的 HTTP/证照 TRX 作为最终结果。

UI **164** + 示例 **6** 测试通过；前端类型检查、库/示例构建及 86 文件边界检查、7 项边界测试通过；契约生成差异检查通过。NuGet/npm 独立消费者构建和真实浏览器两项冒烟通过：`packaged designer publishes a workflow and three approvals activate the certificate`、`packaged endpoints reject anonymous access and unsupported protocol versions`。首次消费者为 `output/engineering/consumer-7a136e482aca46269bc2c36aa6cadd31/`；最后只读设计入口修正后，前端类型检查、164+6 项测试、库/示例构建重跑通过；最终独立消费者 `output/engineering/consumer-5d7081eb904f4fd5bb53874495f0c902/` 构建及两项浏览器冒烟通过（33.6 秒）。

三库每库 **9 passed / 0 failed / 0 skipped**（实际迁移、隔离、旧库升级及在途继续的既有数据库契约；新增权限矩阵主要由 SQLite/Core/HTTP 测试验收，不宣称全部权限场景均运行于三库）。本轮制品：

| Provider | Server | runId |
|---|---|---|
| MySql | 8.4.10 | `6d066064f38f483b9fce3adf3d6f89a5` |
| SqlServer | 16.00.4255 | `11ba40f82a974b1c9efc9e9fda2f293e` |
| PostgreSql | 17.11 | `956e161ed050436caf4820bd23b42213` |

各库报告位于 `output/database-tests/<Provider>/<runId>/`，包含 TRX、数据库/迁移信息和制品散列。临时容器由验证脚本清理。

## 阶段 2 复核与限制

- 最初完整定义读取收紧使证照普通发起预检返回 403；通过受发起权限保护的宿主内部预检和专用人员预检 API 修正，没有扩大前端完整定义读取权限。
- 最初后台恢复/监控缺少可信操作人，现通过正式后台或最小系统作用域运行。初次新增自审用例在构造新流程时即被发起规则阻止；最终改为明确的既有自指派任务，验证实际操作仍拒绝自审。
- Minimal API 约定本来延迟构建，单纯加 metadata 不能保证启动期发现遗漏；新增启动时构建路由表，已有遗漏端点用例通过。
- 旧算法测试显式使用测试内部授权替身；新增 Core 安全测试、HTTP、证照和包消费者使用实际生产门禁。没有生产兼容放行配置。
- 进行了静态变异与断言质量复核：去掉主体匹配、缓存允许结果、跳过租户或查询范围、移除逐项改派检查、吞掉授权服务异常、相信 viewer 标志或 UI Admin 推断都会破坏对应具体断言。未执行 mutation 工具，不宣称覆盖率。测试检查精确代码、资源字段、持久化状态、数量、正反按钮可见性和取消类型；内部记录在 .git/testagent。
- 权限适配器需遵守取消，避免后台悬挂；查询范围须可由 EF 翻译。宿主自己的数据库凭据、业务接口及自定义缓存仍是受信任边界。已有跨服务事务/ChangeTracker 回滚、提交结果未知和通用幂等仍属于阶段 3，不在本阶段宣称已解决。
- 未运行远端 CI、真实业务库上线、完整双宿主故障矩阵、备份恢复或阶段 3–6 验收。下一阶段为事务协调与通用幂等纵向切片。


阶段 2 复现命令（PowerShell，仓库根目录）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
'MySql', 'SqlServer', 'PostgreSql' | ForEach-Object {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $_ -SkipRestore -SkipBuild
}
```

All 入口包含 Backend、Frontend、契约和独立包浏览器验证；三库命令仅创建专用临时数据库容器。本轮最后 git diff --check 通过，数据库验证容器清理完成；没有执行提交、推送、部署或业务库迁移。

## 阶段 3 交付：事务协调与通用幂等

2026-11-02 接续被用量限制中断的实施聊天，核对当前工作区后完成最终验收。接续时 HEAD 为 `8924e91d42119bd53efa204faaf270148707bdb1`；保留已有修改，没有重置、提交或推送。

- `Abstractions/ApprovalOperations.cs`、`Persistence/ApprovalUnitOfWork.cs` 和 `ApprovalCommandExecutor.cs` 提供完整业务委托与最外层提交边界；每次执行策略重试重建作用域/DbContext，失败作用域作废，禁止裸外部事务和扩展自行提交。
- `Domain/OperationEntities.cs`、命令摘要、业务占用模型及四库独立迁移保存命令结果、原流程版本和业务占用。唯一范围包含租户、可信主体、命令/资源及原键；同内容重放、异内容冲突，提交阶段故障用独立上下文核验，核验失败明确返回待确认。
- 引擎、表单/草稿、定义、消息和受管 HTTP 写命令接入协调器；证照业务准备与引擎写入进入同一委托。批量改派维持逐实例部分成功，拒绝外层原子工作单元组合。
- 同步动作失败继续采用保存点及局部跟踪检查点；业务回调/同步动作禁止 SaveChanges 和自行提交。通知与 Outbox 唤醒等待真正提交；异步动作提前保存后的失败仍整体回滚，租约标识及并发令牌防止旧 worker 覆盖新结果。
- `web/packages/approval-ui/src/composables/useCommandIntent.ts` 维护用户/租户/页面隔离的稳定键；网络错误、503、待确认结果保留原键，阻止改内容自动换键。正文只保存摘要，浏览器存储失败时同页保留意图；补齐清理失败后不复用已完成键的验证。
- 更新 HTTP/公共 API/前端及示例契约、`docs/transactions-and-idempotency.md`、数据库支持文档和升级占用回填说明。旧同步副作用定义扫描、流程治理与生命周期仍属阶段 4–6。

## 阶段 3 验收证据

| 要求 | 具体证据 |
|---|---|
| 20 个同键请求跨两个独立宿主，只产生一组业务/审批/占用/任务/Outbox | `AtomicCommands_TwentyConcurrentIdenticalRequestsAcrossTwoHosts_HaveOneCommittedResult`；三库均运行当前版本 |
| 异键争用同一业务；同键不同内容冲突 | `AtomicCommands_DifferentKeysCompeteForBusiness_AndSameKeyCannotChangeContent` |
| 重放保留原流程版本及原结果，不重新校验最新版 Schema | `KeyedCoreStart_ReplaysOriginalVersionAndRejectsDifferentContent`、`FormIntent_ReplaysBeforeLatestSchemaValidation_AndChangedBodyConflicts` |
| 多租户独立，撤权仍禁止原键重放 | `AtomicCommands_TenantIsolationAndReplayAuthorizationRemainMandatory` |
| 宿主业务写入和两次引擎调用一起回滚，原键随后可重试 | `AtomicCommands_OuterRollbackUndoesHostAndMultipleEngineCalls_AndKeyCanRetry` |
| 宿主提交后才通知；回滚不通知，不清空宿主跟踪图 | `HostUnitOfWork_CommitsMultipleEngineCallsAndNotifiesOnlyAfterCommit`、`OuterRollback_RemovesBusinessWorkflowAndSlots_WithoutClearingHostTrackerOrNotifying` |
| 提交前故障使用新上下文重试，提交后故障恢复原结果 | `AtomicCommands_CommitFaultIsVerified_AndRetriesUseFreshContexts` 的两个参数用例 |
| 核验不可用返回待确认，原键恢复结果 | `AtomicCommands_UnavailableCommitVerification_ReturnsUnknownAndOriginalKeyRecovers` |
| 回调失败和异步动作提前保存后的失败整体回滚 | `AtomicCommands_CallbackFailureRollsBackAndSameKeyCanRetry`、`AtomicOutbox_SaveThenFail_RollsBackActionEvenWithRetryStrategy` |
| 旧 worker 迟到不能提交本地副作用或覆盖新租约 | `AtomicOutbox_ReplacedLeaseRejectsLateWorkerAndRollsBackItsWrites` |
| 取消边界、裸事务/批量组合拒绝、同步外部动作保护 | `CancellationBeforeCommitRollsBack_AndAfterCommitCannotUndoSuccess`、`RawExternalTransaction_AndBulkInsideHostUnit_AreRejectedBeforeMutations`、`ExternalSyncAction_IsRejectedByValidationAndPublish_UnlessExplicitlyOptedIn` |
| 前端原键恢复、身份隔离、正文快照及存储故障 | `useCommandIntent.test.ts` 共 8 项，含“浏览器存储不可用时，同页重试仍使用原键”“清理存储失败时，新提交不会误用已完成的键” |

最终统一入口 `pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore` 退出码 **0**。后端 Core **310**、HTTP **62**、证照 **65**、SQLite **20**，合计 **457 passed / 0 failed / 0 skipped**；前端 UI **172**、示例 **6**，合计 **178 passed**；边界测试 **7 passed**。构建零警告/错误，类型检查、迁移模型漂移及契约差异检查通过。完整控制台记录：`output/stage3/resumed-verification.log`。

独立 NuGet/npm 消费者 `output/engineering/consumer-020edb2edc4a4c6aba385af74cd31ef9/` 构建通过，真实浏览器 **2 passed**：设计发布后完成三审并激活证照；匿名访问与不支持协议版本被拒绝。浏览器结果见该目录 `browser/results.json`、`browser/passed.json` 和统一入口日志。

三库最终均为 **20 passed / 0 failed / 0 skipped**；已解析 TRX，确认双宿主用例存在，并核对 manifest 的制品 SHA256：

| Provider | Server | 最终 runId |
|---|---|---|
| MySql | 8.4.10 | `efdb684e8be147cda668ac57e056d211` |
| SqlServer | 16.00.4255 | `b0eafe63dbeb4fa0932fa212210fa4af` |
| PostgreSql | 17.11 | `aab5a7f5e7454f118d9a0d54bccf1278` |

证据索引：`output/stage3/database-evidence.json`；完整报告：`output/database-tests/<Provider>/<runId>/`。接续核对发现较早三库报告仍是单宿主用例，因此重新运行当前构建，没有仅按旧的通过数量宣称双宿主验收。三库同时启动的首轮 PostgreSQL 在测试数据初始化时超时，后续有连带失败（**16 passed / 4 failed**，`91f6cdb53529450491449dc6285c6dd8`）；保留失败报告，所有其他验证结束后原代码单独复跑 **20 passed**，最终证据只引用这一干净运行。未通过放宽断言、增加等待时间或跳过测试掩盖失败。

提交故障由真实数据库事务拦截器在 commit 前后注入，并模拟独立核验查询失败；这是恢复分支的验证，不是生产网络分区或数据库进程崩溃演练。外部通知仍为至少一次投递；旧 IApprovalNotifier 为尽力发送。附件物理清理、旧同步副作用扫描和破坏性维护尚未验收。

复现命令（PowerShell；三库脚本仅创建专用临时容器，按顺序运行以减少本机负载）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $provider -SkipRestore -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw ('数据库验收失败：' + $provider) }
}
```

阶段 3 六项检查已完成，下一阶段是阶段 4：流程治理。阶段 4–6 未实施；没有运行远端 CI、部署或业务数据库迁移。

## 阶段 4 交付：流程治理

2026-11-02 按已批准方案执行。开始/结束 HEAD 均为 `8924e91d42119bd53efa204faaf270148707bdb1`，保留前阶段工作区；未提交、推送或重写历史。

- `Domain/DefinitionGovernanceEntities.cs` 新增独立设计管理记录和治理审计；设计草稿不复用业务 `ApprovalDraft`。管理记录分别保存全局编辑令牌、草稿变更令牌、发布计数、当前选用版本及启停状态。
- `ApprovalDefinitionService.Governance.cs` 实现保存草稿、发布草稿、启停、选用历史版本、治理清单/详情/分页审计。并发令牌及唯一约束保护初始化和发布计数，竞争失败整体回滚并返回 409；旧直接发布入口使用相同计数与审计。操作人来自可信作用域，审计与命令/版本同事务落库。
- 发布版本不允许通过跟踪写入或 EF 批量更新改写；治理审计禁止改写/删除。`ApprovalDefinitionAvailability` 为新发起选择当前启用版本；停用不改变旧实例绑定和 Schema。旧宿主校验后显式绑定 DefinitionId 的语义保留，仍受停用限制。
- HTTP 增加治理、设计草稿、发布、启停、选用、审计和旧同步动作扫描接口，全部加入权限元数据及通用幂等边界。启停/选用写响应不泄露保存的设计 JSON；权限允许不等于敏感读取允许。
- 设计器增加保存草稿、刷新恢复、发布变更说明和资源能力按钮；`DefinitionGovernancePanel.vue` 提供启停、历史版本选择及分页审计；定义清单含未发布草稿与只读动作扫描入口。提交失败保留原键，发布重试不重复保存草稿或改变原令牌。
- 扫描历史同步外部副作用、声明自提交动作和无法解析/未注册动作，按版本分页返回节点/动作/在途数量，不输出参数、凭据或改写旧定义。`IApprovalActionHandler.CommitsWorkUnit` 支持声明遗留自提交行为，新发布拒绝此行为；无法从 JSON 自动推断未声明的任意自定义实现，须由宿主复核。
- 证照业务预检遵循治理选用版本，再向引擎显式绑定已校验版本；停用后新业务提交被拒绝，旧三审仍可完成。接入说明见 `docs/definition-governance.md`，README、发布记录、框架/示例公共 API、OpenAPI、前端类型同步更新。

追加五条 `DefinitionGovernance` 迁移及快照，旧迁移不变：MySql `20261102043258`、SqlServer `20261102043303`、PostgreSql `20261102043307`、Sqlite `20261102043312`、证照示例 `20261102043317`。创建管理/审计表、租户复合关联、唯一约束和编辑令牌；没有给旧发布人补造审计。旧流程无管理记录时保持默认启用，首次治理写入才初始化现有计数。

## 阶段 4 验收矩阵

| 要求 | 具体证据 |
|---|---|
| 草稿冲突不覆盖，不留下失败审计；发布记录来源、发布人、说明及摘要 | `DraftConflict_DoesNotOverwriteOrAudit_AndPublishPreservesSource` |
| 草稿变更与启停分离，发布停用流程不自动启用 | 同上，含启停后 HasDraftChanges 不变、停用后再次发布仍 IsEnabled=false |
| 双宿主保存/发布竞争不重号，只有成功方的版本、审计与命令 | 三库 `Governance_ConcurrentDraftsAndPublishCounter_DoNotOverwriteOrDuplicateVersions` |
| 停用禁止新发起，旧实例/任务和原键重放继续 | `DisabledDefinition_BlocksNewStartsButExistingTaskAndOriginalReplayContinue`；三库 `Governance_DisabledFlowKeepsOriginalInstanceAndTenantDraftsIndependent` |
| 显式历史版本选择、原发布 JSON 不变、跟踪/批量改写被拒绝 | `SelectHistoricalVersion_ChangesNewStartsWithoutRewritingPublishedJson`；既有 `Explicit_definition_binding_uses_the_validated_version_when_newer_version_exists` |
| 宿主回滚撤销设计草稿、发布、审计和命令结果 | `HostRollback_RemovesDraftPublishAndAuditTogether`；三库 `Governance_OuterRollbackRemovesDraftPublicationAuditAndOperation` |
| HTTP 原键重放、不同令牌冲突、原子审计与普通用户拒绝 | `DraftPublish_OriginalKeysReplayAndChangedRevisionReturns409_WithAtomicAudit`、`Disable_HidesStartableButPreservesHistory_AndOrdinaryUserCannotGovern` |
| 只有停用权限不能读取保存的敏感草稿 | `DisablePermission_DoesNotExposeStoredDesignDraftOrGrantSensitiveRead` |
| 旧同步动作扫描不执行、不输出凭据、不改变绑定与审计 | `ReadonlyLegacyScan_ReportsSyncExternalAndRunningVersions_WithoutCredentialsOrWrites`、`DeclaredSelfCommit_IsVisibleInLegacyScanAndRejectedForNewPublication` |
| 流程标识/名称长度边界、空 JSON、说明上限失败不留下半次发布 | `InvalidDraftIdentity_LeavesNoManagementOrAudit` 的四个参数用例；`DraftIdentityBoundary_AndMissingBodyAndLongNoteAreValidatedWithoutPartialPublication` |
| UI 原编辑令牌/原键重试，保存和发布分离，冲突保持本地内容 | `useDefinitionGovernance.test.ts` 三项：保存响应丢失重试、只发布已保存草稿且保持停用、冲突/缺少草稿拒绝 |
| 实际浏览器保存后刷新恢复、发布后三审完成；匿名及未知协议拒绝 | `packaged designer publishes a workflow and three approvals activate the certificate`（已包含保存/刷新/恢复/发布）；`packaged endpoints reject anonymous access and unsupported protocol versions` |
| 证照预检使用选用历史版本，停用无新业务，旧三审完成 | `SelectedHistoricalVersion_IsUsedByBusinessPreflight_AndDisabledFlowKeepsOldApproval` |

最终验证的后端为 Core **322**、HTTP **64**、证照 **66**、SQLite **23**，合计 **475 passed / 0 failed / 0 skipped**。统一 `verify.ps1 -SkipRestore` 首轮通过 Core322/HTTP64/证照65/SQLite23；最后证照预检选用版本修正后，完整证照 **66 passed** 与迁移漂移检查重跑通过；补强“停用后发布不自动启用”断言后，治理 Core 子集 **12 passed**。分别引用 `output/stage4/verification.log`、`certificate-final.log`、`governance-final.log`，不把首轮日志的 65 写成 66。

前端 UI **175**、示例 **6**，合计 **181 passed**；类型检查、边界检查及其 **7** 项测试、库/示例构建和契约差异检查通过。五种模型/快照漂移检查通过（四个数据库测试 Provider 与证照模型）。没有测量覆盖率或执行 mutation 工具；质量复核使用具体公开结果、固定 ID/版本/令牌、原 JSON、独立作用域持久状态、原键/权限拒绝和回滚计数，内部记录位于 `.git/testagent/`，不作为交付文件。

独立包首轮消费者 `consumer-9916e2eb760b4d7bbf253559711fbb8b` 完成构建和两项浏览器验证；证照修正后最终消费者 `output/engineering/consumer-f7f0ff22e8b24cd09f84696226bccb20/` 构建及真实浏览器 **2 passed / 0 failed**（27 秒），包含修正后的证照模块。报告见该目录 `verification.json`、`browser/results.json`、`browser/passed.json` 及 `output/stage4/packages-final.log`。

三库最终各 **23 passed / 0 failed / 0 skipped**，按顺序运行，均包含三项新增治理契约和既有双宿主/事务故障矩阵。TRX 计数和 manifest 制品 SHA256 已逐项核对：

| Provider | Server | runId |
|---|---|---|
| MySql | 8.4.10 | `ff50ab3645724698ab57e1c2979eb92d` |
| SqlServer | 16.00.4255 | `207b43390e72440c812bbb678db8d776` |
| PostgreSql | 17.11 | `468c94d4617c47ab9368f52d2a24b7f3` |

证据索引：`output/stage4/database-evidence.json`；完整报告：`output/database-tests/<Provider>/<runId>/`。验证脚本最低发现数更新为 23，临时容器均已清理。

修正过程保留失败日志：首轮 HTTP 验收宿主未注册表单服务，修正宿主配置后通过；完整 Core 回归发现显式绑定原版本的兼容入口被选用过滤收紧，保留其绑定语义后通过；证照迁移历史断言在新增迁移后需追加 ID，仍完整保留全部旧 ID。未通过跳过用例、放宽行为断言、增大等待时间或恢复旧源码掩盖失败。

可复验（PowerShell，仓库根目录）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $provider -SkipRestore -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw ('数据库验收失败：' + $provider) }
}
```

阶段 4 五项检查已完成，下一阶段为阶段 5：诊断与生命周期。尚未运行远端 CI、真实业务库迁移/部署或生产网络分区演练。归档、删除与保留策略仍未实现；旧同步动作报告不代表已完成自定义实现的人工迁移。

## 阶段 5 交付：诊断与生命周期

2026-11-02 按已批准计划完成；HEAD 仍为 `8924e91d42119bd53efa204faaf270148707bdb1`，保留原工作区与暂存状态，未提交/推送。

- `Diagnostics/` 新增通用诊断、受限数字/问题码贡献接口、后台心跳、ActivitySource/Meter。覆盖数据库/迁移、Outbox 积压/失败/租约、任务超期及合格人员、业务占用和文件删除；统计先应用实例范围，样本有限，数据库不就绪时显式 DataAvailable=false。
- `Background/ApprovalTenantSweeper.cs` 将投递、超时与维护改为限量的启用企业轮转，每企业执行预算、独立作用域与故障心跳；日志关联 ID/异常类型，不输出请求或连接。新增受限系统维护权限。
- `Maintenance/ApprovalLifecycleService*`、`Domain/MaintenanceEntities.cs` 增加归档快照、维护作业、不可改写批次审计、保留标记和附件删除任务。维护/删除默认关闭，所有可选保留期默认空；预览不创建任务，策略摘要改变需重读，每批重查数据状态及创建者当前权限。
- 每批使用新的作用域/完整事务，候选、历史快照、变更、游标及审计一起提交；租约和并发保护接管，失败批次不推进游标。运行中、待办/业务占用、Pending/Failed Outbox 及保留数据不会被统一年龄删除。
- 已结束在线实例、任务、日志等可在已有归档后清理；原定义和必要附件保留于历史路径。归档详情另有明确到期策略，到期才清正文并提交附件删除任务，标记/原摘要继续存在。命令正文到期转换为永久 tombstone，清正文及操作归档副本；原键 410，不重新执行业务，前端不会自动换新键。
- 文件增加归档关联、删除标记及并发令牌，排队后不可重新绑定。物理删除在任务提交之后执行，幂等失败重试；真正开始前保留可取消排队，已经开始不能承诺恢复。
- `IApprovalArchiveQueryScope` 在 count/分页/按 ID 导出前应用，独立敏感读取权限保护正文。宿主若有自定义在线范围而没有显式归档范围，拒绝并返回 503，不放宽为企业全数据；过期实例不能再依靠在线查询推断归档范围。
- HTTP 新增诊断、预览、作业/运行/批次记录、保留、历史导出与附件入口；可选匿名 Ready 探针正文只有布尔状态。UI 新增 `maintenance` 路由、诊断卡、预览/确认一批、游标/审计及历史检索。框架/示例公共 API、OpenAPI、前端类型与独立包同步。
- 新接入/保留说明 `docs/diagnostics-and-lifecycle.md`，三库备份、还原、告警及演练文档 `docs/backup-and-maintenance-operations.md`。证照示例绑定 Lifecycle/Background 配置与匿名探针。

四 Provider 和证照各追加 `LifecycleMaintenance` 迁移：MySql `20261102054527`、SqlServer `20261102054533`、PostgreSql `20261102054537`、Sqlite `20261102054542`、证照 `20261102054547`。创建五种维护实体及文件/命令列，保留所有旧迁移 ID。迁移只在专用临时验证库应用，未连接业务库。

## 阶段 5 Requirement / Evidence

| Requirement | Evidence |
|---|---|
| 默认关闭；只读预览无变更，不能创建删除任务 | `DisabledMaintenanceAndPreview_AreReadonlyAndCannotCreateDeletionJob` |
| 匿名仅布尔 Ready，详细诊断/维护/归档分别授权 | `ReadinessIsAnonymousBoolean_WhileDiagnosticsAndMaintenanceRequireIndependentRights`；三项真实包浏览器中的默认关闭/匿名探针测试 |
| 有限样本、无配置/任意异常文本泄露，诊断不修改数据 | `Diagnostics_AreReadonlyAndBounded_AndContributorsCannotExportSecrets` |
| 租户轮转，停用企业跳过，异常/挂起不阻塞下一企业 | `Rotation_SkipsDisabledTenantAndFailureCannotStarveLaterTenant`、`TenantBudget_CancelsHungWorkAndContinuesWithNextTenant` |
| 分批恢复、重复运行无重复归档，运行中/保留实例受保护 | `ArchiveBatches_ResumeWithoutDuplicates_AndProtectRunningHeldAndPendingOutbox`；三库 `Lifecycle_TwoWorkersAndTransientBatchRetry_KeepOneArchiveAndDurableCursor` |
| 真实提交失败不留下快照/游标，原作业可恢复 | 三库 `Lifecycle_FailedCommitLeavesNoArchiveOrCursor_OriginalJobCanResume` |
| Pending Outbox/保留阻止清理；原键不重做，其他企业不受影响 | 三库 `Lifecycle_PendingOutboxAndHoldBlockPurge_ThenOriginalKeyExpiresWithoutCrossTenantImpact` |
| 清理后历史可检索；永久防重/410 | `PurgeKeepsHistoricalSnapshot_AndPermanentKeyTombstonePreventsReexecution`、三库 `Lifecycle_OperationRetentionPersistsTombstoneThroughScopedSubqueries` |
| 文件先有持久删除意图，失败重试、排队绑定拒绝 | `FileDeletion_IsDurableBeforeStorageCall_FailureRetriesAndPendingFileCannotBind`；三库 `Lifecycle_ArchivedAttachmentExpiryCommitsDeletionIntent_StorageFailureRetriesSafely` |
| 归档附件可读，到期再删除，归档标记及原摘要保留 | `ArchivedAttachmentsRemainReadableUntilExplicitPayloadExpiry_ThenKeepMarker` |
| 独立家族保留期，失败投递只归档、发布计数不删、操作只清正文 | `AncillaryPolicies_KeepFailedMessagesAndDefinitionCounters_AndExpireOnlyOperationBody` |
| 只有 ArchiveRead 不能导出敏感正文，撤权/策略变更停止作业 | `ArchiveReadDoesNotGrantSensitiveExport_AndRevokedResourcePermissionStopsQueuedPurge` |
| 归档范围先于 count、分页及 ID 导出；缺少对应范围拒绝 | `ArchiveQueryScope_FiltersBeforeCountAndRejectsGuessedExportId`，含自定义在线范围但未注册归档范围的 503 |
| 前端 410 保留原键，显式核对才新建逻辑操作 | Vitest `重放保留期结束仍保留原键，核对归档后才允许开始新操作` |
| 请求/事务/后台观察及日志关联，不使用高基数指标标签 | `Diagnostics/ApprovalInstrumentation.cs`、`ApprovalTenantSweeper.cs`；各实际命令/批次/后台测试运行相应路径，未宣称测量了生产指标 |
| 备份/还原、告警和保留指南 | `docs/backup-and-maintenance-operations.md`、`docs/diagnostics-and-lifecycle.md`；示例命令未在业务库运行 |

最终 `pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore`（`output/stage5/verification-scope-final.log`）退出码 **0**，包含 Core **333**、HTTP **65**、证照 **66**、SQLite **28**，合计 **492 passed / 0 failed / 0 skipped**。后端构建零警告/错误，证照及四 Provider 迁移/模型漂移验证通过。前端 UI **176** + 示例 **6** = **182 passed**，边界 **7** 项及类型检查/库和示例构建、契约差异检查通过。

最终独立包消费者 `output/engineering/consumer-ac0bb2a9256140f19ef21a7b2302740a/` 构建及真实浏览器 **3 passed / 0 failed / 0 skipped / 0 flaky**（36.8 秒）：保存/刷新/发布后三审生效；匿名和未知协议拒绝；匿名 Ready 只有布尔、维护默认只能预览。报告见该目录 `verification.json`、`browser/results.json`、`browser/passed.json`。首轮 All 及三库通过后最后补入归档范围防线，已重新完成当前版本完整验收及三库，不只复用旧报告。

三库各 **28 passed / 0 failed / 0 skipped**。最终 TRX 确认含五项新 Lifecycle 契约、旧双宿主及事务矩阵；manifest 各制品 SHA256 核对通过，索引 `output/stage5/database-evidence.json`：

| Provider | Server | 最终 runId |
|---|---|---|
| MySql | 8.4.10 | `ff2e22e4ab354ebebc391a73512932bc` |
| SqlServer | 16.00.4255 | `5a012270fce549b8ad7b6d1d14aa015f` |
| PostgreSql | 17.11 | `382931b777984c5abc002edcb83c3b3d` |

完整报告位于 `output/database-tests/<Provider>/<runId>/`；最低用例发现数更新为 28，临时验证容器已清理。提交失败使用真实事务拦截器注入，文件失败通过存储适配器注入，不是生产进程崩溃、网络分区或备份文件恢复演练。

保留失败日志：维护候选中的数据范围子查询使操作记录未跟踪，重复处理但未保存 tombstone，被持久化和重复计数断言发现，现变更前显式跟踪；数据库测试启用整段重试策略，创建/保留改为正式命令委托；归档二进制端点补成功响应类型及专用二进制契约检查，没有取消其他端点的 JSON schema 验证。未跳过测试、放宽行为断言或扩大等待时间掩盖失败。

可复验（PowerShell，仓库根目录）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $provider -SkipRestore -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw ('数据库验收失败：' + $provider) }
}
```

阶段 5 五项检查完成，下一阶段为阶段 6：参考宿主与完整兼容验收。未运行远端 CI、业务库备份/还原/迁移、部署或真实外部渠道的恢复对账。默认仍没有破坏性维护；实际保留期、授权、备份和 exporter 由宿主配置。

## 阶段 6 交付：参考宿主与完整兼容验收

沿用 `8924e91d42119bd53efa204faaf270148707bdb1` 及全部前阶段工作区修改；没有创建分支/worktree、提交、推送、部署或连接业务数据库。

- 新增不可打包的 `samples/Approval.MultiTenant.Api`：共享表双租户、表单初审/终审、按企业的身份/目录/权限适配、严格稳定请求键。JWT 为生产默认；本机演示认证限制 Development，令牌只来自安全配置。唯一 sub/tenant_id 还需成员核验；演示权限控制台只能变更当前企业成员的非系统权限，生产模式不映射该路由。
- 新增四套空库 `InitialReferenceApproval` 迁移：SQLite `20261102063429`、MySQL `20261102063438`、SQL Server `20261102063447`、PostgreSQL `20261102063456`。普通启动不迁移，`--migrate` 显式执行；不替换既有证照或测试宿主迁移历史。
- 参考宿主真实 HTTP 覆盖双租户/同用户/同键、完整两级审批、当前撤权/恢复、原版本重放、同键正文冲突、跨租户实例/任务拒绝；20 个相同 HTTP 请求跨两个独立宿主只提交一次。新增真实 Provider 后台 scope 隔离、停用跳过与故障继续。
- 最低矩阵补齐三库同步动作失败的整体回滚，以及批量改派逐实例权限成功/跳过、外层原子工作单元拒绝及其他企业不受影响。
- `eng/verify.ps1` 增加四 Provider 参考模型漂移检查及真实 Kestrel 冒烟；独立包消费者复制参考宿主，改为 PackageReference，核对 deps 的 package 类型/SHA512，并真实执行双租户审批。CI 保存参考宿主 smoke.json；未触发远端 CI。
- 框架/示例独立公共 API、OpenAPI 与前端类型在统一入口继续匹配；阶段 6 不增加框架 HTTP 契约，演示控制台属于宿主。新增 `docs/production-acceptance.md`，更新 README、数据库/包指南和 CHANGELOG，明确版本、升级、维护窗口、回退及未演练边界。

### 阶段 6 行为证据

| Requirement | Evidence |
|---|---|
| 双租户完整审批、当前撤权/恢复、原 DefinitionId 和 Schema 重放 | `ReferenceHost_DualTenantApprovalRevocationAndOriginalVersionReplay`；三库 `ReferenceHost_ProviderMigrationsDualTenantWorkflowAndPermissionChanges` |
| 20 个真实 HTTP 同键请求跨两个独立宿主，只保留一个实例/任务/占用/操作结果 | `ReferenceHost_TwentyIdenticalHttpRequestsAcrossTwoHostsCommitOnce`；三库 `ReferenceHost_TwentyHttpRequestsAcrossTwoHostsHaveOneProviderResult` |
| 未认证/伪造请求头、无唯一 Claim/成员/停用、演示控制台边界及严格键 | `ReferenceHost_AuthenticationMembershipAndPermissionConsoleFailClosed`、`ReferenceHost_DirectoryRejectsAmbiguousDisabledAndNonmemberClaims` |
| 生产模式拒绝演示/错误认证配置，短/重复/非成员令牌拒绝，系统权限受限 | `ReferenceHost_ProductionRejectsDemoAndInvalidJwtConfiguration`、`ReferenceHost_DemoTokensRejectShortDuplicateAndNonmemberCredentials`、`ReferenceHost_SystemPermissionsAreRestrictedAndTenantScoped` |
| 数据库后台作用域不串租户，停用跳过，失败不阻止后续企业 | `ReferenceHost_BackgroundFailureCannotCrossOrBlockAnotherTenant`；三库 `ReferenceHost_BackgroundScopesSkipDisabledAndIsolateTenantFailure` |
| 同步动作失败回滚宿主/实例/任务/日志/占用/操作/Outbox，原键可恢复 | 三库 `FinalAcceptance_SynchronousActionFailureRollsBackHostApprovalOperationAndOutbox` |
| 改派逐实例报告成功/拒绝，其他企业不变，禁止外层原子工作单元组合 | 三库 `FinalAcceptance_BulkReassignmentReportsPerInstancePermissionAndKeepsOtherTenant` |
| 旧证照固定企业、既有迁移及三级审批 | `Three_reviews_are_required_before_real_certificate_is_created`；证照回归 66 项、独立包三级审批浏览器场景 |

最低 I/T/N/A/G/O/L/D/C 全部逐项映射到[最终矩阵](../docs/production-acceptance.md)，38 个方法引用已与当前测试源码核对。受控故障和真实生产故障边界分开记录。

### 阶段 6 最终运行证据

| 验证入口 | 结果 / 制品 |
|---|---|
| 完整 All（增加最终两项 DB 补充前） | 退出 0；Core333、HTTP73、Certificate66、SQLite31；UI176+demo6、边界7、类型/构建/契约及浏览器3通过；`output/stage6/verification-final.log` |
| 最终 Backend（含全部补充） | 退出 0；Core **333**、HTTP **73**、Certificate **66**、SQLite **33** = **505 passed / 0 failed / 0 skipped**；构建零警告/零错误，五个示例 Context 漂移检查通过；`output/stage6/verification-backend-final.log` |
| 最终独立包消费者/浏览器 | 退出 0；`output/engineering/consumer-d005596a1e2a49f4875718c2a90fbfed/`；证照和参考宿主均通过本次 NuGet 包构建/运行，前端通过 tarball 构建；浏览器 **3 passed / 0 failed / 0 skipped / 0 flaky**，31.7 秒；`output/stage6/verification-packages-final.log` |
| 源码参考宿主真实 Kestrel | **38** 项检查通过；`output/reference-host/0829f78897ad4acba344f971388570a8/smoke.json` |
| 独立 NuGet 包参考宿主真实 Kestrel | **38** 项检查通过；`output/reference-host/503a0da80bb1408d8a36effd863d6292/smoke.json` |
| MySQL 8.4.10 | **33 / 0 / 0**；`93f94d91f6c34cc18ee8d80bd3facb0e` |
| SQL Server 16.00.4255 | **33 / 0 / 0**；`36f2c5963af446619f8bbe4b0e6e11cb` |
| PostgreSQL 17.11 | **33 / 0 / 0**；`3b5f580e896345b2aef40f7b4d01f9ef` |

三库报告位于 `output/database-tests/<Provider>/<runId>/`；汇总及 manifest/TRX SHA256 为 `output/stage6/database-evidence.json`。已重新解析真实 TRX，验证非零发现、33/0/0 和每个制品哈希；核对 package SHA256、独立 package/SHA512、浏览器 results.json/passed.json 和两套 Kestrel smoke.json。临时数据库容器均已清理。最初的重放权限预期、同步异常类型预期及测试编译失败均已修正；失败 TRX 保留，不替代最终成功报告。

阶段 6 的实现、文档与本机验收已完成。原阶段 6 会话未运行的真实认证、三库备份/附件还原和 TCP 故障现已另行完成，见下文补充。生产权限 SDK、多实例外部权限传播、生产拓扑/进程断电、真实外部投递对账、远端 CI 或正式部署仍未运行。本地包版本仍为 0.2.0 验收产物；正式发布须选择新版本后重建验证，不能覆盖已发布包。

## 后续补充：真实认证、原生还原与 TCP 故障

用户确认没有现成生产认证中心配置。按[补充计划](2026-11-02-auth-backup-network-rehearsals.md)部署本机隔离 Keycloak HTTPS 真实服务，使用参考宿主的 Production JWT 管线、原生三库工具和 Toxiproxy TCP；没有修改生产认证默认值、系统证书信任库或业务数据库。

新增 `eng/Approval.RehearsalTool`、`eng/verify-rehearsals.ps1`、固定镜像/二进制清单和 `docs/live-service-rehearsals.md`；工具不可打包，通过显式入口运行，普通 xUnit 不伪装成已执行重型演练。更新普通数据库探针为 TCP（MySQL 同时验证 TLS），避免临时初始化 socket 误判就绪。

最终 All 入口退出 0：**`ea79988feac24c25817e175aff963307`**。Keycloak 26.4.7，Toxiproxy 2.12.0，SDK 10.0.401。

| Server | Seed | JWT | 原生还原 | TCP/确认丢失/恢复 |
|---|---:|---:|---:|---:|
| MySQL 8.4.10 | 26 | 11 | 25 | 12 |
| SQL Server 16.00.4255 | 26 | 11 | 25 | 27 |
| PostgreSQL 17.11 | 26 | 11 | 25 | 12 |

所有 **237** 项演练检查通过；SQL Server 的额外 15 项来自恢复后基础设施错误的有界重试，始终保留同一个提交键/正文。该数量不是 xUnit 用例数或覆盖率。

| Requirement | Evidence |
|---|---|
| 真实 JWT 主体/企业及不合法令牌拒绝 | `RealOidcSubjectAndTenant`、`JwtSignatureRejected`、`JwtWrongIssuerRejected`、`JwtWrongAudienceRejected`、`JwtExpiredTokenRejected`、`JwtNonmemberRejected` |
| 原生完整新库还原、所有持久属性/迁移与附件 | `NativeBackupRestoredEveryPersistentProperty`、`RestoredAttachmentBytesAndHash`、`RestoredForeignAttachmentRejected` |
| 原键/410、审批/占用、维护游标及 Outbox | `RestoredOriginalKeysReturnOriginalIds`、`RestoredTombstoneReturns410`、`OriginalInstanceCompleted`、`RestoredApprovalReleasesBusinessSlot`、`RestoredMaintenanceResumesFromCommittedCursor`、`RestoredOutboxKeepsOriginalIdAndPayload` |
| 实际中断无提交，丢弃 commit ACK 后核验并原键取回 | `TcpOutageLeavesNoCommittedOperation`、`RealCommitAckLossReturnsUnknown`、`ActualDownstreamTcpToxicInstalledAtCommit`、`DirectConnectionVerifiesCommittedResultDespiteLostAck`、`RestoredNetworkReplaysVerifiedIdWithoutDuplicate` |

全部报告、原生 SQL/dump/bak、前后状态快照、附件和递归 SHA256 位于 `output/rehearsals/ea79988feac24c25817e175aff963307/`。已验证各 stage 非零/全通过、每个 manifest 制品摘要、前后快照哈希相同、原工具 DLL/锁文件摘要和 SDK；确认本次容器/代理进程与 realm 口令文件/TLS 私钥清理完成。早期失败报告保留，完整问题和修正记录见[真实服务演练](../docs/live-service-rehearsals.md)。

普通 Backend 退出 0：Core333、HTTP73、Certificate66、SQLite33 = **Passed: 505, Failed: 0, Skipped: 0**，含构建/模型及 Kestrel38 检查；日志 `output/rehearsals-validation/backend.log`。契约生成与基线一致，日志 `output/rehearsals-validation/contracts.log`。没有执行提交、推送或正式部署。

现有生产中心/权限 SDK、生产账号/加密密钥恢复、HA/进程故障、真实业务对账与生产 RPO/RTO 仍需提供真实环境后单独验收；Keycloak 的单节点 H2 只用于隔离演练。

补充演练复验：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify-rehearsals.ps1'
```

复验（PowerShell，仓库根目录）：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
foreach ($provider in @('MySql', 'SqlServer', 'PostgreSql')) {
    pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider $provider -SkipRestore -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw ('数据库验收失败：' + $provider) }
}
```
