# 后端渐进式重构执行方案（2026-11-03）

## 状态、范围与执行约定

2026-11-03 用户追加约束：数据库相关专项验证交给 GitHub CLI / Actions，本地不执行，不打开 Docker。后续不在本地运行 Approval.Database.Tests、Provider 容器脚本或完整 Backend 脚本内的数据库门禁；本地只编译及相关业务代码测试，数据库矩阵由当前工作分支的 CI 验证，并核验 head SHA。此前由本任务启动的 Docker 已关闭，本地镜像下载已停止。

状态：前三轮代码及本地业务门禁已完成，数据库专项转 GitHub CI，正在进入第四轮。依据 [审计报告](../docs/backend-architecture-audit-2026-11-03.md)；起点 HEAD 为 `ca6987815ce0b668bd1401a8b15e50da824ee81b`。

用户全局工作约定第 3 节要求：大范围 / 高风险变更先写 `plans/<date>-<topic>.md`，提醒用户后由新会话实施。本次遵循该约定。下一会话先读取此计划及实际 HEAD / Diff，继续第一轮；不要重做已经记录的审计，也不要把历史基线当作新代码验证。

执行顺序固定：

| 轮次 | 优先级 | 主题 | 进入条件 |
| --- | --- | --- | --- |
| 第一轮 | P0 | Engine、操作上下文、事务边界 | 当前实际代码基线通过 |
| 第二轮 | P0 | EF Entity 泄漏、公共扩展接口 | 第一轮验收通过；公共 API 迁移选择明确 |
| 第三轮 | P1 | Query、Forms 模块边界 | 第二轮验收通过 |
| 第四轮 | P1 | Authorization、DI | 第三轮验收通过 |
| 第五轮 | P2 | Definition、重复代码、性能 | 第四轮验收通过；涉及行为变化的规则已有明确选择 |

每轮可分多个小主题，但一次 Diff 只处理一个主题。每个主题完成 **显式 Build → 相关测试 → 集成测试 → Diff 审查 → 记录结果** 后才能继续。没有通过的轮次不得标成完成，也不得靠修改预期值让未经授权的行为变化变绿。

## 不变量（Invariants）

1. 状态机、Any / All / Sequential、嵌套 Parallel / Join、Round、前后并加签、转交、改派、退回、重新提交、撤回和超时的业务结果保持一致。
2. 日志 Sequence、TaskId 归属、事件类型和发生顺序、接收人、时间读取时点保持一致。重复取消或状态赋值只能在证明不影响事件快照后删除。
3. 继续使用原 DbContext 与原 UnitOfWork；最终 Commit / Rollback 归 UnitOfWork。不会创造第二个事务所有者。
4. 命令执行器预约唯一操作键先于宿主委托；每次重试使用新 scope。稳定 ID 的 kind 字符串、调用顺序、OperationSeed 和定义固定行为保持一致。
5. 并发令牌、业务占用槽、Definition Revision、历史追加约束、未知提交恢复、幂等回放授权保持一致。
6. Outbox 与审批、宿主本地业务同事务保存；提交后才唤醒。Outbox 成功事务、失败新事务和 allowSave 回调特例保持一致。
7. Host callback 不继承 Engine / Forms 写权限；不能保存或自行提交事务。生命周期当前可修改 Title / FormDataJson 的能力有明确替代。
8. QueryScope 在分页、Count、批量候选选择前应用；不能用列表读权限替代敏感详情权限。权限失败关闭、可信 Actor、系统权限白名单保持一致。
9. 不新增程序集 / NuGet 依赖，不改变数据库模型 / Migration，不引入全局缓存、泛型 Repository 或 Handler 框架。
10. HTTP JSON、OpenAPI、日期和字段可见性契约保持不变。C# 公共契约改变须单独列出，不能静默实施。

## 第一轮：Engine、上下文与事务边界（P0）

### 当前实现与目标

审计 A01–A03：Engine 六个 partial 共享一次操作的可变字段；`CommitAsync` 的真实职责是提前保存，而 UnitOfWork 拥有事务提交。`FinishAsync` 已是结束逻辑的权威实现，`ApprovalAssigneeService` 已是审批人协作者。

