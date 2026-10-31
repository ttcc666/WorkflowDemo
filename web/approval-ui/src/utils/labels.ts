import type {
  ApprovalAction,
  AssigneeKind,
  CertificateStatus,
  CertOperation,
  InstanceStatus,
  OutboxStatus,
  RequestStatus,
  TaskKind,
  TaskStatus,
} from '@/api/types'

type TagType = 'primary' | 'success' | 'warning' | 'danger' | 'info'

export const instanceStatusLabels: Record<InstanceStatus, { text: string; type: TagType }> = {
  Running: { text: '审批中', type: 'primary' },
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
  Approve: '同意',
  Reject: '驳回',
  Withdraw: '撤回',
  Transfer: '转交',
  AutoApprove: '自动通过',
  Cc: '抄送',
  Complete: '流程结束',
  Action: '自动动作',
  Remind: '超时提醒',
  TimeoutApprove: '超时自动同意',
  TimeoutReject: '超时自动驳回',
  Return: '退回',
  AddSign: '加签',
  Resubmit: '重新提交',
}

export const outboxStatusLabels: Record<OutboxStatus, { text: string; type: TagType }> = {
  Pending: { text: '待投递', type: 'warning' },
  Succeeded: { text: '已送达', type: 'success' },
  Failed: { text: '失败', type: 'danger' },
}

export const certificateStatusLabels: Record<CertificateStatus, { text: string; type: TagType }> = {
  Active: { text: '有效', type: 'success' },
  Revoked: { text: '已注销', type: 'info' },
}

export const operationLabels: Record<CertOperation, string> = {
  Create: '新增',
  Update: '变更',
  Revoke: '注销',
}

export const requestStatusLabels: Record<RequestStatus, { text: string; type: TagType }> = {
  Reviewing: { text: '审核中', type: 'primary' },
  Approved: { text: '已生效', type: 'success' },
  Rejected: { text: '已驳回', type: 'danger' },
  Withdrawn: { text: '已撤回', type: 'info' },
}

export const certificateFieldLabels: Record<string, string> = {
  CertNo: '证书编号',
  Name: '证书名称',
  Holder: '持有人',
  CertType: '证书类型',
  IssueDate: '发证日期',
  ExpireDate: '到期日期',
}

export const certTypeLabels: Record<string, string> = {
  Normal: '普通证书',
  Special: '特种证书',
}

export const businessTypeLabels: Record<string, string> = {
  Certificate: '证书审核',
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
