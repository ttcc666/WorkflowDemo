# WorkflowDemo 生产准备审查与加固

审查基线 `480143de63ee91139ffbc6d5880e0221ac488a97`，开始时 `main` 与远端同步，工作区干净。本轮按任务要求在当前工作区直接实施。现有生产边界是单宿主、单应用实例，允许同一实例内并发 HTTP。宿主负责身份、组织、授权、文件存储和外部事件幂等。多进程共享数据库的写事务与多进程运行 Dispatcher 分开验收。

## 实施与验证清单

- [x] 修复版本对比统计与导入预检竞争，并核实上一阶段的验收记录。前端用例验证纯布局、业务和混合修改，浏览器控制预检响应顺序以验证旧结果失效。
- [x] 审查 HTTP 权限与 Core 调用、QueryScope、定义治理、文件上传和数据库写入边界。用实际复现或现有覆盖确定修复，给改动增加负向测试。
- [x] 审查 Outbox 持久化、同进程调度、确认故障、取消、重试和积压。区分数据库状态与外部副作用，保留宿主按 `Event.Id` 去重的职责。
- [x] 对照公共 API、DI、NuGet 和生成契约，检查性能与前端生命周期。对有证据的缺陷做局部改动，验证对应测试与工程门禁。
- [x] 记录问题位置、风险等级、实际验证与待验收场景，形成发布判断。
- [ ] 提交后核对本轮确切 SHA 的 Windows 及 MySQL、PostgreSQL、SQL Server CI。

回滚按本次实际修复拆分。前端统计和请求状态可独立回滚。后端加固与对应回归用例一同回滚，保留宿主原有数据和事件处理方式。若发现必须调整公共契约或数据库模型，先记录现有消费者、迁移影响与兼容方案。

## 执行摘要

按 HTTP、Core、可选 Forms、后台投递、公开契约、数据库查询和前端入口审查。共修复 9 项，其中两条隐藏附件越权路径在原实现通过真实 HTTP 复现，另有一条跨用户文件名泄露。优先覆盖同一数据库上的并发、提交确认故障、服务端授权和可恢复的客户端请求。修复沿用现有审批状态机、数据库模型、依赖和公开 API 形状。基准功能及 CI 证据已补入[上一阶段验收记录](2026-11-05-definition-portability-and-host-testing.md)。

## 已修复风险

| 编号 | 等级 | 位置、触发条件与影响 | 修复及回归 |
|---|---|---|---|
| PR-01 | P0 | `ApprovalFileService.cs` 104 行。当前参与节点隐藏附件字段时，原下载判定搜索可见文本和所有轨迹中的 UUID 子串。备注含隐藏附件 ID 时，Bob 的实际 HTTP 下载返回 200 | 按已发布版本的附件字段类型检查规范化引用，并且只读取可见附件字段的 `FormEdit` 前后值。`HiddenAttachmentCannotBeAuthorizedByItsIdInVisibleTextOrAudit` 同时验证真实可见附件和上传者仍可下载 |
| PR-02 | P0 | `ApprovalFileService.cs` 159 行及 `ApprovalFormService.EditAsync`。已绑定同实例的隐藏附件 ID 可被参与者放进可编辑附件字段，原 HTTP 同意返回 204 | 保留原字段引用，新引用其他人上传的同实例附件时，重新确认附件对当前查看者可见。`ReviewerCannotCopyHiddenAttachmentIntoAnEditableField` 验证拒绝、实例仍在运行且无表单修改轨迹 |
| PR-03 | P1 | `ApprovalFileService.cs` 183 行。提交其他用户的附件 ID 时，原错误含真实文件名 | 不存在、归属不符及不可见引用使用同一提示。`CrossUserAttachmentValidationDoesNotDiscloseFileMetadata` 比较真实外部附件与随机 ID 的结果 |
| PR-04 | P2 | `ApprovalFileService.cs` 240 行。扩展名超过文件名长度预算时，原截断产生负索引 | 将异常归为请求校验错误。`OversizedExtensionReturnsBadRequestInsteadOfRangeException` 验证 HTTP 400 |
| PR-05 | P2 | `definitionDiff.ts` 123–153 行。纯坐标差异占用业务修改计数 | 对业务字段和画布坐标分别计数，每个节点在每一类最多计一次，定位仍使用同一个节点 ID。单测覆盖纯布局、纯业务、混合三种情况 |
| PR-06 | P2 | `ImportDefinitionDialog.vue` 23–71 行。异步预检的成功、失败和结束处理原先只依赖通用 generation，目标 Key 可变化后仍接受前一份结果 | 捕获请求 Key 和预检代数。切换 Key、文件、身份或对话框状态时同步失效，提交保持服务端重查。浏览器用例控制响应逆序和关闭后重开 |
| PR-07 | P1 | `FlowDefinitionValidator.cs` 39、147 行。显式空起点或不完整的通知数组可在完整校验中触发空引用或空 Key 异常 | 提供 `InvalidDefinition`，保留已支持的输入形状。HTTP 断言错误、草稿、审计和 Operation 原子回滚 |
| PR-08 | P1 | `FlowDefinitionValidator.cs` 198 行及 `FlowStructure.cs` 70 行。验证用 DFS 将长链映射成调用栈，JSON 自身浅层也能产生很长的流程路径 | 改用显式栈，保持访问顺序、环判定和并行汇聚规则。`LongNodeChainCanBeValidatedAndCyclesStillFail` 验证 20,001 个节点和实际闭环 |
| PR-09 | P1 | `ApprovalBackgroundRunner.cs` 13 行。第一次 Dispatcher 调用阻塞时，第二次空跑会用成功心跳覆盖仍在进行的投递，取消也可能留下悬空心跳 | 以当前宿主和任务名序列化 Runner，取消后记录结束状态。`OverlappingDispatchDoesNotOverwriteTheActiveHeartbeatAndCancellationCanRecover` 使用真实数据库和受控处理器验证下一轮恢复 |

