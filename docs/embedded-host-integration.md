# 单实例宿主接入

引擎与宿主同进程、共用 DI、DbContext 和数据库事务。认证、目录、组织、授权、业务操作及通知发送归宿主。当前 API 不再包含 TenantId、FixedTenantId、MultiTenant、租户上下文/目录/Claim 配置；宿主 DbContext 使用 `ApprovalDbContext(options)`。

## 可信身份与人员

HTTP 默认由 `ClaimsApprovalCurrentUser` 和 `ClaimsApprovalContextResolver` 识别当前用户。非 HTTP 调用实现 `IApprovalContextResolver.Resolve()` 返回 `new ApprovalExecutionIdentity(userId)`，也可在独立可信作用域调用 `ApprovalExecutionContext.InitializeSystem(actorId, allowedPermissions)`；系统主体权限必须显式列明。

注册 `IApprovalIdentityProvider`（角色成员/角色检查）和 `IApprovalUserDirectory`（搜索/回显/用户状态）。ApprovalUser.IsActive 默认为 true，宿主必须准确返回停用状态。没有目录时，可注册 IApprovalUserStatusProvider，仅提供服务端用户状态；UI 选择器仍需要宿主目录，不能用演示用户或前端输入代替。

用户不存在或停用会拒绝发起、处理、转交、加签及管理员改派。宿主权限接口 `IApprovalPermissionProvider` 每次重新判断；`IApprovalQueryScope` 在数据库 count/分页/统计前收窄数据范围。Core 不创建用户、部门、岗位、角色或独立权限表。

## 抄送人员

`GET /carbon-copies/{id}/recipients?page=1&pageSize=20` 同时要求 DirectoryRead、InstanceRead、实例可见性和宿主查询范围。角色名单来自宿主当前成员，响应 `isCurrentRoleMembership=true` 和查询 UTC 时间，不能当作历史发送快照。ID 去重排序后分页，仅请求该页的目录信息；未知用户保留 ID，isKnown=false，姓名和状态为空，停用用户显示停用。HTTP 优先使用宿主 `IApprovalHttpUserDirectory`，避免内部目录姓名越过公开范围；不会借查询标记已读。工作台和实例详情共用人员弹窗，身份改变立即关闭并拒绝旧响应。

## 宿主业务与事件

开始准备实现 `IApprovalLifecycleHandler.OnStartingAsync(ApprovalInstance, CancellationToken)`，通过 `.AddLifecycleHandler<T>()` 注册（后注册替换前注册）。只用于本地校验和准备申请快照/跟踪实体，在条件分支计算前调用；异常回滚完整发起操作。按 BusinessType 区分宿主业务，不再通过流程图配置动作步骤。不能自行 SaveChanges、提交或执行远程副作用。

业务事务回调 `IApprovalBusinessHandler` 保持原契约：与审批同 DbContext/事务，只修改跟踪实体，不自行 SaveChanges 或提交。完整宿主业务调用由 IApprovalCommandExecutor 执行，并传稳定 OperationId。

```csharp
public sealed class HostApprovalEvents(IHostNotificationService notifications) : IApprovalEventHandler
{
    public Task HandleAsync(ApprovalEvent approvalEvent, CancellationToken ct) =>
        notifications.HandleApprovalAsync(approvalEvent.Id, approvalEvent.EventType,
            approvalEvent.InstanceId, approvalEvent.TaskId, approvalEvent.Status, ct);
}

builder.Services.AddApprovalWorkflow<AppDbContext>()
    .AddEventHandler<HostApprovalEvents>();
```

注册处理器后，每次发起、进入节点、创建待办、审批、退回、重提、转交、加签、抄送、撤回、终止及完成都会随审批事务写 Outbox。事件包含固定定义版本、业务键、节点/任务、操作者、意见及 UTC 时间。EventType 使用对应操作名，例如 Submit、NodeEntered、TaskCreated、Approve、Reject、Return、Resubmit、Transfer、AddSign、Cc、Withdraw、Terminate、Complete/Completed。Return 与 Reject 分开；Returned 状态表示等待申请人修改。

投递是至少一次：处理器抛异常会安排退避重试，事件 ID 在全部自动/人工重试中不变；宿主用持久幂等记录去重。远端成功但本地确认丢失可能再次投递，不能声称外部恰好一次。取消时等待处理器退出再释放其作用域。

保留 `.AddChannel<T>()` 兼容已有按流程配置的通知，但具体发送器全部在宿主。Core 不包含 SMTP、机器人或 Webhook；证书宿主 `DemoWebhookChannel` 只是演示。旧定义引用未配置渠道时明确失败并回滚，不静默漏通知。AddNotifier 为原有 best-effort 兼容回调，新接入使用可靠事件。

## 单个后台执行器

默认只注册一个 ApprovalWorker，串行执行 Outbox 和超时扫描；显式启用的维护能力也复用该循环。Outbox 提交后唤醒，超时扫描保持独立间隔。Outbox、维护及附件删除不再持有租约；写入并发字段仍保护人工操作与结果确认。

宿主有自己的循环时配置 EnableBackgroundServices=false，调用 ApprovalOutboxDispatcher.DispatchOnceAsync、ApprovalTimeoutScanner.ScanOnceAsync；不要把它们再注册为 BackgroundService。只能有一个宿主进程处理同一审批数据库。

超时默认提醒。流程配置 AutoApprove/AutoReject 时，只有宿主设置 EnableAutomaticTimeoutDecisions=true 才执行自动决定；否则按 Remind 处理。幂等状态检查防止重复扫描/人工审批竞争推进两次。

## 新库初始化

当前项目尚未正式使用，只提供当前模型的 `InitialApproval` 新库初始化。证书宿主启动时执行 `MigrateAsync`；参考宿主通过 `dotnet ... --migrate` 显式初始化。九套上下文分别覆盖证书 SQLite、参考宿主四 Provider 和测试四 Provider。

不再提供旧 EnsureCreated 数据库导入、升级副本、租户/业务占用回填、混合归属阻断、动作迁移扫描或程序集转发。正常备份、数据库就绪与业务一致性检查保留；初始化不会自动删除已有数据库。

前端通过 `app.use(createApprovalClient(options))` 或 `app.use(ApprovalUI, options)` 安装，每次安装持有独立客户端。组件外使用同一个 client.api/client.http，未安装时显式报错；没有全局配置兼容实例。
