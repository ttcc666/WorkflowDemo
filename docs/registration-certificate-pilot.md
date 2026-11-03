# 证照业务消费者示例

Approval.Certificate 是宿主业务类库，Certificate.Api 是业务 HTTP 宿主。AppDbContext 直接继承 DbContext，业务表、审批表、可选 Forms 表使用同一上下文和事务。证照提交通过稳定幂等命令执行，结束回调更新业务单据。

示例启用可选 Forms、宿主 DemoIdentityProvider、显式 DemoPermissionProvider 和 DemoWebhookChannel（实现 IApprovalEventHandler）。实际生产系统提供自己的 Identity、权限、组织、通知及文件存储；示例 DemoMode 只由开发宿主明确开启。

历史迁移保留，0.3 边界迁移不删除旧归档/维护数据。运行 dotnet run --project samples/Certificate.Api。独立消费验证会复制此业务宿主、将框架 ProjectReference 替换为 NuGet 引用并构建。
