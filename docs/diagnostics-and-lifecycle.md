# 通用诊断、归档与保留策略

默认 `Lifecycle.Enabled=false`、`AllowDeletion=false`，所有可选保留期为空。只读诊断和预览可用，后台不会自动创建破坏性任务。启用维护只允许执行已经授权创建的作业；未绑定附件清理还必须显式配置其保留期。

```mermaid
flowchart LR
  P["只读预览<br/>当前策略摘要 / 候选数"] --> C["创建任务<br/>当前权限 + 策略再次核对"]
  C --> L["领取租约<br/>独立租户作用域"]
  L --> B["串行化批次事务<br/>复查状态 / 保留 / 创建者权限"]
  B --> A["历史快照 / 在线记录变更<br/>永久防重 / 批次审计 / 游标"]
  A --> K["Commit"]
  K --> F["附件删除任务<br/>实际存储删除 / 幂等重试"]
  K --> N["下一批或崩溃后从已提交游标继续"]
```

## 权限与 HTTP

接口相对于 `MapApprovalEndpoints` 前缀。新接口继续受到可信租户、主体和服务端权限检查。

| 接口 | 权限 / 行为 |
|---|---|
| GET `/diagnostics` | `approval.diagnostics.read`；租户内统计、受限处理人样本、后台心跳 |
| GET `/maintenance/preview?target=Instances&mode=Archive` | `approval.archive.read`；只读候选数、样本 ID、策略摘要，不产生任务 |
| POST `/maintenance/jobs` | `archive.execute`，Purge 再要求 `archive.purge`；正文 `{ target, mode, policyHash }` |
| GET `/maintenance/jobs` | `archive.read`；最近 100 项进度，不返回请求正文或存储键 |
| POST `/maintenance/jobs/{id}/run` | `archive.execute`，Purge 再要求 `archive.purge`；执行一批，已知任务 ID 和持久游标提供恢复语义 |
| GET `/maintenance/jobs/{id}/batches?page=1` | `archive.read`；每个已提交批次的数量、主体、时间 |
| POST `/maintenance/holds` | `archive.execute`；`{ target, resourceId, held, reason }`，与保留审计同事务 |
| GET `/archives?page=1` | `archive.read`；归档标记和原资源 ID，不返回快照正文 |
| GET `/archives/{id}/export` | 同时要求 `archive.read` 和独立的 `archive.read-sensitive`；详细历史可包含业务数据和流程参数 |
| GET `/archives/{id}/files/{fileId}` | 同上；只打开该归档关联的附件 |

表中省略的权限均有共同前缀 `approval.`。读取权限不授予执行/删除权限，诊断权限也不授予归档或敏感详情权限。跨租户 ID 在调用外部权限服务前被拒绝。

归档使用独立的 `IApprovalArchiveQueryScope`，应用在 count、分页和按 ID 导出之前。原实例清理后不能从在线查询重建其范围：宿主如已配置自定义 `IApprovalQueryScope`，必须显式注册对应的归档范围，否则归档访问拒绝并返回 503，不回落为整个企业可见。默认没有额外在线范围的宿主保持当前租户及归档权限限制。

写命令使用 `Idempotency-Key`。`run` 是明确的分批恢复入口，不将整段维护伪装成单次原子命令；它不会因复用某个 HTTP 结果而停止后续批次。Core 创建任务、设置保留应通过 `IApprovalCommandExecutor`，尤其是启用 Provider 自动重试时。

匿名就绪探针由宿主显式调用 `app.MapApprovalReadiness()`，默认 `/health/approval`。200/503 的正文只有 `{ "ready": true/false }`；不公开企业、用户、连接、数据库版本或业务内容。证照示例已接入。整体探针核对共享数据库及迁移，不等于承诺全部业务和远程渠道都健康。

## 诊断与观察

`ApprovalDiagnosticsService` 返回迁移/模型就绪、运行中数量、Outbox 待投递/失败/最老积压/过期租约、超期任务、业务占用不一致、删除任务及后台心跳。实例查询范围在统计前应用；处理人检查最多 `Background.AssignmentSampleSize` 项，结果只含问题码和资源 ID。数据库不可用时 `DataAvailable=false`，不能将零值解读成没有积压。

宿主可注册 scoped `IApprovalDiagnosticsContributor`，只返回稳定的小写问题码、布尔状态和数字数量，不返回任意异常文本或配置。贡献检查有限时，失败使用 `contributor_unavailable`。默认日志携带关联 ID 和异常类型，避免输出连接或请求正文。

`ApprovalInstrumentation.Activities` / `Metrics` 名称均为 `Approval.Core`。指标包括命令数量、重试、冲突、命令/事务/后台耗时；指标不附带 TenantId、InstanceId 或 OperationId 等高基数标签。追踪可携带这些关联标识，宿主负责采样、访问控制及导出位置；默认不配置外部 exporter。HTTP 请求追踪可结合宿主 ASP.NET Core instrumentation。

