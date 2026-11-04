# Changelog

## Unreleased — 2026-11-03（RC 最终收尾）

- 项目未发布且无生产历史数据：删除附件 ArchiveId／DeletionPending、旧维护访问分支及 Tenant 存储键兼容路径；真实附件授权规则保持。
- 九套示例／测试迁移重建为当前模型的 InitialApproval，删除空边界迁移和旧维护表；物理 schema 与附件元数据往返纳入服务数据库必需测试。
- TimeoutScanner 使用 scopes 命名；清理测试／演练中误导性的 Tenant 参数与测试名。
- 完善生产 OperationId、HTTP／SDK／非 HTTP 与批量恢复文档；RequireOperationId 默认值不变。
- ApprovalActions 测试使用真实 Element Plus 插件及 SSR 注入，保留权限断言并验证零 warning。当前验收见 docs/rc-report.md。

## Unreleased — 2026-11-03（第三轮审批写边界与宿主可靠性）

以下记录第三轮当时状态；旧维护字段与未发布迁移保留策略已由 RC 收尾替代。

- 审批运行、治理、消息、Operation/Outbox 与 Forms 采用内部实体/属性写授权；普通共享 DbContext 跟踪写入、批量操作和可识别原生 DML 拒绝，宿主业务保存与原有事务保持。
- 隔离宿主回调、输入/结果序列化和依赖读取；保存后的命令校验、显式 AcceptAllChanges 和池化 Context 租期状态继续验证。
- 启用 Dispatcher 时启动检查身份/事件处理器；新增显式关闭投递选项，不丢弃 Outbox。严格 OperationId 不能由裸 UnitOfWork 替代；批量改派新增兼容键重载并保留逐实例恢复。
- 保留公共旧签名、流程/字段权限/channels JSON、九套历史 Migration 与旧附件/幂等数据；Core-only 消费者补当前包摘要与实际攻击/正常审批验证。
- 改造与实际验证证据见 docs/round3-report.md，保护与可信数据库边界见 docs/persistence-write-boundaries.md。

## 0.3.0 — 2026-11-03

- 删除默认维护/归档/保留系统、相关 API/UI/Worker；保留诊断、审批审计与 Outbox。
- 宿主可直接使用 DbContext、IdentityDbContext 或已有基类，EF 拦截器与工作单元保留事务和不可变保护。
- 拆出可选 Approval.Forms / Approval.Forms.AspNetCore；实际文件存储归宿主，不再自动清理附件。
- 外部事件统一 IApprovalEventHandler + Outbox；删除渠道及 best-effort Notifier 接口，权限默认拒绝且不推断管理员角色。
- 保留幂等结果恢复、请求摘要、唯一占用和并发保护；没有自动操作保留调度。旧已擦除结果的防重标记仅用于升级兼容。
- 九套边界迁移退出维护模型管理，保留历史表、快照及附件数据，不修改历史 Migration。
- 包验证不依赖 ripgrep；四个 NuGet 包、独立 Identity 消费者和 npm 消费者纳入验证。后端 Forms 已独立，前端表单仍在 @approval/ui。

本轮实际测试与未满足的服务环境/远端验收见 docs/round2-report.md。

## 历史开发记录

以下内容是此前开发快照，不是当前接入方式或当前测试证明。


## Unreleased — 新项目初始化（2026-11-03）

- 根据项目尚未正式使用的约束，九套 EF 上下文统一为当前模型的一条 InitialApproval；删除开发期迁移链、租户/业务占用回填工具、升级副本逻辑及相应兼容测试。
- 删除只读旧 Action 协议、数字枚举兼容、动作扫描 API/UI、旧程序集转发；保留审批生命周期、可靠事件、分类和抄送人员功能。
- 前端仅使用每次安装独立的客户端，移除全局配置/API/传输兼容实例和旧类型重导出。
- 数据库初始化、正常备份、就绪检查、权限、事务/并发、业务历史与幂等回归继续验证；真实三库验收以对应日志为准。


## Unreleased — 2026-11-03（嵌入式审批改造）

- 删除 Core 多租户运行接口、TenantId 实体隔离、Claim 路由/后台目录轮转及多实例投递/维护/附件租约；可信 Actor、宿主授权及实例并发保护保留。参考宿主改名 Approval.ReferenceHost。
- 删除内置 HTTP 动作、JavaScript 沙箱/Jint、HTTP/脚本配置、第三方发送器及设计器自动编排入口；旧定义/在途自动化在迁移、发布、发起及投递时明确阻断，不静默跳过。通用动作接口/注册/执行链已删除，旧协议仅供历史读取与诊断；准备工作改用最小 IApprovalLifecycleHandler，结束业务和可靠事件各沿用既有契约。
- 一个 ApprovalWorker 串行投递与超时扫描，失败退避/重试上限/人工重试/稳定事件 ID 保留；统一 IApprovalEventHandler 接收可靠生命周期事件，默认超时只提醒。
- Returned 明确表示申请人修改，重提保留实例与固定版本；新增独立抄送阅读、服务端完整查询过滤排序、宿主未知/停用用户校验、意见必填、只追加节点/任务轨迹、流程复制草稿与只读预览。
- 并行增加激活标识和分支到达去重；并发完成与命令重放只产生一个下游任务，原嵌套/退回/加签行为继续回归。
- 九套上下文追加 SimplifyOutbox 与 EmbeddedApproval，保留旧迁移、业务数据和旧归属审计；混合归属/未迁移自动化改表前阻断。公共接口、调用方、OpenAPI/TypeScript/包消费者及前后端文档同步更新。
- 完善管理列表分类投影、服务端筛选与受权分类选项；新增角色抄送当前人员分页查询，HTTP 优先受限目录，工作台/详情共用弹窗并隔离身份变化后的旧响应。
- 九套上下文追加 CompleteEmbeddedApproval，阻断所有在途旧动作与未完成动作消息，保留历史原文；回填草稿优先/选用版本分类。
- 完整交付、迁移/回退说明、测试矩阵和未运行环境边界见 docs/embedded-approval-completion.md。

