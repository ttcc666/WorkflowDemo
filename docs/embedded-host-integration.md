# 嵌入已有宿主

2026-11-03 上下文重构改变了 C# 扩展签名，接入或升级时按 [上下文 API 迁移](backend-context-api-migration.md) 修改业务、resolver、准备和投影实现；HTTP JSON 与数据库模型保持不变。

先 AddDbContext<TContext>，再 AddApprovalWorkflow<TContext>；TContext 可以继承 DbContext、IdentityDbContext 或自定义基类。OnModelCreating 先调用宿主 base，再 ConfigureApproval，可选 ConfigureApprovalForms，然后继续宿主自己的模型配置。

DI 使用 EF ConfigureDbContext 安装 UseApproval 拦截器，不要求改写宿主继承结构。设计工厂或手动 new DbContext 时显式安装 UseApproval。不要覆盖或移除这些拦截器；已发布定义不可变、审计只追加、回调禁止自行提交和失败作用域不能复用的保护依赖它们。

直接 `db.Set<ApprovalTask>()` 改状态、删除日志、伪造 Operation 或修改 Forms 元数据会被拒绝；通过公开 UnitOfWork 包裹也不会获得权限。先修改审批实体再调用引擎，以及调用结束后夹带修改，也不能随最终提交保存。宿主自己的业务实体继续正常保存；从数据库加载的审批对象可以读取，手工 Attach 的伪造对象不能成为引擎的权威状态。

启用 Dispatcher 时，启动检查必须能构造 IApprovalIdentityProvider 和 IApprovalEventHandler。非 Web 宿主通过 IApprovalContextResolver 提供可信操作人；系统 scope 使用明确的允许能力集合。池化 Context 的租期切换会清空上一请求的失败/授权状态，但同一个 DbContext 仍须遵守 EF 的非并发使用规则。

注册 IApprovalIdentityProvider，并提供 IApprovalUserDirectory 或 IApprovalUserStatusProvider；可选 IApprovalOrganizationProvider 只读取宿主组织数据。权限通过 AddPermissionProvider，事件通过 AddEventHandler。Core 没有内置宿主角色、管理员或用户表。

业务启动请通过 IApprovalCommandExecutor，把业务写入和引擎调用放进同一完整委托，从传入 IServiceProvider 获取上下文。执行器重试时创建新的 scope，不得捕获请求 scope 的已跟踪实体。多个 DbContext 不自动共享事务；裸外部事务明确拒绝。

独立消费夹具 eng/consumers/EmbeddedHost 使用 NuGet 包、已有 IdentityDbContext 和 SaveChanges override，没有 Forms 引用；实际迁移/发起/加签/退回/重提/并行/事件验证由 verify-packages.ps1 执行。Certificate.Api 则验证业务宿主使用可选 Forms 的集成。