后台按启用租户稳定排序轮转，每轮最多 `MaxTenantsPerSweep`，每企业有 `TenantBudget`，完成/失败留下租户内心跳。某企业失败或超时不会阻塞后续企业；处理器、目录和存储适配必须遵守 CancellationToken。受限系统主体增加 `approval.system.maintenance-run`，不能借此发布流程或冒用用户。

## 保留期与受保护数据

| Target | 保留期 | 行为 |
|---|---|---|
| Instances | ArchiveAfter；PurgeAfter 可选 | 先快照，再按已结束状态清理在线记录；定义与归档附件继续保留 |
| ReadMessages | ReadMessagesAfter | 只处理过期已读消息；关联运行中/保留实例受保护 |
| SucceededOutbox | SucceededOutboxAfter | 只清理成功且无租约的投递 |
| FailedOutbox | FailedOutboxAfter | 仅归档快照，不能按年龄删除；Pending 始终不因保留期清理 |
| BusinessDrafts | BusinessDraftsAfter | 快照保留原内容与附件，再清理在线草稿 |
| DesignDrafts | DesignDraftsAfter | 清理可编辑 JSON，保留发布计数、选用版本、启停和历史发布版本 |
| Operations | OperationsAfter | 清结果正文及其归档副本，永久保留租户/主体/类型/范围/原键/请求摘要 |
| UnboundFiles | UnboundFilesAfter | 只处理未绑定、未归档、未保留文件；提交删除任务后才访问存储 |
| ArchivedPayloads | ArchivedPayloadsAfter | 已清理在线记录的归档详情到期后清正文并排队删除附件，保留归档标记与原摘要 |

保留期均指对应时间边界：实例按 CompletedAt，消息按 ReadAt，Outbox 按 CompletedAt，草稿按 UpdatedAt，命令按 CommittedAt，附件按 UploadedAt，归档详情按在线清理的 PurgedAt。各可选值默认空，配置要大于零。启用删除必须另设 `AllowDeletion=true`。

清理实例前再次检查 Running、待办状态、Pending/Failed Outbox、业务占用及相关保留标记；任何一项仍需要在线数据就不删除。必要附件先改关联到归档，不能成为可复用的未绑定上传。只有另行启用归档详情到期策略并复查保留后，才允许清正文及排队删除附件。

命令正文过期后原键返回 `ReplayExpired` / 410，仍不会重新执行业务；同键不同请求继续冲突。前端保留原键，核对归档后才能显式开始新操作。重放窗口由 OperationsAfter 或实例清理边界决定，永久防重标记没有自动删除入口。

归档存在时在线历史查询仍可用；在线实例清理后使用归档列表和已授权导出入口。详情保留期结束后导出返回 410，归档标记仍可检索。定义版本不会随实例年龄删除。

## 批次、权限撤销与文件失败

任务冻结截止时间和策略摘要；每批从仍符合条件的候选中选择有限记录，不使用已删行的 offset。业务变更、快照、批次审计和游标在同一事务提交。崩溃后租约到期可接管，重试使用新的作用域；失败批次不会推进游标。两个 worker 的结果受租约/并发令牌保护。

每批重新评估持久任务创建者的当前权限。ASP.NET policy 依赖 HTTP 主体时，后台没有该用户请求则拒绝；宿主应提供能按可信 subject 评估的权限 provider。不能用另一位管理员的当前 HttpContext 代替原创建者。策略变化时任务返回冲突，须重新预览和创建；预览不是永久删除许可。

删除任务提交后，文件不能绑定或正常下载。存储 `DeleteAsync` 必须幂等，成功后才移除在线元数据；失败保存短错误码并安排重试。删除真正开始前可用保留取消排队；已经开始后，不能承诺恢复文件并接受新保留。远程删除/响应丢失仍是至少一次，不承诺外部恰好一次。

## 接入与升级

证照示例绑定 `Approval:Lifecycle` 和 `Approval:Background`；通用宿主在 AddApprovalWorkflow 的配置委托中设置对应 options。UI 新增 `maintenance` 路由，分别按诊断、归档、执行、清理及敏感读取能力显示。

追加四 Provider 和证照的 `LifecycleMaintenance` 迁移：归档、维护作业、批次审计、保留、文件删除表，以及文件归档/删除/并发列和命令 tombstone 列。旧迁移保持不变。停写/停 worker、备份后演练迁移，先用关闭维护的配置启动并检查隔离/健康，再显式启用所需保留期。

恢复与告警操作见[三库备份与生命周期运维](backup-and-maintenance-operations.md)。本机验收见计划实施记录；不将故障注入或构建成功等同生产完整恢复演练。
