# 开发宿主 C# Handler

通过 `ApprovalBuilder.AddActionHandler<T>()` 注册 Scoped 处理器。相同实现重复注册会复用注册项。不同实现使用相同类型 ID 时注册表拒绝。描述符的 `Type` 是定义契约的一部分，应保持稳定。提供 JSON Schema 的 `properties`、`required`、类型、默认值和枚举可供设计器生成表单。

```csharp
builder.AddActionHandler<ArchiveInvoiceAction>();
```

可运行的 `ArchiveInvoiceAction` 位于 `eng/consumers/EmbeddedHost/Program.cs`。它注入宿主 DbContext，把发票记录加入变更跟踪，再由审批工作单元提交。该示例由以下命令打包并在独立 NuGet 项目中执行。

```powershell
pwsh -NoProfile -File ./eng/verify.ps1 -Scope Packages
```

`ValidateConfig` 在发布及运行时校验配置版本、结构和业务约束，返回可以公开给设计者的错误描述。同步 Handler 可通过正常 DI 使用业务服务，在当前事务暂存实体。保存、提交和重入由引擎保护。同步读取外部数据须显式控制时限和大小。

使用 `context.TryResolveVariable` 读取受限路径，或用 `context.BindParameters` 绑定配置中的 `{{path}}`。自定义参数应在读取后再次验证类型和业务约束。

异步 Handler 声明 Async 和 Retry，由现有 Dispatcher 在独立作用域调用。用 `context.ExecutionId` 作为外部幂等键。为计算类 Handler 提供预演时，实现 `IApprovalActionPreviewHandler`，只使用给定合成数据，不调用真实外部服务。
