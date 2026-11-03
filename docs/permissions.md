# 宿主授权接入

引擎不建设独立 RBAC。宿主通过 IApprovalPermissionProvider 返回当前动作/资源的授权结果；Core 与 HTTP 共用同一授权边界，API capabilities 只用于界面显示，提交时重新校验。

## 可信操作人

HTTP 默认从已验证 Claims 获取 NameIdentifier/sub；业务请求中的 userId、operatorId、角色和管理员标记不能替换可信 Actor。非 HTTP 实现 IApprovalContextResolver 返回 ApprovalExecutionIdentity(actorId)，系统任务用独立作用域的 ApprovalExecutionContext.InitializeSystem(actorId, allowedPermissions)。身份一经初始化不可重置。

```csharp
builder.Services.AddApprovalWorkflow<AppDbContext>()
    .AddIdentityProvider<HostIdentity>()
    .AddUserDirectory<HostDirectory>()
    .AddPermissionProvider<HostPermissions>()
    .AddQueryScope<HostDataScope>();
builder.Services.AddApprovalAspNetCore(o => o.AdminPolicy = "ApprovalAdmin");
```

宿主不提供自定义 provider 时，默认适配现有身份角色/ASP.NET policy，管理、设计和普通处理能力分开。新版本没有 FixedTenantId、MultiTenant 或租户 Claim 配置。

## 资源与数据范围

ApprovalResource 包含定义/实例/任务/文件/消息等资源键。引擎先解析实际资源，再询问宿主权限。普通用户需要同时满足任务所属用户/角色、禁止自审、岗位资格、用户有效状态和节点操作规则，单个通用权限不能越过任务资格。

IApprovalQueryScope 在 count、分页、排序、统计之前收窄 IQueryable；不能查询全表后在前端过滤。归档有独立 IApprovalArchiveQueryScope，原实例已清理时不能从在线记录猜测权限。字段隐藏同时作用于详情表单和修改轨迹。

权限服务失败/超时返回 AuthorizationUnavailable；缺少身份 Unauthorized；明确拒绝 Forbidden；无权查看与不存在保持相同 NotFound 行为。命令重放仍核对当前读取权限，不能用原键绕过撤权。

宿主用户/状态来自 IApprovalUserDirectory 或 IApprovalUserStatusProvider；未知或停用用户不能发起、处理或成为新转交/加签/改派对象。UI 人员选择器需要宿主目录，不允许用演示用户或手工输入假用户替代正式实现。

维护/归档执行与敏感读取、删除是独立授权；默认关闭维护和删除。系统权限只授予显式系统主体的 allowedPermissions 集合，不能由普通用户自行授予。
