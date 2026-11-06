# 排查 Action 执行

| 现象 | 检查 |
| --- | --- |
| 发布报告动作未注册 | 检查当前宿主注册和类型 ID。导入预检使用目标宿主的注册表。 |
| 预演停在 HTTP 或 SQL | 查看动作计划。依赖实际查询或响应的下游路径需要执行真实流程。 |
| 审批操作回滚 | 按实例、节点、步骤和执行 ID 对照服务日志。核对输入路径、配置版本、输出预算和 Handler。 |
| Outbox Pending | 检查 Dispatcher、到期时间、积压和当前实例的 Outbox 记录。 |
| Outbox Failed | 查看安全错误类别和 HTTP 状态，修复宿主目标或适配器后通过原记录重试。 |
| HttpDestinationChanged | 将原登记标识恢复到原 URL、方法和凭证别名，随后重试原 Outbox ID。 |
| 退回后没有第二次 ERP 请求 | 异步步骤按实例入队一次。查看原 Outbox 项及其幂等键。 |

在宿主日志按 `InstanceId`、`NodeId`、`ActionId`、`ExecutionId` 或 `OutboxId` 查找动作故障。同步和异步使用同一组结构化字段，包括 Handler 类型、执行模式、错误分类及有界的异常类型和方法栈。日志内容限于这些结构化信息，异常消息、请求正文、配置、文件路径及参数值均排除在外。调用方或 Worker 取消时，当前步骤按取消处理。Handler 自身超时仍按失败策略处理。

生产部署以单 Dispatcher 为边界。进程重启后 Pending 记录会重新读取。若进程在远端成功、本地确认之前退出，同一个 ID 可能再次投递。宿主接收方应按 ID 保存处理结果。

本地运行全量门禁。

```powershell
pwsh -NoProfile -File ./eng/verify.ps1
```

服务数据库的 Provider 回归由 GitHub Actions 矩阵运行。验收记录要关联实际提交 SHA。