优先引入一个 internal `ApprovalOperationContext`，以明确参数贯穿运行链路。随后按需要提取少量 concrete 协作者：事件与通知写入、任务状态协调、流程导航；不为这些内部实现新增接口。保留公共 ApprovalEngine Facade 和现有公开构造方式。

### 1A：收敛操作状态（首个实施主题）

- [x] 列出六项字段所有绑定引用，包括六个 partial、只读入口和逐实例批量改派。
- [x] 新增 `Engine/Runtime/ApprovalOperationContext.cs`，承接 Steps、当前操作 TaskId、OutboxWritten、LoadedFlows、Structures、States。
- [x] 在每个真正 Engine 命令执行体创建 context；通过私有方法参数传入。并行分支的递归共享该次操作 context，同一 UnitOfWork 内不同 Engine 调用使用不同 context。
- [x] 批量交接：每个实例委托有独立 context，实例内每个任务的日志 TaskId 显式更新。不能为整个批次共用一个 context。
- [x] 读取入口若需要流程 / 结构缓存使用调用内上下文；不依赖此前写操作留在 Service 上的 TaskId。
- [x] 删除旧操作字段及成功路径的手动复位，不使用 `_currentContext`、AsyncLocal 或隐式属性转发来伪装隔离。
- [x] 保留 TransactionState 的 OperationSeed、Definitions、IdCounters、AfterCommit、写权限与失败状态；这些属于事务 / 重试边界，不移入 Engine command context。
- [x] StateJson 的序列化时点仍是本次提前保存之前；一次流程内 200 自动步数上限继续覆盖所有递归分支。
- [x] 同一作用域连续调用、同事务启动后再审批、早返回、异常、取消、回放分别验证。独立 context 不代表 DbContext 支持并发调用，不新增并发执行承诺。

**首批文件：** `ApprovalEngine.cs`、`Commands.cs`、`Collaboration.cs`、`Admin.cs`、`Timeouts.cs`、`Notifications.cs`、新增 context，以及测试用例所在现有 Core Tests 文件。若只做 context，不提前提取全部协作者。

### 1B：明确 Flush 与事务 Commit

- [x] 私有 `ApprovalEngine.CommitAsync` 重命名为反映真实用途的 `FlushOperationAsync`；保持保存位置、异常映射和 AfterCommit 登记。
- [x] 调用点明确为：实例引用绑定 → StateJson / CurrentNodeId 归档 → SaveChanges → 登记 Outbox 提交后唤醒。
- [x] 用文档及职责命名区分预约保存、模块提前保存、最终保存 / Commit，不新增 `IApprovalPersistence` 通用包装。
- [x] 对宿主 SaveChanges override / interceptor 调用次数建立观察；不直接消除“无实体变化”的 SaveChanges，因为宿主可能有审计或其它副作用。
- [x] 第一轮保留所有现有 SaveChanges。若证明某个保存点可以删除，作为后续独立主题，先补充跨命令可见性与错误时点测试。

**首批文件：** Engine 六个调用文件、`docs/transactions-and-idempotency.md`、`docs/persistence-write-boundaries.md`。UnitOfWork / CommandExecutor 只在有实际必要时修改；不能为了统一命名重写其重试流程。

### 1C：逐个提取真实职责

- [x] 提取 `ApprovalEventWriter`：日志、可靠事件、站内消息、通知规则及 Outbox 入队共用同一个写入协作者，显式使用 context；无权自行 Commit。
- [x] 提取 `ApprovalTaskCoordinator`：任务创建 / 完成 / 取消、最近有效 Round、前加签恢复、Sequential 激活、HandOver 保留字段与 parent 关系；保持与事件发生时点的一致关系。
- [x] 提取 `ApprovalRuntimeNavigator`：条件、Fork / Join、分支 scope、推进停止点；仍使用现有 `FlowStructure` 与 `ApprovalAssigneeService`。
- [x] 保持依赖无环：TaskCoordinator 不反向调用 Navigator；Engine 依据任务处理结果发起继续推进。Navigator 返回结束 / 等待结果，由现有 Finish 处理终态。
- [x] `FinishAsync` 继续作为权威结束逻辑；只有 Engine 仍明显承担跨职责协调时才进一步移动，不预建 CompletionService。
- [x] 每提取一个协作者就删除失去意义的旧私有实现与 partial；不保留内部转发包装。公开兼容入口按实际边界保留。

