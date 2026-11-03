# 0.3 升级与历史数据

这是明确的边界收敛版本。NuGet 与 @approval/ui 均升为 0.3.0。升级前按宿主策略备份业务数据库和实际附件；历史 Migration 文件保持原样。

现有九套示例/数据库测试迁移新增 EmbeddedEngineBoundary，最新 snapshot 退出维护实体管理。Up/Down 不删除旧表、索引或外键，因此旧 Archives、MaintenanceJobs、MaintenanceAudits、RetentionHolds、FileDeletions、快照与附件元数据保留。它们不再由审批运行时读取或自动执行，后续导出/删除由宿主数据库策略决定。

旧归档关联附件继续保存 ArchiveId 和存储键，新在线附件接口不绕过授权开放旧归档数据；宿主使用自己的历史数据读取/导出方式。已写入旧删除意图不会被新引擎执行。新宿主根据 ConfigureApproval / ConfigureApprovalForms 建立自己的迁移，不需要这些旧维护表。

已擦除操作结果的旧 IsTombstone 标记保留，旧键不允许重新执行；新命令结果没有自动过期。旧 Notification Outbox 行投递时转换为 ApprovalEvent，宿主以原 ID 幂等接收。

改造宿主时去掉审批基类继承，保留已有 DbContext/IdentityDbContext 结构；模型调用 ConfigureApproval，可选 ConfigureApprovalForms。DI 自动安装必要 EF 拦截器，设计工厂/手动构造使用 UseApproval。

把 AddFormApprovals 改为 AddApprovalForms 并增加 Forms HTTP 注册；文件设置移入 ApprovalFormOptions.Files。删除 Lifecycle 配置与旧维护/归档 endpoints。把渠道/Notifier 适配器迁移为 IApprovalEventHandler；宿主可继续处理通知规则的路由提示。

移除旧管理员/设计者角色配置并注册明确的权限实现。未注册时默认拒绝。回退应用版本时旧表仍在；若宿主另行清理了这些数据，回退需使用宿主备份，不应修改 Migration 历史。

第三轮写边界收尾没有新增数据库字段或迁移。共享上下文的直接审批写入现在拒绝；业务写入保留。已有直接改 Task/Instance/Operation/Draft/File 的宿主代码必须改用拥有它的服务，不应移除 UseApproval。

新的启动检查会拒绝启用 Dispatcher 却未注册处理器的宿主，包括只关闭后台调度的宿主。注册真实 IApprovalEventHandler，或明确设置 Outbox.EnableDispatcher=false 并接受事件不会投递的行为。RequireOperationId 默认仍为 false；已经开启严格模式的宿主必须用带键 executor，不能以裸 UnitOfWork 代替。批量改派可使用新增重载/HTTP Header，原无键入口在兼容模式保留。

流程 schemaVersion、channels、字段权限 JSON、历史 Migration、ArchiveId、DeletionPending、旧存储键和 Tombstone 读取继续保留。回滚代码会重新开放旧写入缺口，不能以删除幂等/Outbox 或清库解决失败；业务数据与操作结果不自动清理。
