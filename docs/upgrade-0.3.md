# 0.3 接入与未发布基线

当前项目尚未正式发布，没有外部消费者或生产历史数据。0.3.0 是仓库内开发版本，不据此推定需要旧生产升级协议。

九套示例／数据库测试上下文各由当前模型重新生成一条 InitialApproval。旧维护表、约束和空 EmbeddedEngineBoundary 迁移已经删除，不提供旧迁移链升级桥。

附件 ArchiveId／DeletionPending 没有当前业务写入者，已连同访问分支和旧保留测试删除。当前附件安全语义仍由上传者、实例访问、隐藏字段和跨实例／草稿引用校验决定。宿主根据 ConfigureApproval／ConfigureApprovalForms 建立自己的迁移。

若本地可丢弃开发库应用过旧基线，应由开发者重建该开发库后应用当前迁移。不要仅更改迁移历史；验证只创建自己的临时测试库，不自动删除用户数据库。

Operation 的 HashVersion／ResultVersion／IsTombstone 不属于附件维护字段，本轮不改变其协议。Notification 规则仍转换为宿主事件，新命令结果没有自动过期。

改造宿主时去掉审批基类继承，保留已有 DbContext/IdentityDbContext 结构；模型调用 ConfigureApproval，可选 ConfigureApprovalForms。DI 自动安装必要 EF 拦截器，设计工厂/手动构造使用 UseApproval。

把 AddFormApprovals 改为 AddApprovalForms 并增加 Forms HTTP 注册；文件设置移入 ApprovalFormOptions.Files。删除 Lifecycle 配置与旧维护/归档 endpoints。把渠道/Notifier 适配器迁移为 IApprovalEventHandler；宿主可继续处理通知规则的路由提示。

注册明确的权限实现，未注册时默认拒绝。实际业务清理与文件存储生命周期由宿主管理。

共享上下文的直接审批写入拒绝；业务写入保留。宿主通过拥有实体的服务修改 Task／Instance／Operation／Draft／File，不应移除 UseApproval。

新的启动检查会拒绝启用 Dispatcher 却未注册处理器的宿主，包括只关闭后台调度的宿主。注册真实 IApprovalEventHandler，或明确设置 Outbox.EnableDispatcher=false 并接受事件不会投递的行为。RequireOperationId 默认仍为 false；已经开启严格模式的宿主必须用带键 executor，不能以裸 UnitOfWork 代替。批量改派可使用新增重载/HTTP Header，原无键入口在兼容模式保留。

FieldPermissions／FieldAccess／HiddenFields 与 Channels 当前仍用于表单权限和宿主通知路由，本轮不拆除这些实际运行契约。旧轮次报告保留当时事实，当前状态以 RC 报告为准。生产接入推荐 `options.RequireOperationId = true;`，详见[事务与幂等](transactions-and-idempotency.md)。
