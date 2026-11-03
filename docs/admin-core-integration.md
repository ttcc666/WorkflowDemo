# Admin.Core 集成入口（历史集成记录）

当前单宿主接口已发生变化：移除租户上下文与隔离字段，新增 Returned、独立抄送阅读和可靠宿主事件。本文记录的外部仓库基线未在本次修改，请以 [当前接入指南](embedded-host-integration.md) 适配并在外部宿主独立验收，不可把历史集成结果当成本版已验证。

2026-11-01 已将注册证审批模块接入同级目录的 Admin.Core，基线为 `47961ee33b2bdcae8104015e82b61f41b8372f78`，分支为 `codex/registration-approval-integration`。

本地目录：

```text
D:/Study/CSharp/
├─ Workflow/      审批引擎、注册证业务及 @approval/ui
└─ Admin.Core/    实际集成宿主、模块、菜单和权限
```

- [运行、人员配置、生产发布与恢复](../../Admin.Core/docs/approval-integration.md)
- [集成验收及逐项测试证据](../../Admin.Core/docs/approval-integration-validation.md)
- [原实施方案](../plans/2026-11-01-admin-core-integration.md)

当前通过项目引用构建 `samples/Approval.Certificate` 示例类库，Admin 模块调用 `AddCertificateModule()`，不再链接示例项目的业务源码。注册证是业务集成示例，不属于审批框架的 NuGet 发布范围。注册证与审批仍共用 EF Core 事务；Admin 身份、部门和权限使用其 FreeSql 主库。数据库被绑定到单一企业范围，不能通过改变配置改属其他企业。

注册证类型直接引用 Approval.Certificate，不提供旧程序集转发。当前项目尚未正式使用，数据库直接采用当前 InitialApproval 模型。模块的默认流程与 Admin 自己的 `ApprovalFlows` 分别管理，宿主决定发布哪套定义。工程化验证见[验收记录](engineering-validation.md)。

本仓库提供两处宿主扩展：

| 扩展 | 作用 |
|---|---|
| `ApprovalUIOptions.fetcher` | 复用宿主认证请求、令牌刷新和取消，并保留原始 Response；未设置时仍使用浏览器 fetch |
| `IApprovalHttpUserDirectory` | 可选的公开人员目录限制；HTTP 搜索及回显优先使用它，内部资格检查及 `/me` 继续使用原有 `IApprovalUserDirectory` |

Admin.Core 的公开人员目录应用宿主部门和本人数据范围；后台及跨部门审批使用固定企业的权威目录。前端页面使用 Admin 动态菜单的具体视图路径，并保留 `approval-*` 路由名称。

开发启动与验证命令在运行手册中。真实员工、通知、备份调度和生产配置仍需部署环境验收；运行验证未修改实际业务数据库，包发布和生产部署独立执行。
