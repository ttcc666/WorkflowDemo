# 统一事件与 Outbox

```mermaid
flowchart LR
  E["ApprovalEngine"] --> P["ApprovalEvent"] --> O["审批事务内持久 Outbox"] --> H["IApprovalEventHandler"] --> C["宿主邮件 / 推送 / 企业消息 / 内部通知"]
```

事件在审批事务中生成，包含稳定 Id、实例、版本、业务键、操作人、状态和时间。任务创建事件可包含接收人。流程规则生成 NotificationRequested 事件，Notification 携带内容及已解析用户，DeliveryTarget 仅是宿主声明的路由提示。

NotificationTargets 提供设计器可用的提示名称，不注册发送器。宿主只需实现 IApprovalEventHandler；不再提供 IApprovalChannel、AddChannel、IApprovalNotifier 或 AddNotifier。Core 不发送 SMTP/Webhook/短信，也不知道实际消息平台。

ApprovalWorker 单进程串行投递，失败指数退避、次数上限、人工重试和乐观并发确认保留。缺失处理器会失败并保留消息。旧 Outbox Notification 行在投递时转换为同一 ApprovalEvent，原消息 ID 和正文保留。

抄送及逐人已读是审批业务记录。审批中心站内提示可随事务保存；它们与宿主外部通知渠道分开，不会引入独立消息平台。宿主通过 Event.Id 幂等处理副作用。