## 专项复核

- **授权、身份与事务**。HTTP 组统一校验可信身份和权限元数据，`EveryMappedEndpointHasKnownPermission_AndNewUnclassifiedRouteFailsStartup` 对未分类路由执行失败检查。Core 在实际命令再次验证操作者、资源归属与能力。`ApprovalResourceReader.cs` 按当前资源执行 QueryScope，`ApprovalQueryService.cs` 和统计路径在计数、分页或读取预算前收窄候选。现有 `PermissionEndpointTests`、`ApprovalWriteProtectionTests`、`SingleHostBoundaryTests` 和 `TestingHost` 覆盖撤权、伪造跟踪、批量与原始 SQL、停用、身份作用域、事务和重放。
- **定义与契约**。导入预检和草稿写入分别要求目标设计权限，发布、停用和敏感导出要求各自能力。已发布快照通过存储保护和版本绑定保留。`ConcurrentDraftPublicationsCreateOneVersionAndPreserveHistory` 验证两个写命令竞争同一草稿 Revision，只提交一份新版本、审计及 Operation。导入 HTTP 体与文件分别受 1.5 MiB、1 MiB 限制，包和流程 Schema 分离。生成的 API、OpenAPI、JSON Schema、TypeScript 契约随 `contracts:check` 校验。
- **后台与 Outbox**。Outbox 随业务状态提交，Dispatcher 从数据库读取到期 Pending。进程退出时不存在需要手工解锁的领取状态。同宿主用执行门和版本戳保护重复调度与人工重试，失败按退避和最大次数进入 Failed，诊断保留待处理数量、失败数量和最早待处理时间。`OutboxDatabaseConfirmationFaultPreservesDeliveryIdentityAndOneLocalEffect` 在真实投递事务的 COMMIT 前后注入异常，验证重复投递使用相同事件 ID，且宿主数据库副作用只提交一次。外部渠道仍按 `Event.Id` 自行去重。
- **DI、包、性能与前端**。后台 Singleton 通过作用域工厂解析 Scoped DbContext、身份与事件处理器，`Approval.Testing` 由测试宿主显式启用。包门禁检查独立消费者的还原内容、传递依赖和产物哈希。统计读取预算、Outbox 到期索引与分页保留现有验证。设计器已有保存失败保留编辑、撤销/重做以及离开页面提示，`DesignerView.vue` 在销毁时清理 `beforeunload`。大型浏览器画布和长期积压的吞吐量仍需按实际宿主规模测量。

## 验证结果

