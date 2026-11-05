# 嵌入已有宿主

2026-11-03 上下文重构改变了 C# 扩展签名，接入或升级时按 [上下文 API 迁移](backend-context-api-migration.md) 修改业务、resolver、准备和投影实现；HTTP JSON 与数据库模型保持不变。

先 AddDbContext<TContext>，再 AddApprovalWorkflow<TContext>；TContext 可以继承 DbContext、IdentityDbContext 或自定义基类。OnModelCreating 先调用宿主 base，再 ConfigureApproval，可选 ConfigureApprovalForms，然后继续宿主自己的模型配置。

DI 使用 EF ConfigureDbContext 安装 UseApproval 拦截器，不要求改写宿主继承结构。设计工厂或手动 new DbContext 时显式安装 UseApproval。不要覆盖或移除这些拦截器；已发布定义不可变、审计只追加、回调禁止自行提交和失败作用域不能复用的保护依赖它们。

直接 `db.Set<ApprovalTask>()` 改状态、删除日志、伪造 Operation 或修改 Forms 元数据会被拒绝；通过公开 UnitOfWork 包裹也不会获得权限。先修改审批实体再调用引擎，以及调用结束后夹带修改，也不能随最终提交保存。宿主自己的业务实体继续正常保存；从数据库加载的审批对象可以读取，手工 Attach 的伪造对象不能成为引擎的权威状态。

启用 Dispatcher 时，启动检查必须能构造 IApprovalIdentityProvider 和 IApprovalEventHandler。非 Web 宿主通过 IApprovalContextResolver 提供可信操作人；系统 scope 使用明确的允许能力集合。池化 Context 的租期切换会清空上一请求的失败/授权状态，但同一个 DbContext 仍须遵守 EF 的非并发使用规则。

## 最小宿主接入示例

以下以 SQLite 为例。`HostUsers`、`HostPermissions` 与 `HostEvents` 由宿主实现。`HostUsers` 同时提供用户目录，`HostPermissions` 根据可信操作人及资源判断能力，`HostEvents` 按事件 ID 幂等处理外部副作用。

```csharp
services.AddDbContext<InvoiceDbContext>(o => o.UseSqlite(connectionString));
services.AddApprovalWorkflow<InvoiceDbContext>()
    .AddIdentityProvider<HostUsers>()
    .AddUserDirectory<HostUsers>()
    .AddPermissionProvider<HostPermissions>()
    .AddEventHandler<HostEvents>();
```

`InvoiceDbContext.OnModelCreating` 先调用 `base.OnModelCreating(modelBuilder)`，再调用 `modelBuilder.ConfigureApproval()`，并配置宿主的 `Invoice` 实体。宿主负责 EF Migration。DI 已为该上下文安装 `UseApproval()`，设计时工厂或手动构建 EF options 时也要显式调用它。

HTTP 宿主可通过 `AddApprovalAspNetCore()` 和 `MapApprovalEndpoints().RequireAuthorization()` 集成标准入口，当前操作人由宿主认证身份解析。自定义身份提取使用 `IApprovalCurrentUser`，非 HTTP 宿主注册 `IApprovalContextResolver`。宿主的自定义写入口从可信身份读取 `actorId`，从 HTTP `Idempotency-Key` 读取 `operationId`。非 HTTP 调用同样为每次业务意图保留一个稳定键。

## 在同一事务提交业务数据与审批

测试宿主可通过 `Approval.Testing` 安装可选的身份、目录、权限、事件和时间替身，同时保留实际 DbContext、审批命令和 Outbox。 [宿主测试工具包](testing-toolkit.md)

假设已发布 `invoice` 流程，且 `commands` 是当前可信请求作用域中的 `IApprovalCommandExecutor`。

```csharp
var businessKey = invoiceId.ToString("D");
var instanceId = await commands.ExecuteAsync(
    new ApprovalCommand("invoice.submit", $"invoice/{businessKey}", operationId,
        new { invoiceId, amount }, ApprovalPermissions.InstanceStart,
        new ApprovalResource { DefinitionKey = "invoice", BusinessType = "Invoice", BusinessKey = businessKey }),
    async (sp, token) =>
    {
        var db = sp.GetRequiredService<InvoiceDbContext>();
        db.Invoices.Add(new Invoice { Id = invoiceId, Amount = amount });

        var engine = sp.GetRequiredService<ApprovalEngine>();
        var instance = await engine.StartAsync(new StartApprovalRequest("invoice", "Invoice", businessKey, actorId)
        {
            FormData = new { amount }
        }, token);
        return instance.Id;
    }, ct);
```

执行器在同一事务中先预约键，再运行完整委托，最后提交业务数据、审批、操作结果和 Outbox。每次数据库策略重试会使用新的作用域，委托只捕获业务输入等值，DbContext 和审批服务均从委托参数解析。重试保留原 `Kind`、`Scope`、`OperationId` 和完整 `Payload`，确保所有影响业务决策的输入均参与摘要。裸 `IApprovalUnitOfWork` 无法预约操作键，两个 DbContext 也不会自动共享事务。

独立 NuGet 消费者使用已有 `IdentityDbContext` 和 `SaveChanges` override，`verify-packages.ps1` 验证迁移、发起、加签、退回、重提、并行和事件。可选 Forms 由 Certificate.Api 验证。[可运行的 EmbeddedHost 示例](../eng/consumers/EmbeddedHost/Program.cs)、[幂等恢复](transactions-and-idempotency.md)、[事件处理](events.md)

当前产品边界：单宿主、单应用实例的嵌入式通用审批引擎，Forms 可选，samples 仅为集成验证。宿主拥有用户、组织、权限和外部副作用；不包含多租户平台、服务任务、持久等待、工作日历、SLA 或审批代理。本轮计划能力及验收状态见[阶段一产品化计划](../plans/2026-11-04-approval-productization.md)，未验收项不代表已实现。