**候选新增文件上限：** 一个 context + 三个协作者。数量是限制而非必须达到的目标；以最终职责收益决定。

### 测试与验收

- 复用：`OperationTests`、`EngineRuleTests`、`ParallelReturnAddSignTests`、`ActionRollbackTests`、`ActionAndTimeoutTests`、`AdminTests`、`StrictCommandBoundaryTests`、`ApprovalWriteProtectionTests`、`HostDbContextIntegrationTests`、`EmbeddedApprovalTests`。
- 最小新增行为验证：同 Engine 连续两个命令的步数 / TaskId 不串联；同外层事务启动并继续审批；无效超时早返回后的后续操作；多任务批量改派日志分别归属原 TaskId。
- 异常失败的 scope 仍须释放，不能通过上下文清理让 UnitOfWork.Failed 变为可复用。
- Database：Atomic rollback、未知 COMMIT、同操作键并发、Parallel branch completion、WriteGuard；真实 Provider 按下文门禁执行。
- 验收：公共 Engine 调用方式、模型和事务不变；操作字段消失；Facade 的任务 / 导航 / 事件职责实际减少；没有循环协作者或新接口群。

**影响：** 无公共 API / 数据库变化；事务逻辑保持，内部传参与职责改变；预期收益为降低隐式状态及 partial 耦合。

## 第二轮：EF Entity 与公共扩展契约（P0）

### 进入前的 API 决策

2026-11-03 用户已明确选择推荐路线：直接迁移现有接口并同步修改仓库内宿主，允许 C# breaking change。HTTP JSON、数据库结构及事务行为保持不变；实施时记录每个实际签名变化。

审计 A04–A06：现有接口签名替换不能在保持 C# source / binary 兼容的同时完成。未发布 `0.3.0` 也不等于已获得任意破坏性改动授权。

先提交可审查的参数 / 返回值对照和消费者影响清单，选择以下其一并记录：

| 路线 | 实施方式 | 兼容性与代价 |
| --- | --- | --- |
| 推荐结构（需明确授权契约变化） | 将已有扩展接口改为 Context / Snapshot / PreparationResult，一次迁移仓库内宿主与测试 | 最少并行接口，C# breaking change；HTTP、DB 保持稳定；提供迁移文档 |
| 保持已用接口 | 新 context 契约承接主路径，旧接口在真实使用时适配，保留必要兼容入口 | C# 兼容但类 / 接口更多；旧 Entity 契约不能宣称已彻底消失 |

如果 API 选择尚未明确，完成纯内部映射和影响清单，保持旧声明；将契约迁移标为待决，不能把这一轮记成完整完成或静默跳过。

### 实施清单

- [ ] `ApprovalInstanceContext` / `ApprovalCompletionContext` 只含必要标识、业务键、状态与快照；不包含 StateJson、ConcurrencyStamp 或跟踪导航。
- [ ] resolver 的 Instance 和 Node 输入从可信绑定实例映射；只复制实际需要的只读字段，不复制整张 FlowDefinition / EF graph。
- [ ] lifecycle 使用显式 preparation result，保留 Title / FormDataJson 的变更能力与未修改 / 赋空语义；Core 在原权限范围内应用结果。
- [ ] 一处 internal mapping 承接 Engine / Query / Forms 共用快照；嵌套集合复制，禁止 Entity 藏在 object 字段中继续传递。
- [ ] 业务 handler 仍在原 DbContext / 事务写宿主业务实体，不能把本地完成回调改为 Outbox 后置业务更新。
- [ ] AccessPolicy 保留默认规则和宿主派生扩展点；Projection 保留隐藏字段 / 审计 Diff 过滤能力。
- [ ] `IApprovalResourceResolver` 已是 DTO，保持原接口；`IApprovalQueryScope` 的数据库组合能力另列评估，不机械 DTO 化。
- [ ] public `ApproveWithDataAsync` 委托、Core 查询返回 Entity、Forms editable adapter 均进入同一兼容性清单，不只处理六个接口名称。
- [ ] 更新必要消费者与接入文档；旧内部映射 / 包装只在无调用且无公共兼容用途时删除。

