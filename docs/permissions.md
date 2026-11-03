# 审批权限接入

阶段 2 在 HTTP 和 Core 服务内统一校验权限。已有宿主需要提供可信操作人；单租户的 FixedTenantId 不再代表匿名程序可以调用管理服务。数据库、原始 SQL、宿主业务接口仍由宿主自身保护。

```mermaid
flowchart LR
  H[HTTP 端点及权限元数据] --> C[Core 服务]
  C --> A[可信操作人及参数一致性]
  A --> T[当前租户及资源归属]
  T --> S[宿主查询范围]
  S --> P[IApprovalPermissionProvider]
  P --> B[任务资格、自审及字段规则]
  B --> W[执行及提交]
```

权限码定义在 `ApprovalPermissions`。独立授权包括发起、同意、驳回、退回、转交、加签、重新提交、撤回、终止、改派、设计、校验、发布、完整定义读取、投递读取/重试、诊断等。`DefinitionDisable` 和 `Archive*` 为后续阶段保留，本阶段没有新增停用或归档接口。

## 默认适配

- Core 独立宿主通过 ApprovalOptions.AdminRole 和 DefinitionReaders 配置角色。普通审批仍按现有参与关系和任务资格判定；管理权限默认要求 Admin。
- 使用 AddApprovalAspNetCore 后，Core 和 HTTP 均使用 ASP.NET 适配器，保留 ApprovalAspNetCoreOptions.AdminRole、AdminPolicy、DefinitionReaders。DefinitionReaders 可以读取完整定义、设计和校验，发布仍单独授权。
- PermissionPolicies 中的显式映射优先于默认角色规则；policy 的 resource 是 ApprovalPermissionRequest，包含可信租户、操作人、权限码和规范化资源。无 HttpContext 时显式 policy 不放行；离线任务应使用明确的系统授权或独立宿主权限适配器。
- 自定义 IApprovalPermissionProvider 始终保留，不受 AddApprovalAspNetCore / AddApprovalWorkflow 注册先后顺序影响。

```csharp
builder.Services.AddApprovalWorkflow<AppDbContext>(o =>
{
    o.FixedTenantId = "my-company";
    o.PermissionTimeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddAuthorization(o =>
    o.AddPolicy("WorkflowPublisher", p => p.RequireClaim("permission", "workflow.publish")));
builder.Services.AddApprovalAspNetCore(o =>
    o.PermissionPolicies[ApprovalPermissions.DefinitionPublish] = "WorkflowPublisher");
```

只有设计权限的用户不能借校验或设计界面发布。只有诊断/投递读取权限的用户不能重试。`ApprovalViewer.IsAdmin` 不再是可信授权来源；框架从权限服务重算实例监控能力。方法中的 userId / operatorId / InitiatorId 必须与 IApprovalActorContext.ActorId 完全一致；批量交接的 fromUserId / toUserId 是业务目标，不是操作人。

## 第三方权限及数据范围

```csharp
builder.Services.AddApprovalWorkflow<AppDbContext>(o => o.MultiTenant = true)
    .AddPermissionProvider<CompanyApprovalPermissions>()
    .AddQueryScope<CompanyApprovalScope>();
```

CompanyApprovalPermissions 实现 `Task<bool> AuthorizeAsync(ApprovalPermissionRequest request, CancellationToken ct)`，通过 request.Subject 查询权限系统；不能从请求体或未经验证的请求头构造主体。对有 ID 的任务、实例、文件等，框架先验证当前租户归属，再调用 provider。定义和业务信息由数据库规范化；跨租户 ID 返回 404，不发送给第三方权限服务。

完整流程定义列表及历史版本列表在列表权限之外，逐项按实际 DefinitionId 复核 DefinitionReadSensitive；流程筛选摘要逐项复核 DefinitionRead。被拒绝的定义不进入结果，原有排序保留，后续请求重新判定当前权限。权限服务异常或超时仍返回 AuthorizationUnavailable（HTTP 503），不返回未过滤或部分授权的列表。

CompanyApprovalScope 实现 `IApprovalQueryScope.ApplyAsync`，返回收窄的 IQueryable。框架将范围与原租户/业务查询相交，在数据库分页、总数、待办角标、统计、关联消息和投递列表之前应用。不要先分页再逐条远程授权。宿主内部查询可通过 `ApprovalAuthorizationService.ScopeInstancesAsync` 应用同样的范围。批量改派先应用范围，再逐任务检查改派权限；结果保留显式部分成功语义。

默认不缓存授权结果；每次命令重新判定，撤权及租户切换后不复用旧允许结果。provider / query scope 超时或抛错时 fail closed，返回 `AuthorizationUnavailable`（HTTP 503）；调用方取消仍传播取消。PermissionTimeout 默认 5 秒，允许大于零、不超过一分钟。适配器应响应 CancellationToken；框架超时不能终止不遵守取消的宿主后台工作。任何宿主缓存都需自行保证租户隔离、撤权及时生效。

## 能力与界面

`/me` 返回 capabilities 权限字典；实例详情返回实例能力以及任务改派能力；`instances/{id}/my-task` 返回当前合格任务的能力。UI 从这些字段控制按钮、菜单和动作选项，不从 Admin 角色推断权限。未返回的能力按拒绝处理。能力是显示提示，实际提交仍经过 Core 检查；资格、状态、自审和字段限制继续生效。权限系统变更后可调用 `useApprovalUser().reload()` 刷新菜单，详情刷新会重新加载资源能力。

框架端点的 ApprovalPermissionMetadata 在启动时完整校验；新增未分类端点会使宿主启动失败。实例的公开定义继续去掉动作参数等敏感内容；GetInstanceAsync / GetByBusinessAsync 返回原始模型，必须具备 InstanceReadSensitive；完整定义查询必须具备 DefinitionReadSensitive。新的 `GetStartEligibleApproversAsync` 仅在发起权限及可信发起人检查后进行人员预检，不创建实例、不向前端返回完整定义。

## 系统任务及升级

框架后台作用域只授予 OutboxDispatch、TaskTimeout、FileCleanup。启动发布、诊断监控等宿主离线任务须在新作用域、首次解析 DbContext 之前调用 `ApprovalExecutionContext.InitializeSystem(tenant, actor, permissions)`，列出最小权限集合。初始化只能执行一次；系统身份不能伪装普通用户参数。该入口仅供宿主受信任代码，不能暴露为 HTTP API。

证照示例的启动发布仅获得定义读取/发布权限；监控仅获得诊断权限。投递恢复测试通过正式租户后台作用域运行。旧流程算法测试使用测试程序集内的明确授权替身；新增权限测试及实际 HTTP / 包消费者使用生产门禁，没有运行时“兼容放行”开关。

本阶段不修改数据库 schema；旧库迁移仍遵循[租户升级指南](multitenancy.md)。升级时同时更新 Core、ASP.NET、UI 包及生成契约，接入操作人和权限适配器后再开放写入。对后台服务原有的隐式管理调用，需要改为受限系统作用域或具备相应权限的可信操作人。
