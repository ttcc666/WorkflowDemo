import type { NodeType, NotifyEvent, TimeoutAction, ApproveMode, ApproverRule, NotificationRule } from '../../contracts/flow'
import type { FlowNodeData } from './flowElements'

export const nodeTypeLabels: Record<NodeType, string> = {
  Start: '开始',
  Approval: '审批',
  Cc: '抄送',
  Condition: '条件分支',
  Parallel: '并行分支',
  Join: '合并',
  End: '结束',
}

/** 条件节点、并行节点可以有多条出线 */
export const isMultiOutput = (type: NodeType | undefined) => type === 'Condition' || type === 'Parallel'

export const notifyEventLabels: Record<NotifyEvent, string> = {
  Enter: '到达节点',
  Approved: '同意',
  Rejected: '驳回',
  Transferred: '转交',
  Timeout: '超时提醒',
  Returned: '退回修改',
  Resubmitted: '重新提交',
  Completed: '流程结束',
}

export const timeoutActionLabels: Record<TimeoutAction, string> = {
  Remind: '提醒 / 催办',
  AutoApprove: '自动同意',
  AutoReject: '自动驳回',
}

/** 各类节点可配置的通知事件；流程级规则对所有审批节点生效 */
export function notifyEventsFor(type: NodeType | 'Flow'): NotifyEvent[] {
  switch (type) {
    case 'Approval':
    case 'Flow':
      return ['Enter', 'Approved', 'Rejected', 'Transferred', 'Timeout', 'Completed', 'Returned', 'Resubmitted']
    case 'Cc':
      return ['Enter']
    case 'End':
      return ['Completed']
    default:
      return []
  }
}

export const modeLabels: Record<ApproveMode, string> = {
  Any: '或签',
  All: '会签',
  Sequential: '依次审批',
}

/** 内置审批人类型的名称（未加载元数据时的兜底，如只读流程图）；设计器以后端元数据 approverTypes 为准 */
export const approverTypeLabels: Record<string, string> = {
  Role: '角色',
  User: '指定用户',
  FormField: '表单人员字段',
  Initiator: '发起人',
  DeptLeader: '部门负责人',
  Manager: '上级',
  ManagerChain: '逐级上级',
  // 仅通知接收人可用
  Assignees: '当前审批人',
  Approvers: '已审批过的人',
}

/** 不需要填写参数的接收人 / 审批人类型（兜底，元数据中 valueKind 为 None） */
export const valuelessApproverTypes = new Set(['Initiator', 'Assignees', 'Approvers'])

/** 组织架构类（参数为级数）的内置类型（兜底，元数据中 valueKind 为 Level） */
export const levelApproverTypes = new Set(['DeptLeader', 'Manager', 'ManagerChain'])

/** 解析级数参数：`2` 或 `2@payee`（以表单人员字段 payee 为基准），无效时返回 null */
export function parseLevel(value: string | null | undefined): { level: number; field: string | null } | null {
  const match = /^\s*(\d{1,2})\s*(?:@\s*([A-Za-z_][A-Za-z0-9_]*)\s*)?$/.exec(value ?? '')
  if (!match) return null
  const level = Number(match[1])
  return level >= 1 && level <= 10 ? { level, field: match[2] ?? null } : null
}

export function formatLevel(level: number, field: string | null) {
  return field ? `${level}@${field}` : String(level)
}

/** 组织架构类审批人的说明，如“直属上级”“第 2 级上级”“逐级审批至第 2 级上级（基准：payee）” */
export function levelRuleText(type: string, value: string, fieldLabel?: (key: string) => string) {
  const parsed = parseLevel(value)
  if (!parsed) return `${approverTypeLabels[type] ?? type}：${value || '未设置'}`
  const { level, field } = parsed
  const text =
    type === 'DeptLeader'
      ? level === 1 ? '所在部门负责人' : `上 ${level - 1} 级部门负责人`
      : type === 'ManagerChain'
        ? `逐级审批至第 ${level} 级上级`
        : level === 1 ? '直属上级' : `第 ${level} 级上级`
  return field ? `${text}（基准：${fieldLabel?.(field) ?? field}）` : text
}

/** 审批人规则的简短说明（节点卡片、摘要） */
export function approverRuleText(rule: ApproverRule, types?: { type: string; displayName: string; valueKind: string }[]) {
  const meta = types?.find((t) => t.type === rule.type)
  const label = meta?.displayName ?? approverTypeLabels[rule.type] ?? rule.type
  const kind = meta?.valueKind ?? (valuelessApproverTypes.has(rule.type) ? 'None' : levelApproverTypes.has(rule.type) ? 'Level' : 'Text')
  if (kind === 'None') return label
  if (kind === 'Level') return levelRuleText(rule.type, rule.value)
  return `${label}：${rule.value || '未设置'}`
}

export function approverSummary(data: FlowNodeData) {
  const rules = data.approvers.map((r) => approverRuleText(r)).join('，')
  if (data.type === 'Approval') return `${modeLabels[data.mode]} · ${rules || '未设置审批人'}`
  if (data.type === 'Cc') return rules || '未设置抄送人'
  if (data.type === 'Parallel') return '所有分支同时进行'
  if (data.type === 'Join') return '等待全部分支完成'
  return ''
}

export const defaultTemplates: Record<NotifyEvent, string> = {
  Enter: '【待审批】{标题}，申请人 {申请人}，当前环节：{节点}',
  Approved: '【已同意】{标题}，{操作人} 同意了 {节点}',
  Rejected: '【已驳回】{标题}，{操作人} 在 {节点} 驳回：{意见}',
  Transferred: '【转交】{标题}，{操作人} 将 {节点} 转交给你',
  Timeout: '【催办】{标题} 在 {节点} 已超过处理时限（截止 {截止时间}），请尽快处理',
  Returned: '退回修改',
  Resubmitted: '重新提交',
  Completed: '【{结果}】{标题}',
}

/** 新建通知规则：超时、到达默认发给当前审批人，其余默认发给申请人 */
export function defaultNotification(event: NotifyEvent): NotificationRule {
  const toAssignees = event === 'Enter' || event === 'Timeout' || event === 'Transferred'
  return {
    events: [event],
    recipients: [{ type: toAssignees ? 'Assignees' : 'Initiator', value: '' }],
    channels: ['InApp'],
    content: defaultTemplates[event],
  }
}