**文件：** `Abstractions/{Abstractions,ApprovalContext,ApprovalDataExtensions,IApprovalAccessPolicy,IApprovalLifecycleHandler}.cs`；Engine / AssigneeService / resolvers / DefaultPolicy / Query / CommandExecutor；Forms Projection / handler / FieldVisibility；`ApprovalResponses.cs`；Certificate handler / policy / query；受影响 test fixtures、独立消费者和迁移文档。实际路径以本轮引用搜索为准。

**验证：** 所有宿主构建；扩展不能修改 Core 状态；lifecycle 数据准备与业务回滚；当前 AccessPolicy / FieldVisibility 结果；HTTP Response / OpenAPI 契约；独立 NuGet consumer。修改测试前先使用 `dotnet-test:code-testing` 的相关流程，保留关键业务断言。

**影响：** C# API 取决于明确选择；HTTP / DB / 事务 / 业务行为保持。预期收益为减少宿主对 EF graph、运行态和 Core 可写数据的知识依赖。

## 第三轮：Query 与 Forms 模块边界（P1）

### 3A：读取职责与查询往返

- [ ] `ApprovalQueryService` 保留公共入口；优先提取 `ApprovalInstanceReader` 承接 WithDetails、Viewer detail、业务历史、公用定义读取与投影。
- [ ] 列表保留已有 TodoTasks、ToListItems、ApplyFilters 的权威实现；只有迁移后确有重复才提取 Worklist / QueryBuilder，不强制三个类。
- [ ] 将访问与投影需要的定义按不同 DefinitionId 批量预取到读取上下文；保留当前请求内解析缓存，无 Singleton flow cache。
- [ ] 分别测量数据库查询数和宿主权限 / 身份调用数；不通过缓存 Allow、删除 Viewer 规范化或用用户参数代替可信 Actor 省查询。
- [ ] 保持默认 PublicDefinition 的裁剪、无表单投影时拒绝、AsNoTracking、日志顺序和 Tasks 顺序。
- [ ] 保持筛选 `[From, To)`、关键词 / BusinessKey 行为、排序二级 Id、分页 Clamp、列表去重、角色任务、当前查询范围。
- [ ] AsSplitQuery 由实际行放大 / 读取量决定，不作为统一规范批量添加或删除。

**文件：** QueryService、新增 Reader（必要时）、DefaultPolicy 关联读取、HTTP snapshot mapping、Paging / Access / FieldPermission / Response contract tests。

### 3B：Forms 收敛 internal 知识

- [ ] 建一个 Core internal `ApprovalFormsBridge`（位于 Persistence），只提供 Forms 拥有者操作、稳定 ID / pinned definition、限制字段准备、类型注册和必要宿主调用隔离。
- [ ] Forms 模块中的 internal 依赖集中在一个 Forms 内部适配位置；优先使用已有 public `ApprovalAuthorizationService`、`IApprovalHostContext`、DTO 扩展。
- [ ] 不向宿主公开可选择 Engine / Operations / Definitions 写所有者的通用授权方法；Bridge 不能成为公开 WriteGuard 绕过入口。
- [ ] Bridge 加入原 UnitOfWork，公开 UnitOfWork 仍不授予写权限；嵌套 Forms → Engine、附件绑定和失败回滚保持原语义。
- [ ] `InternalsVisibleTo` 不能限定到某个 internal 类型。首步保留 friend assembly，但新增依赖边界测试限制 Core internal 成员的实际引用；诚实记录编译器访问仍然较宽。
- [ ] 架构测试覆盖 Forms 的签名和 IL 成员引用，而非只做 namespace 字符串匹配；禁止新增直接引用 TransactionState、WriteAuthorization、CommandExecutor。公开写保护模型注册经固定入口完成。
- [ ] 不创建新的程序集；如果以后移除 friend，须提供经安全审查的有限协作 API，另行进入 API 兼容决策。

**文件：** Core 新增 Bridge；Forms `ApprovalFormService*.cs`、`ApprovalFileService.cs`、`ApprovalFormsModelBuilderExtensions.cs`、注册；现有 Core Tests 增加模块边界用例。不新增测试项目。

**验证：** Query / Paging / Access / FieldPermission / Forms / Draft / File / Formula 原有用例；陌生文件、隐藏附件字段、普通 Unit 写保护、Forms 外层失败、审批编辑附件绑定、草稿提交删除；Core-only 包消费者仍不依赖 Forms。

