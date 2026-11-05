import type {
  ApprovalAction,
  AssigneeKind,
  InstanceStatus,
  OutboxStatus,
  TaskKind,
  TaskStatus,
} from '@ui/contracts/api'

export type TagType = 'primary' | 'success' | 'warning' | 'danger' | 'info'

export const instanceStatusLabels: Record<InstanceStatus, { text: string; type: TagType }> = {
  Running: { text: '审批中', type: 'primary' },
  Returned: { text: '待申请人修改', type: 'warning' },
  Approved: { text: '已通过', type: 'success' },
  Rejected: { text: '已驳回', type: 'danger' },
  Withdrawn: { text: '已撤回', type: 'info' },
  Terminated: { text: '已终止', type: 'info' },
}

export const taskStatusLabels: Record<TaskStatus, { text: string; type: TagType }> = {
  Waiting: { text: '等待中', type: 'info' },
  Pending: { text: '待处理', type: 'warning' },
  Approved: { text: '已同意', type: 'success' },
  Rejected: { text: '已驳回', type: 'danger' },
  Canceled: { text: '已取消', type: 'info' },
  Transferred: { text: '已转交', type: 'info' },
  Suspended: { text: '等待加签', type: 'info' },
}

export const taskKindLabels: Record<TaskKind, string> = {
  Approval: '',
  Resubmit: '待重新提交',
  AddSignBefore: '前加签',
  AddSignAfter: '后加签',
  AddSignParallel: '并加签',
}

export const actionLabels: Record<ApprovalAction, string> = {
  Submit: '提交申请',
  NodeEntered: '进入节点',
  Approve: '同意',
  Reject: '驳回',
  Withdraw: '撤回',
  Transfer: '转交',
  AutoApprove: '自动通过',
  Cc: '抄送',
  Complete: '流程结束',
  Remind: '超时提醒',
  TimeoutApprove: '超时自动同意',
  TimeoutReject: '超时自动驳回',
  Return: '退回',
  AddSign: '加签',
  Resubmit: '重新提交',
  Terminate: '终止流程',
  Reassign: '改派',
  FormEdit: '修改表单',
}

export const outboxStatusLabels: Record<OutboxStatus, { text: string; type: TagType }> = {
  Pending: { text: '待投递', type: 'warning' },
  Succeeded: { text: '已送达', type: 'success' },
  Failed: { text: '失败', type: 'danger' },
}

export function assigneeText(kind: AssigneeKind, value: string) {
  return kind === 'Role' ? `角色：${value}` : value
}

const dateTimeFormat = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
})

export function formatDateTime(value: string | null | undefined) {
  return value ? dateTimeFormat.format(new Date(value)) : '-'
}

/** 距截止时间的描述：已超时 / 剩余时间。 */
export function dueText(dueAt: string, now = Date.now()) {
  const diff = new Date(dueAt).getTime() - now
  const minutes = Math.round(Math.abs(diff) / 60000)
  const span = minutes >= 1440 ? `${Math.floor(minutes / 1440)} 天` : minutes >= 60 ? `${Math.floor(minutes / 60)} 小时` : `${minutes} 分钟`
  return diff < 0 ? { overdue: true, text: `已超时 ${span}` } : { overdue: false, text: `剩余 ${span}` }
}

/** 时长（分钟）的简短描述：45 分钟、2.5 小时、3.2 天。 */
export function durationText(minutes: number | null | undefined) {
  if (minutes === null || minutes === undefined) return '-'
  if (minutes < 60) return `${Math.round(minutes)} 分钟`
  if (minutes < 1440) return `${Math.round(minutes / 6) / 10} 小时`
  return `${Math.round(minutes / 144) / 10} 天`
}
