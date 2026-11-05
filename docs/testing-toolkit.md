# Approval.Testing 宿主集成测试工具包

在宿主的测试项目引用 `Approval.Testing` NuGet。测试宿主继续注册自己的 DbContext、数据库 Provider 和 `AddApprovalWorkflow<TContext>()`，使用 `ConfigureApproval()` 加入审批模型。仓库的独立示例使用 SQLite 内存数据库和持续打开的连接，真实 Provider 的约束和并发场景应在宿主选用的数据库中验证。 [独立消费者示例](../eng/consumers/TestingHost/Program.cs)

```csharp
var approval = services.AddApprovalWorkflow<AppDbContext>(options =>
{
    options.EnableBackgroundServices = false;
});
approval.AddApprovalTesting(options => options.InitialUtcTime = DateTimeOffset.UnixEpoch);

await using var provider = services.BuildServiceProvider();
var users = provider.GetRequiredService<ApprovalTestUsers>();
users.SetUser("alice", "Alice", ["reviewers"]);
var permissions = provider.GetRequiredService<ApprovalTestPermissions>();
permissions.Grant("alice", ApprovalPermissions.InstanceStart, resource => resource.DefinitionKey == "invoice");

using var actor = provider.GetRequiredService<ApprovalTestActor>().Use("alice");
await using var scope = provider.CreateAsyncScope();
var executor = scope.ServiceProvider.GetRequiredService<IApprovalCommandExecutor>();
```

注册放在测试宿主常规审批注册之后。`UseTestActor`、`UseTestUsers`、`UseTestPermissions` 和 `UseTestEvents` 分别控制操作人、用户目录/角色/状态、授权和事件处理器的替换。保留宿主的 HTTP Claims 流程时设 `UseTestActor = false`。测试真实宿主授权或事件代码时关闭对应替换项。设置 `InitialUtcTime` 时可从 DI 获取 `FakeTimeProvider` 并调用 `SetUtcNow` 或 `Advance`。同一个审批 DI scope 固定一个已解析的操作人，切换操作人时为下一次调用创建新 scope。

权限默认拒绝。按操作人、能力及资源条件显式授权，`Revoke` 删除对应授权，`Failure` 模拟权限服务故障。用宿主自己的 `IApprovalQueryScope` 验证查询过滤后再分页和计数。不同测试宿主的替身状态彼此隔离，测试操作人按异步调用链隔离。

用真实 `IApprovalCommandExecutor` 执行稳定键命令。每个尝试从执行器提供的 `IServiceProvider` 解析宿主 DbContext 和 `ApprovalEngine`。用新的 scope 读取持久化结果，检查业务数据、审批状态、Operation 及 Outbox 的原子性。提交结果未知时按原键和原内容重试。独立示例还覆盖授权拒绝、资源隔离、用户停用、同意、驳回、退回、重新提交、转交、加签、撤回和并行 Join。

`ApprovalTestEvents.Snapshot()` 返回收到的投递尝试，可按类型、实例和 `Event.Id` 查询；`FailNext` 配置某种事件的失败次数。通过 `ApprovalOutboxDispatcher.DispatchOnceAsync()` 运行真实投递。手动投递示例显式授予后台操作人 `OutboxDispatch`，用可控时间推进重试，并观察同一个 `Event.Id`。宿主按该 ID 对自己的外部副作用去重。

`Approval.Testing` 依赖 Core，测试框架和数据库 Provider 由宿主测试项目选择。生产接入验证由现有 `EmbeddedHost` 独立完成，`TestingHost` 单独从本次打包的 NuGet 验证工具包及真实集成场景。 [包验证脚本](../eng/verify-packages.ps1)