**影响：** 纯内部职责重构无新公共契约；查询返回值若尚未完成 DTO 迁移按第二轮决策处理。DB 不变，共享事务与写权边界不变。收益为减少跨模块 internal 知识并降低重复数据读取。

## 第四轮：Authorization 与 DI（P1）

### 4A：授权职责

- [ ] 公共 `ApprovalAuthorizationService` 继续统一入口；DefaultAuthorization 保留 Subject、Provider 调用、权限白名单、超时 / 异常映射。
- [ ] 只提取一个 internal `ApprovalResourceReader`，承接 task / message / outbox / instance / definition 规范资源链。资源解析不决定是否允许操作。
- [ ] Scope 必须仍用于实际 instance 归属验证；Subject 必须先验证 userId 与可信 Actor 一致；系统权限先于宿主 Allow 执行边界判断。
- [ ] 资源解析及 Provider / QueryScope 回调继续处于 Extension 无写权范围；不将它们从安全包装里移出。
- [ ] 保留调用方取消与 Provider 超时的不同错误码；失败不能返回无过滤 Query。
- [ ] 不缓存跨命令 ACL；不单独按 Actor / permission 缓存已解析 Resource，因为资源归属会变化。

**文件：** AuthorizationService、ResourceReader、DefaultPolicy 相关集成、Permission / AccessControl / WriteProtection / Endpoint / replay tests。

### 4B：Composition Root

- [ ] AddApprovalWorkflow 的 public 泛型签名保持；内部 private 注册方法按 Persistence / Authorization / Runtime / Query / Background 组织。
- [ ] 新协作者与可选依赖通过 internal 构造函数注入，保留已有 public 构造方式的兼容用途。
- [ ] 减少 Engine / Definition / Outbox 属性注入；不把内部注册方法变成新 public API。
- [ ] 注册测试核对 Options、TimeProvider、ScopeFactory、Signal、BackgroundRunner、Worker 和业务服务的生命周期；`ValidateScopes` / `ValidateOnBuild` 适用时开启。
- [ ] Composition Root 的 factory / IEnumerable lazy resolution 保留实际用途；新 scope 创建 / 重新绑定 HTTP services 属于重试必要机制，不机械移除。
- [ ] 重复 AddApprovalWorkflow / Forms 的策略先明确；仅拆注册方法不得改变默认 last registration 与 TryAdd / Replace 顺序。
- [ ] Singleton 后台仅持有 scope factory / runner / signal 等安全依赖，不捕获 DbContext / Engine / Host handler。

**验证：** BackgroundRegistration、HostInitialization、DisabledAutomation、HostIdentity、Permission、RequestBodyRetry、Channel / Action override、Core-only / Forms-enabled 两种消费者；所有项目引用构建。

**影响：** 公共授权入口、默认拒绝和权限范围不变；public 注册方法不变；DB / 事务不变。收益为降低 DI 隐藏依赖与资源解析混合职责。

## 第五轮：Definition、冗余与性能（P2）

### 5A：先处理有证据的重复读取

- [ ] 治理摘要批量投影 Management 必要列与 HasDraftChanges 所需字段；消除每 Key 再加载整行 / DraftJson。当前权限过滤及 capability 的含义不变。
- [ ] 记录优化前后 SQL 数、返回行 / payload 量和权限回调数，区分单行数据加载与 ACL 的必要逐资源检查。
- [ ] 复用“最新已发布 Version”查询，仅用于 Definition 列表 / 名称；不替换 `ApprovalDefinitionAvailability.ForNewStarts` 的 active version 规则。
- [ ] 元数据中重复排序 / 集合构造按 catalog 生命周期收敛；描述符可变集合须复制，不能把可变 host 结果放入全局缓存。
- [ ] `ValidateAsync` 作为仓库内默认调用；public sync `Validate` 的兼容处理另列，不能移除权限 / 改为 Task.Run 来规避等待。
- [ ] Reader / Validator / Governance 仅按剩余职责需要拆分；继续使用 FlowDefinitionValidator 和现有 Governance Revision / Audit 权威逻辑。

### 5B：局部性能优化