2026-11-06 在仓库根目录运行 `pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore`，以 `Verification succeeded: All` 退出。`-SkipRestore` 复用本机依赖，独立 NuGet 和 npm 消费者在各自隔离目录完成还原与验证。

| 本轮本地范围 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Core | 614 | 0 | 0 |
| HTTP | 142 | 0 | 0 |
| SQLite 数据库契约 | 86 | 0 | 0 |
| Certificate.Api | 59 | 0 | 0 |
| Vitest UI / Demo | 255 / 6 | 0 | 0 |
| 前端依赖与声明边界用例 | 12 | 0 | 0 |
| Playwright 产物消费者 | 10 | 0 | 0 |
| EmbeddedHost / TestingHost 场景 | 18 / 22 | 0 | 0 |

模型漂移检查覆盖四种 Provider 的模型，ReferenceHost 烟测 27 项，生成契约与基线一致，NuGet/npm 独立包消费者通过。原实现的三条附件回归为 3 失败、3 通过。修复后 `FileEndpointTests`、`FormEndpointTests`、`PermissionEndpointTests` 定向运行 24 通过、0 失败、0 跳过，后续全量门禁又覆盖超长扩展名与导入格式用例。

本轮代码以本地工作区为验证对象。SQLite 实际运行了并发发布、Outbox COMMIT 前后故障和取消恢复。MySQL、PostgreSQL、SQL Server 的实际运行待本轮提交后的 CI。基准 `480143de63ee91139ffbc6d5880e0221ac488a97` 的 Windows 与三个服务数据库作业已通过，服务数据库各 82 通过、0 失败、0 跳过。 [基准 GitHub Actions](https://github.com/ttcc666/WorkflowDemo/actions/runs/42065839666)

## 遗留风险与部署判断

| 编号 | 等级与适用条件 | 当前影响与处理边界 |
|---|---|---|
| R-01 | P1，多进程同时投递 | `ApprovalOutboxService.cs` 75–99 行的执行权属于单个宿主进程，跨进程的行版本只能阻止覆盖确认，无法阻止两个外部调用先后发生。生产拓扑保持一个 Dispatcher，滚动切换时确保旧实例退出后再启用新实例。多进程投递作为独立架构决策与验收项 |
| R-02 | P1，外部处理器长期阻塞或系统时钟跳变 | 投递等待处理器退出后才释放作用域与运行权。宿主为每个外部 I/O 设置超时并遵守取消；计划时间使用 UTC，后拨可延迟到期判定。当前自动化覆盖有限时间内忽略取消后退出、恢复、重试间隔，真实长时间停机、数据库断网和时钟跳变需运维演练 |
| R-03 | P1，接受不可信附件的宿主 | `ApprovalFileService.cs` 49–88 行按扩展名白名单、流式大小与随机存储键约束，下载强制附件且有 `nosniff`。字节格式鉴定、恶意内容扫描、跨存储确认不明和孤立文件对账由宿主存储边界解决。上传文件应进入隔离区，合格后可下载。 [ASP.NET Core 文件上传安全](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0) |
| R-04 | P2，版本或审批历史规模很大 | `ApprovalDefinitionService.cs` 154–172 行完整加载同一 Key 的版本并逐版本授权，`ApprovalInstanceReader.cs` 42–55 行加载业务历史详情。当前缺少实际流量的规模和 SQL 成本证据。先测内存、请求耗时和权限调用数，按结果决定是否增设按需快照或分页接口 |
| R-05 | P1，将流程设计能力授予不可信作者 | `DynamicExpressoConditionEvaluator.cs` 12–45 行可解析表达式并调用输入类型允许的成员，语法校验和 JSON 字节上限不等于计算预算。本轮验证图遍历和一般语法，极端表达式与资源耗用仍需专门的隔离复现与能力收敛。在此之前只授予可信设计者相关能力。 [DynamicExpresso 解析器源码](https://github.com/dynamicexpresso/DynamicExpresso/blob/master/src/DynamicExpresso.Core/Parsing/Parser.cs) |

建议在单实例、宿主权限和外部存储控制到位的前提下进入试运行验收。正式发布前须取得本轮确切提交的 CI 结果。要求多实例投递或开放不可信附件时，对应的 R-01、R-03 是部署门槛。下一阶段优先验证宿主附件隔离与对账、Outbox 停机/断网演练，以及真实数据规模下的版本与历史查询成本。
