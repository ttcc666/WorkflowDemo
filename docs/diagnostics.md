# 运行诊断

/health/approval 是可选匿名 Ready 布尔值，/api/approval/diagnostics 要求 approval.diagnostics.read。报告包含模型漂移、待迁移数量、实例/任务/Outbox 积压、业务占用一致性和受限处理人样本；不返回连接字符串或任意配置。

核心诊断不包含附件删除、保留周期或归档作业。UI 的运行诊断页面独立于审批流程监控。ApprovalWorker 只记录 Outbox 与超时扫描心跳；宿主可通过 IApprovalDiagnosticsContributor 补充自己的运行指标。