- [ ] Forms 人员字段先收集、去重、一次 directory lookup，再按字段重建相同错误结果；附件 `ResolveAsync` 已按去重 ID 批量加载，保留该实现及输入顺序 / 授权检查，不重复重构。
- [ ] Backlog 在 SQL 中按 Assignee 聚合 Count / Overdue / Min，再取前十；四 Provider 验证翻译及结果。保持统计公式、时间边界和数据 Scope。
- [ ] flow / form JSON 缓存只在操作 / 读取上下文内，按实际 JSON 值或明确变更版本失效；lifecycle / ApproveWithData / Resubmit 更新后必须读取新数据。
- [ ] 只优化实测热点，不新增跨操作 global cache，不改时间精度、公式舍入或 Provider 专属 SQL。

### 5C：注册规则与最后一次冗余审查

- [ ] 将 resolver 覆盖内置、channel last wins、lifecycle Replace 与 business handler 重复分别记录；保留有文档 / 测试保障的覆盖行为。
- [ ] 需要拒绝重复 BusinessType / 自定义 Key 或增加启动校验时，先列出行为变化与构造副作用，不借“重构”默认启用。
- [ ] HostValidation 目前只校验必需宿主服务；启动时枚举全部 Lazy handler 可能实例化无关依赖，先评估再选择窄校验。
- [ ] 审查重复状态 / DTO / 验证 / 日志 / 时间 / DI 逻辑；删除本轮产生的死代码、internal 包装与无用 partial。
- [ ] `ApprovalHostServices`、公开兼容构造函数、Provider 隔离流程和权限复查有实际边界价值，不能按重复代码指标移除。

**文件：** Definition 两个文件、ExtensionCatalog、Forms Normalize / File Resolve、Statistics、必要的 Query / Runtime context、相关测试和文档。注册规则变化单独纳入行为变更流程。

**验证：** DefinitionContract / Governance / Binding、GovernanceDatabaseContract、ChannelOverride / ActionOverride、Forms / File / Formula、Admin Statistics、HTTP 契约、四 Provider；量化 SQL / payload 改善后才填写性能收益。

**影响：** 内部查询 / 映射保持行为；同步 API 及重复注册拒绝可能涉及契约 / 行为变化，单独决策。DB 不变；提前保存首轮保留原则继续有效。

## 验证命令与门禁

在仓库根目录；xUnit v2 + Microsoft.NET.Test.Sdk，当前 global.json 使用 VSTest 命令模式。新增 / 修复测试按测试技能执行，运行测试前按 `dotnet-test:run-tests` 检查实际配置和可选 overlay。

