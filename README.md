# Approval Workflow

一个轻量、可嵌入 ASP.NET Core 业务系统的通用审批引擎，基于 .NET 10 / EF Core。当前版本 **0.3.0**。

支持流程定义与不可变版本、或签/会签/依次审批、条件与并行汇聚、转交/加签、退回与重新提交、撤回/终止、抄送及已读、审计、查询、事务幂等和 Outbox。用户、组织、认证、授权、业务数据、通知渠道和实际文件存储由宿主提供。

## 安装与接入

只需要业务审批时安装 `Approval.Core`、`Approval.AspNetCore`。动态表单额外安装 `Approval.Forms`、`Approval.Forms.AspNetCore`。

```csharp
// AppDbContext 可以继承 DbContext、IdentityDbContext 或宿主已有基类。
services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
services.AddApprovalWorkflow<AppDbContext>(o => o.RequireOperationId = true)
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

审批 DI 自动为宿主 EF options 安装 `UseApproval()` 写保护；手动构造上下文时在 options 中显式调用该方法。宿主原有 SaveChanges override、EF 拦截器与业务模型继续使用。宿主自行创建和应用 EF Migration。

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapApprovalEndpoints().RequireAuthorization();
```

未注册宿主权限实现时人工操作默认拒绝。系统操作只接受审批后台创建的显式能力集合；角色命名和管理员身份由宿主决定。

## 业务与审批同一事务

通过 `IApprovalCommandExecutor` 执行完整业务委托：委托从新的 DI scope 获取同一个 AppDbContext 和 ApprovalEngine，写入业务数据并启动审批。命令执行器在数据库事务内预约稳定操作键、核验请求摘要并保存结果；数据库提交结果未知时用新上下文恢复已提交结果。

`IApprovalUnitOfWork` 可将多个本地审批调用与同一上下文的业务数据组合提交。不支持裸外部事务或假设两个不同 DbContext 自动共享事务。严格模式要求稳定 OperationId / HTTP `Idempotency-Key`。

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

所有业务事件随审批事务写入 Outbox，由单个 Worker 至少一次投递给 `IApprovalEventHandler`。Event.Id 稳定，宿主以该 ID 幂等处理外部副作用。旧通知规则可作为 `NotificationRequested` 事件携带接收人、内容与宿主路由提示；Core 不注册任何发送渠道。

前端包 `@approval/ui` 支持 Vue 3 / TypeScript、审批页面与设计器。当前前端仍将表单组件放在同一 npm 包中；后端 Forms 已是独立可选包。CSS 从 `@approval/ui/style.css` 引入，契约从 `@approval/ui/contracts` 引入。

## 文档与验证

- [架构与依赖](docs/architecture.md)
- [宿主接入](docs/embedded-host-integration.md)
- [事务与幂等](docs/transactions-and-idempotency.md)
- [权限](docs/permissions.md)、[事件](docs/events.md)、[Forms](docs/forms.md)
- [0.3 升级与数据保留](docs/upgrade-0.3.md)
- [数据库验证](docs/database-validation.md)、[包和契约](docs/contracts-and-release.md)
- [本轮实际交付报告](docs/round2-report.md)

在仓库根目录执行 `pwsh -NoProfile -File ./eng/verify.ps1`。三个服务数据库由 `eng/verify-databases.ps1` 在独立测试容器中验证；SQLite 纳入默认后端测试。发布验证会打包四个 NuGet 包及 npm 包，复制独立消费者，禁止消费者引用框架源码，验证包摘要并实际构建/运行。