## Unreleased — 2026-11-02

- 增加独立真实服务演练入口：Keycloak HTTPS/JWT、三库原生备份到新库恢复、附件/维护游标与原键核对、实际 TCP 中断及 COMMIT 确认丢失恢复；不修改生产认证默认值或系统信任库。
- 数据库验证探针改为实际 TCP（MySQL 同时验证 TLS），避免初始化时临时 Unix socket 被误判为就绪。演练客户端根据真实响应进行有界原键重试，并记录镜像、工具、备份和报告摘要。

- 阶段 6：新增不可打包的 `Approval.MultiTenant.Api` 参考宿主，支持共享表双租户、JWT 默认认证、Development 专用演示令牌和第三方权限适配；写入口强制稳定提交键，数据库迁移显式执行。
- 新增四 Provider 独立空库迁移及真实宿主双租户/撤权/恢复授权/原版本重放和 20 请求并发验收，接入统一验证、三库契约和 Kestrel 冒烟。最终支持/升级/回退及未演练边界见 `docs/production-acceptance.md`。

- 阶段 5：通用诊断、可选匿名 Ready 探针、ActivitySource/Meter 和限量轮转的租户后台；诊断不输出配置或任意异常文本。
- 默认关闭归档/清理。新增预览、当前策略核验、租约与同事务批次游标、保留标记、历史快照、独立敏感读取/归档范围接口和永久命令 tombstone；附件先提交删除任务再幂等执行。宿主追加 LifecycleMaintenance 迁移，并配置各自保留期，见 `docs/diagnostics-and-lifecycle.md`。

- 阶段 4：独立设计草稿、编辑令牌与发布计数，发布版本不可变；启停和历史版本选择接入权限、原键重放及同事务审计，在途实例保持原定义与 Schema。
- 设计器支持保存草稿和刷新恢复、变更说明；版本抽屉增加启停、历史版本选择和审计。增加旧同步外部动作/声明自提交动作的分页只读扫描，不输出参数或更改旧定义。宿主追加 DefinitionGovernance 迁移，见 `docs/definition-governance.md`。

- 阶段 3：新增工作单元与整段命令协调器、租户范围的操作结果/业务占用、稳定 Idempotency-Key、同内容重放及提交结果未知核验；宿主委托在全新作用域重试。
- Core/HTTP/证照写入接入事务，批量改派保留显式部分成功；Outbox 使用独立事务、租约 fencing 与提交后唤醒；前端按逻辑意图保留重试键。升级须新增 AtomicOperations 迁移并停写回填存量业务占用，详见 `docs/transactions-and-idempotency.md`。

- 阶段 2：统一 Core/HTTP 权限 provider、稳定权限码、ASP.NET policy/默认角色适配、可信操作人参数校验、受限系统身份与数据库查询范围；权限超时/失败拒绝并返回 503，默认不缓存授权。
- `/me`、实例和当前任务提供 capabilities，UI 按能力显示动作；端点权限元数据启动时校验。后台调用须显式授权，详见 `docs/permissions.md`。

- 阶段 1：九类审批实体加入 TenantId、Context 命名过滤、写入/批量保护及同租户复合关联；HTTP 从可信 Claim 解析企业，后台按启用企业独立调度。
- 默认本地附件采用租户范围存储键；支持租户时区、链接和模板变量，前端企业切换清空缓存及未提交内容并拒绝迟到响应。
- 新增四种 Provider 的 Expand/Mapping/Harden 迁移、离线租户回填 CLI 与报告；三库验证旧待办在升级后继续审批。
- 接入变更：宿主 Context 须继承 ApprovalDbContext 并调用 ConfigureApproval(this)，单租户须配置 FixedTenantId；多租户禁止固定回退，适配器按作用域注册。升级步骤见 docs/multitenancy.md。
- 生产认证中心、真实业务库备份还原和网络故障演练由宿主按最终支持矩阵完成；本机验收不代表已部署或已发布。

## 0.2.0 — 2026-11-01

- 将注册证业务整理为 `samples/Approval.Certificate` 示例类库，携带默认流程和原 EF 迁移；保留原命名空间及程序集类型转发，不作为框架包发布。
- 统一构建入口和 NuGet 版本，抽取 scoped 扩展目录与人员解析服务。
- 同名通知渠道统一采用后注册实现，与元数据、动作覆盖规则一致。
- 提供独立 Vue 客户端、用户/目录缓存与路由状态隔离；旧单实例入口保持兼容。
- 拆分前端协议、图编辑、布局、序列化与表单计算；提供无 UI 依赖的类型入口。
- 提供明确 HTTP 响应 DTO、生成契约与差异检查；框架与注册证示例分别维护契约，UI 包只导出框架 HTTP 类型。
- 流程 JSON 协议 v1：旧数据省略 schemaVersion 时仍按 v1 读取，新发布显式写入 v1；未知版本拒绝编辑/发布，既有定义和数据库迁移历史不重写。

发布前通过统一验证及独立包/浏览器验收；实际包与校验和由发布产物清单关联。
