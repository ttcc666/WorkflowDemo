# 运行诊断

/health/approval 是可选匿名 Ready 布尔值，/api/approval/diagnostics 要求 approval.diagnostics.read。报告包含模型漂移、待迁移数量、实例/任务/Outbox 积压、业务占用一致性和受限处理人样本；不返回连接字符串或任意配置。

匿名探针通过 `IApprovalHostScopeProvider.GetBackgroundScopesAsync()` 获取宿主范围，逐范围进入可信上下文并创建独立系统 scope。至少有一个范围且全部数据库检查通过才返回 200；空范围、范围解析失败、任一检查失败或五秒共享取消预算耗尽返回 503。遇到失败立即结束探测，只输出 `{ "ready": false }`，不要求客户端提供认证或租户标识。默认单宿主 Provider 提供一个范围。

核心诊断不包含附件删除、保留周期或归档作业。UI 的运行诊断页面独立于审批流程监控。ApprovalWorker 只记录 Outbox 与超时扫描心跳；宿主可通过 IApprovalDiagnosticsContributor 补充自己的运行指标。
