# 单宿主诊断与可选维护

ApprovalDiagnosticsService 返回数据库就绪/迁移漂移、活跃实例、Outbox 积压与失败、超期任务、业务占用不一致、附件删除及单宿主后台心跳。Returned 与 Running 都是活跃实例，不能归档/释放占用。匿名 /health/approval 仅返回 Ready，不暴露实例、用户或配置。

不再返回过期租约、租户轮转或多 Worker 状态。后台只有 ApprovalWorker；Outbox、维护和附件删除不再领取持久租约。ApprovalMaintenanceGate 仅防止同一进程中重复执行同一作业，不能用来支持集群。

Lifecycle.Enabled=false、AllowDeletion=false 是默认值；保留期默认不设。预览与诊断可用，但不会自动创建删除任务。显式启用后只执行已授权创建的维护作业，每批重新核对资源范围、创建者权限、冻结策略与保留标记。

批次快照、在线记录变更、审计和游标同事务提交；失败不推进游标，重启后从已提交状态继续。永久幂等标记不删除；未完成 Outbox、活跃实例、被保留的数据不按统一年龄清理。实际附件先登记删除任务再幂等删除；取消确认前仍保留恢复能力。

HTTP 保持 maintenance/preview、maintenance/jobs、maintenance/holds、archives 等既有路径，权限由宿主提供。维护是兼容的可选能力，不是独立调度平台。
