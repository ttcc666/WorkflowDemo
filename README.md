# Approval Workflow

一个轻量、可嵌入 ASP.NET Core 业务系统的通用审批引擎，基于 .NET 10 / EF Core。当前开发版本 **0.3.0**，尚未正式发布。

设计器支持发布前只读模拟与审批人预览，运行诊断提供异常定位和既有安全操作入口；SDK 写命令要求显式稳定键，UI 支持同页原请求恢复。能力边界、权限与接入变更见[预演与异常处理指南](docs/definition-simulation-and-operations.md)。

`samples/` 仅为示例，目前没有其他已部署宿主。代码、初始化迁移和契约共同按当前版本演进，不提供旧宿主、旧消息或旧数据库的兼容层。外部流程 JSON 必须显式包含 `"schemaVersion": 1`；服务通过 `AddApprovalWorkflow` 注册并从 DI 获取。清理范围见[开发期兼容清理报告](docs/unreleased-compatibility-cleanup.md)。

支持流程定义与不可变版本、或签/会签/依次审批、条件与并行汇聚、转交/加签、退回与重新提交、撤回/终止、抄送及已读、审计、查询、事务幂等和 Outbox。用户、组织、认证、授权、业务数据、通知渠道和实际文件存储由宿主提供。

## 安装与接入

只需要业务审批时安装 `Approval.Core`、`Approval.AspNetCore`。动态表单额外安装 `Approval.Forms`、`Approval.Forms.AspNetCore`。

宿主测试项目可使用 `Approval.Testing` 配置测试身份、权限、事件记录和时间。流程设计支持版本对比和将已发布配置导入为新草稿。 [测试工具包](docs/testing-toolkit.md) · [版本对比](docs/definition-version-diff.md) · [导入和导出](docs/definition-import-export.md)

```csharp
// AppDbContext 可以继承 DbContext、IdentityDbContext 或宿主已有基类。
services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
services.AddApprovalWorkflow<AppDbContext>()
    .AddIdentityProvider<HostIdentityProvider>()
    .AddUserDirectory<HostUserDirectory>()
    .AddPermissionProvider<HostPermissionProvider>()
    .AddEventHandler<HostApprovalEventHandler>();
services.AddApprovalAspNetCore();
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureApproval(); // 可传 tablePrefix，之后可继续覆盖 ToTable 等宿主配置。
}
```

审批 DI 自动为宿主 EF options 安装 `UseApproval()` 写保护；手动构造上下文时显式安装。审批实体的新增、修改、删除必须来自拥有它的内部服务，公开 UnitOfWork 不授予写权限；普通 ExecuteUpdate/Delete 同样拒绝。宿主业务 SaveChanges、已有 override 和事务集成继续使用。宿主自行创建和应用 EF Migration。详见[写入保护边界](docs/persistence-write-boundaries.md)。

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapApprovalEndpoints().RequireAuthorization();
```

未注册宿主权限实现时人工操作默认拒绝。系统操作只接受审批后台创建的显式能力集合；角色命名和管理员身份由宿主决定。

## 业务与审批同一事务

通过 `IApprovalCommandExecutor` 执行完整业务委托：委托从新的 DI scope 获取同一个 AppDbContext 和 ApprovalEngine，写入业务数据并启动审批。命令执行器在数据库事务内预约稳定操作键、核验请求摘要并保存结果；数据库提交结果未知时用新上下文恢复已提交结果。

`IApprovalUnitOfWork` 可在带键命令委托内将多个本地审批调用与同一上下文的业务数据组合提交。不支持裸外部事务或假设两个不同 DbContext 自动共享事务。受管理的业务写操作必须提供稳定 OperationId / HTTP `Idempotency-Key`。

严格幂等是固定规则，不提供关闭开关；每次逻辑提交的重试必须复用原键和原内容。只读、定义校验、显式系统能力及文件上传保留各自边界。流程定义服务仅提供异步 `ValidateAsync`，调用时应 `await`，权限检查不再通过同步入口阻塞。

统计最多选取最早的 100,000 个实例（按创建时间、ID 排序）；关联任务与当前积压分别受 `options.Statistics.MaxTaskRows`（默认 250,000）及 `MaxBacklogRows`（默认 100,000）限制。超过预算返回 HTTP 400 / `StatisticsLimitExceeded`，不返回部分任务指标。积压不受日期范围限制，需按流程筛选或使用宿主报表。这些限制约束应用读取量，不承诺消除数据库物理扫描。

## 可选 Forms

```csharp
services.AddApprovalForms();
services.AddApprovalFormsAspNetCore();
services.AddScoped<IApprovalFileStore, HostFileStore>();
// OnModelCreating 中，在 ConfigureApproval 后调用 ConfigureApprovalForms()。
```

Forms 保留动态字段、公式、明细、字段权限、规范化、草稿、快照及附件引用。Core 只持有可选 JSON 快照与业务变量。未注册快照模块时会明确拒绝相应定义，避免丢失字段校验或泄漏隐藏数据。

实际文件存储归宿主；`LocalApprovalFileStore` 仅是宿主可显式选择的 Forms 适配器。审批 Worker 只投递 Outbox 和扫描审批超时，不归档、清理记录或删除文件。

## 事件与前端

所有业务事件随审批事务写入 Outbox，由单个 Worker 至少一次投递给 `IApprovalEventHandler`。Event.Id 稳定，宿主以该 ID 幂等处理外部副作用。流程通知规则直接产生 `NotificationRequested` 事件，携带接收人、内容与宿主路由提示。

启用 Dispatcher 时缺少或无法构造事件处理器会在宿主启动时失败。明确不投递时设置 `o.Outbox.EnableDispatcher = false`：Outbox 继续保存为 Pending；`EnableBackgroundServices = false` 只关闭定时调度，仍允许手动投递并要求处理器。

前端包 `@approval/ui` 支持 Vue 3 / TypeScript、审批页面与设计器。当前前端仍将表单组件放在同一 npm 包中；后端 Forms 已是独立可选包。CSS 从 `@approval/ui/style.css` 引入，契约从 `@approval/ui/contracts` 引入。

## 文档与验证

- [架构与依赖](docs/architecture.md)
- [宿主接入](docs/embedded-host-integration.md)
- [事务与幂等](docs/transactions-and-idempotency.md)
- [权限](docs/permissions.md)、[事件](docs/events.md)、[Forms](docs/forms.md)
- [0.3 升级与数据保留](docs/upgrade-0.3.md)
- [数据库验证](docs/database-validation.md)、[包和契约](docs/contracts-and-release.md)
- [本轮实际交付报告](docs/round2-report.md)
- [第三轮写边界与宿主集成报告](docs/round3-report.md)
- [RC 最终验收报告](docs/rc-report.md)

在仓库根目录执行 `pwsh -NoProfile -File ./eng/verify.ps1`。三个服务数据库由 `eng/verify-databases.ps1` 在独立测试容器中验证；SQLite 纳入默认后端测试。发布验证会打包四个 NuGet 包及 npm 包，复制独立消费者，禁止消费者引用框架源码，验证包摘要并实际构建/运行。

当前产品边界：单宿主、单应用实例的嵌入式通用审批引擎，Forms 可选，samples 仅为集成验证。宿主拥有用户、组织、权限和外部副作用；不包含多租户平台、服务任务、持久等待、工作日历、SLA 或审批代理。本轮计划能力及验收状态见[阶段一产品化计划](plans/2026-11-04-approval-productization.md)，未验收项不代表已实现。
