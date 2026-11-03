# 宿主授权边界

Core 只定义 approval.task.approve/reject/return/transfer/add-sign、approval.definition.publish、approval.instance.withdraw/terminate 等能力。默认 IApprovalPermissionProvider 拒绝人工操作，即使宿主用户拥有名为 Admin 的角色，也不会隐式授权。

通过 AddPermissionProvider<T> 注册宿主权限服务。可选 ASP.NET PermissionPolicies 将单个能力映射到宿主已有 policy；没有映射的能力默认拒绝。审批库没有 AdminRole、AdminPolicy 或 DefinitionReaders 的角色推断，也不实现 RBAC。

可信 Actor 由宿主身份上下文提供；参数里的操作人必须与当前身份相符。发布/启动/任务/敏感查询都在 Core 再检查权限，HTTP UI 的按钮可见性不能代替服务端判断。系统身份仅由后台 scope 创建且有显式允许能力，HTTP 用户不能请求 system.*。

IApprovalQueryScope 在分页、count、排序、统计前收窄 IQueryable；不得先读全表再过滤。Forms 的字段隐藏同时过滤详情 JSON、变更审计与附件访问。权限或查询范围服务超时/失败安全拒绝并返回 AuthorizationUnavailable。

Certificate.Api 的 DemoPermissionProvider 仅由 DemoMode 明确启用；ReferenceHost 的 DirectoryPermissionProvider 是宿主样例授权实现。生产宿主提供自己的授权规则。
