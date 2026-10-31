# 设计器交互增强 + 动作节点 + 节点通知 + 超时提醒

## 背景与目标

1. 设计器：从左侧面板拖放节点到画布任意位置；拖到连线上自动插入（A→B 变为 A→新→B）；撤销 / 重做。
2. 动作节点（Action）：注册动作（C# 类，参数描述自动生成设计器表单）、参数模板自动解析、HTTP 请求、JS 脚本（Jint 沙箱）。
3. 节点通知：事件（到达 / 同意 / 驳回 / 转交 / 超时 / 流程结束）× 接收人 × 渠道 × 消息模板。
4. 可靠发送：站内信与审批数据同一事务写入；外部渠道与异步动作写入 Outbox 表，由后台任务发送，失败指数退避重试。
5. 超时提醒：节点设置时限，后台定时扫描超时待办 → 提醒 / 重复催办 / 自动同意 / 自动驳回。

非目标：工作日历（按自然时间计算时限）、并行网关、驳回到指定节点、分布式调度框架（Hangfire / Quartz）。

## 关键不变量

- 旧流程定义 JSON 无需修改即可继续使用（新增字段均为可选）。
- 审批状态、业务回调、站内信、Outbox 记录在同一次 SaveChanges 中提交；任一失败全部回滚。
- 外部副作用（HTTP 异步动作、外部渠道通知）只在提交成功后由后台任务执行，至少一次投递；以 Outbox Id 作为幂等键。
- 同步动作在事务内执行，失败则本次审批操作回滚（可配置“失败继续”）。
- 脚本沙箱：无 CLR 访问，限制执行时间、内存、语句数、递归深度；脚本代码不做模板替换（防注入）。
- HTTP 动作只允许访问白名单主机（防 SSRF）。

## 定义格式新增（全部可选）

```jsonc
{
  "notifications": [ /* 流程级规则：对所有审批节点生效 + 流程结束 */ ],
  "nodes": {
    "dept": {
      "type": "Approval",
      "timeout": { "minutes": 1440, "action": "Remind", "repeatMinutes": 240, "maxReminders": 3 },
      "notifications": [
        { "events": ["Enter"], "recipients": [{ "type": "Assignees", "value": "" }],
          "channels": ["InApp", "Webhook"], "title": "待审批", "content": "【待审批】{标题}，申请人 {申请人}" }
      ]
    },
    "archive": {
      "type": "Action", "next": "end",
      "actions": [
        { "type": "Http", "name": "同步档案", "mode": "Async",
          "parameters": { "method": "POST", "url": "{appBaseUrl}/api/demo/webhook", "body": "{\"title\":\"{标题}\"}" } },
        { "type": "Script", "name": "计算风险", "mode": "Sync", "parameters": { "script": "form.risk = form.amount > 1000 ? 'high' : 'low'" } }
      ]
    }
  }
}
```

## 新增表

| 表 | 用途 |
|---|---|
| `ApprovalMessages` | 站内信（每个接收人一行，已读状态） |
| `ApprovalOutbox` | 待投递的外部通知与异步动作：状态、重试次数、下次执行时间、最后错误 |
| `ApprovalTasks` 新列 | `DueAt`、`RemindCount`、`LastRemindedAt` |

示例使用 `EnsureCreated`，需删除旧的演示库文件后重建；正式项目需通过 EF Core 迁移添加表和列。

## 实施清单

- [ ] 前端：flowModel 增加 `insertNodeOnEdge`、`findEdgeAtPoint`；`history.ts` 快照撤销栈；面板拖放；快捷键
- [ ] Core：定义模型（Action 节点、NotificationRule、TimeoutPolicy、ActionStep）+ 校验
- [ ] Core：模板渲染 `TemplateRenderer`；`IApprovalActionHandler` + Http / Script 内置动作
- [ ] Core：`IApprovalChannel`（Webhook / 企业微信机器人 / 钉钉机器人 / 邮件），站内信内置
- [ ] Core：引擎在各事件点生成通知；Action 节点执行；任务时限
- [ ] Core：`ApprovalOutboxDispatcher`、`ApprovalTimeoutScanner` 两个 BackgroundService
- [ ] 示例：演示 Webhook 接收端（可模拟失败）、注册自定义动作、消息 / 投递记录 API、流程 JSON 增加通知与时限
- [ ] 前端：节点面板（时限、通知、动作步骤）、流程设置面板、消息中心、投递记录页
- [ ] 测试：后端 xunit、前端 vitest；浏览器端到端验证

## 验证

- `dotnet test -c Release`；`pnpm typecheck && pnpm test && pnpm build`
- 浏览器：拖放 / 插入 / 撤销；发起审批 → 站内信、Webhook 收到消息；模拟 Webhook 失败 → 投递记录显示重试后成功；短时限节点触发催办。

## 回滚

所有新增字段可选、旧接口（`IApprovalNotifier`）保留；回滚代码后旧表结构仍可用（新表 / 新列被忽略）。

## 风险

- 同步 HTTP 动作在事务提交前执行，若随后提交失败，外部调用已发生 → 文档建议有副作用的调用使用异步模式。
- 至少一次投递可能重复 → 接收方按 `Idempotency-Key` 去重。
- 多实例部署时 Outbox 通过乐观并发抢占，超时扫描通过任务并发令牌防重复。