### 每个主题：先显式构建，再运行相关现有测试项目

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
dotnet build 'Workflow.slnx' --configuration 'Release'
dotnet test 'tests/Approval.Core.Tests/Approval.Core.Tests.csproj' --configuration 'Release' --no-build --no-restore --verbosity 'minimal'
dotnet test 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj' --configuration 'Release' --no-build --no-restore --verbosity 'minimal'
git diff --check
git diff --stat
git diff -- 'src' 'tests' 'docs' 'plans'
```

按影响增加 AspNetCore / Certificate.Tests；scope 更窄时可使用核对后的方法 / 类 Filter。不能以测试命令的隐式构建代替跨 partial / public 变化的独立 Build 证据。

### 每轮结束：完整后端门禁

```powershell
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend'
```

包含 Release build、四个测试项目、九个 Migration 模型漂移检查、ReferenceHost 冒烟和宿主脚本语法检查。

### P0、权限 / 查询翻译改动及最终验收：真实数据库

```powershell
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
```

使用脚本创建的随机专用测试容器 / 数据库，不使用业务库。本次 Docker daemon 未运行；后续必须记录实际执行或明确未验证，不能用模型检查替代。可在本地继续有限纯结构主题，但真实 Provider 验收未完成前，不宣称 P0 事务或多数据库最终验收通过。

### 公共接口 / transport mapping 改动

```powershell
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Packages'
```

Contracts / Packages 按仓库要求使用 pnpm 等既有工具；包含独立消费者构建 / 运行。HTTP JSON 兼容检查之外，第二轮还需编译 C# 扩展实现及继承默认 Policy 的宿主。

## 回归矩阵

| 不变量 | 当前优先复用的测试 |
| --- | --- |
| 启动、Any / All / Sequential、状态完成 | EngineRule、CertificateFlow、Endpoint |
| Parallel / Join、退回、Round、加签 | ParallelReturnAddSign、ParallelConcurrency、EmbeddedApproval |
| 转交、改派、撤回、终止 | Admin、StrictCommandBoundary、WithdrawalEndpoint |
| 超时、通知、提交后信号 | ActionAndTimeout、Notification、Operation、BackgroundRegistration |
| 本地业务同事务、失败回滚 | ActionRollback、HostDbContextIntegration、AtomicDatabaseContract、FinalAcceptanceDatabase |
| 幂等、未知提交恢复、并发 | Operation、OperationEndpoint、RequestBodyRetry、AtomicDatabaseContract、CertificateConcurrency |
| Actor / ACL / Scope / 写权 | Permission、AccessControl、ApprovalWriteProtection、ApprovalWriteProtectionDatabase、PermissionEndpoint |
| 定义版本 / 草稿 / 发布 / 治理 | DefinitionGovernance、DefinitionBinding、GovernanceDatabaseContract、DefinitionGovernanceEndpoint |
| Forms / 附件 / 公式 / 隐藏字段 | Form、Draft、File、FieldPermission、FormFormula、FormEndpoint、FileEndpoint |
| 包和数据 / HTTP 契约 | SingleHostBoundary、SchemaBoundary、HttpResponseContract、ModulePackaging、独立消费者 |

表中简称对应仓库现有 `*Tests.cs`，执行前以实际 test 名称和归属项目核对。新增测试优先覆盖本轮隔离 / 边界风险，不机械镜像新类实现。

## 回滚与 Diff 审查

- 每个主题记录实际起点、文件范围、public / model / transaction 变化与验证结果。需要提交时使用描述性 `refactor:` / `test:` / `docs:` 消息，未经要求不自动提交。
- 出现失败立即停止进入下一主题，修复或撤回仅本主题改动。保留用户的其它工作和 staging，不使用 reset --hard、clean 或全仓覆盖。
- 若已建立主题提交，可审查后用 revert 回滚；未提交时依据主题 Diff 逐项撤回，不能用笼统 restore 覆盖后来修改。
- 本方案不包含 schema migration，因此正常回滚只涉及代码与接口版本；第二轮公开契约迁移必须与宿主 / consumer 一起回滚。
- 审查 TaskId / 时间 / 事件顺序、稳定 ID kind、SaveChanges / Commit 顺序、扩展权限 scope、LINQ 翻译、服务生命周期、新旧兼容入口的真实调用。
- 文档新增是 untracked 文件时 `git diff` 看不到正文，使用文件预览或 Windows 命令 `git diff --no-index -- 'NUL' 'plans/2026-11-03-backend-progressive-refactoring.md'` 审查；差异存在时该命令退出码为 1，不代表审查失败。不要漏掉新文件。

## 逐轮记录与最终交付

| 轮次 | 状态 | Build / Tests / Diff | 遗留项 |
| --- | --- | --- | --- |
| 审计 / 方案 | 已完成 | 初始 Release build 0 warnings / 0 errors；548 passed；SQLite smoke 27 checks | 三个真实服务 Provider 本次未运行 |
| 第一轮 P0 | 本地门禁通过 | 554 passed，0 warnings / errors，模型与 smoke 通过；见 docs/backend-refactoring-round1.md | 三个服务 Provider 本次未运行 |
| 第二轮 P0 | 本地门禁通过 | 557 passed，Contracts / Packages / browser5 passed；见 docs/backend-refactoring-round2.md | C# 变化已授权；真实服务 Provider 待验证 |
| 第三轮 P1 | 本地门禁通过 | 530业务tests passed，Contracts passed；见 docs/backend-refactoring-round3.md | DB专项由GitHub CI；friend范围靠签名/IL测试限制 |
| 第四轮 P1 | 未开始 | 待实施记录 | 不改变 ACL / 注册顺序 |
| 第五轮 P2 | 未开始 | 待实施记录 | sync API / duplicate-key 规则另行决策 |

每轮报告写明：实际结构变化、每个文件及改动、删除的重复实现、模块依赖收敛、性能数据、公共 API / DB / 事务兼容性、精确命令和 Passed / Failed / Skipped、未验证内容与原因。

最终验收以实际成果评价可维护性、扩展性、冗余和耦合，不以新增类数量、partial 目录变化或仅编译成功宣称完成。
