# 0.3 接入与未发布基线

当前项目尚未正式发布，没有外部消费者或生产历史数据。0.3.0 是仓库内开发版本，不据此推定需要旧生产升级协议。

九套示例／数据库测试上下文各由当前模型重新生成一条 InitialApproval。旧维护表、约束和空 EmbeddedEngineBoundary 迁移已经删除，不提供旧迁移链升级桥。

附件 ArchiveId／DeletionPending 没有当前业务写入者，已连同访问分支和旧保留测试删除。当前附件安全语义仍由上传者、实例访问、隐藏字段和跨实例／草稿引用校验决定。宿主根据 ConfigureApproval／ConfigureApprovalForms 建立自己的迁移。

若本地可丢弃开发库应用过旧基线，应由开发者重建该开发库后应用当前迁移。不要仅更改迁移历史；验证只创建自己的临时测试库，不自动删除用户数据库。

Operation 保留当前 HashVersion／ResultVersion 防止错误重放；已删除 IsTombstone 和 ReplayExpired。Notification 规则直接产生当前 NotificationRequested 审批事件；Outbox 不再有 Kind 字段或旧裸通知转换。命令结果没有自动过期。

改造宿主时去掉审批基类继承，保留已有 DbContext/IdentityDbContext 结构；模型调用 ConfigureApproval，可选 ConfigureApprovalForms。DI 自动安装必要 EF 拦截器，设计工厂/手动构造使用 UseApproval。

使用 AddApprovalForms 并注册 Forms HTTP；文件设置位于 ApprovalFormOptions.Files。业务生命周期与可靠事件分别使用当前扩展接口和 IApprovalEventHandler，通知路由由当前事件的 DeliveryTarget 描述。当前示例和消费者一起更新，没有其他宿主需要旧 API 迁移桥。

注册明确的权限实现，未注册时默认拒绝。实际业务清理与文件存储生命周期由宿主管理。

共享上下文的直接审批写入拒绝；业务写入保留。宿主通过拥有实体的服务修改 Task／Instance／Operation／Draft／File，不应移除 UseApproval。

新的启动检查会拒绝启用 Dispatcher 却未注册处理器的宿主，包括只关闭后台调度的宿主。注册真实 IApprovalEventHandler，或明确设置 Outbox.EnableDispatcher=false 并接受事件不会投递的行为。项目尚未发布，已删除 RequireOperationId 开关和无键批量改派重载；宿主必须使用带键 executor，不能以裸 UnitOfWork 代替。批量改派必须传 OperationId/HTTP Header。统计服务构造器必须接收 ApprovalOptions，统一使用宿主配置的预算。

FieldPermissions／FieldAccess／HiddenFields 与 Channels 当前仍用于表单权限和通知路由。旧轮次报告保留当时事实，当前行为以本文、[事务与幂等](transactions-and-idempotency.md)和[兼容清理报告](unreleased-compatibility-cleanup.md)为准。

外部 JSON 必须显式声明 schemaVersion=1，缺省或不支持的版本会拒绝；C# 直接 new FlowDefinition 初始化为当前版本，序列化会写出版本。前端与生成 Flow Schema 使用同一规则。Engine、DefinitionService、OutboxService 只经 AddApprovalWorkflow 的 DI 路径构造，不保留旧公开构造器。

Dispatcher/TimeoutScanner 已删除忽略旧参数的构造重载；ApprovalDbContextAccessor 的授权、命令执行器及操作人依赖均必填，不再提供省略依赖的入口。

`ApprovalDefinitionService.Validate(flow)` 已删除，使用 `await service.ValidateAsync(flow, ct)`；权限拒绝、取消及超时语义保留。静态纯校验统一使用 `FlowDefinitionValidator.Validate(flow, context)`，通过 `FlowValidationContext` 显式提供审批人元数据、通知路由及可选 Forms 扩展。已删除只接收类型集合的旧重载及 `FormDefinitionValidator` 的静态组合入口；后者仍作为 `IApprovalDefinitionExtension` 提供表单校验。

治理列表统一为 `ListGovernanceAsync(category = null, uncategorized = false, ct = default)`，仅传取消令牌时使用 `ct: ct`。并行运行态只存 Expected 与 ArrivedBranches，到达数直接取去重分支集合的 Count；不再保存未使用的 ActivationId 或重复 Arrived 字段。前端任务直接使用必填 round。

流程定义根节点必须为 JSON 对象；null、数组或数值根节点返回 `InvalidDefinition`（HTTP 400），拒绝发布时不会留下定义、治理状态、审计或幂等记录。

统计新增 `ApprovalOptions.Statistics`，`MaxTaskRows` 默认 250,000、`MaxBacklogRows` 默认 100,000，两者必须大于零且小于 int.MaxValue。超限返回 `StatisticsLimitExceeded`（HTTP 400），整次请求失败，不能将错误当成零积压。现有 `Truncated` 仍仅表示实例数超过 100,000；任务关联精确的受限实例子查询。积压独立于日期，超限时按流程筛选或使用宿主报表。统计仍保留大小写敏感处理人分组，这些读取预算不代表数据库无需扫描。
