import type { FlowSettings, FormSchema } from './forms'

export type NodeType = 'Start' | 'Approval' | 'Cc' | 'Condition' | 'Parallel' | 'Join' | 'End'
export type ApproveMode = 'Any' | 'All' | 'Sequential'
export type EmptyApproverPolicy = 'Error' | 'AutoApprove'
export type NotifyEvent = 'Enter' | 'Approved' | 'Rejected' | 'Transferred' | 'Timeout' | 'Completed' | 'Returned' | 'Resubmitted'
export type TimeoutAction = 'Remind' | 'AutoApprove' | 'AutoReject'
/** 表单字段在节点的权限：可编辑（审批节点，表单审批的审批人同意时可修改）/ 隐藏；未设置为只读 */
export type FieldAccess = 'Editable' | 'Hidden'

export interface ApproverRule {
  type: string
  value: string
}

export interface ConditionBranch {
  priority: number
  expression?: string | null
  next: string
}

/** 通知规则：events 中任一事件发生时，按模板给 recipients 通过 channels 发送消息 */
export interface NotificationRule {
  events: NotifyEvent[]
  recipients: ApproverRule[]
  channels: string[]
  title?: string | null
  content?: string | null
}

/** 审批节点时限 */
export interface TimeoutPolicy {
  minutes: number
  action: TimeoutAction
  /** 提醒后仍未处理时，每隔多少分钟再催办一次；为空只提醒一次 */
  repeatMinutes?: number | null
  maxReminders?: number
}

export interface FlowNodeDef {
  id: string
  name: string
  type: NodeType
  mode?: ApproveMode
  approvers?: ApproverRule[]
  branches?: ConditionBranch[]
  next?: string | null
  emptyApproverPolicy?: EmptyApproverPolicy
  /** 审批节点：驳回时可退回到之前的节点或发起人，默认 true */
  allowReturn?: boolean
  /** 审批节点：允许加签，默认 true */
  allowAddSign?: boolean
  allowTransfer?: boolean
  requireComment?: boolean
  requireQualifiedAssignee?: boolean
  notifications?: NotificationRule[]
  timeout?: TimeoutPolicy | null
  /** 表单字段权限（审批节点：可编辑 / 隐藏；抄送节点：隐藏），字段标识 → 权限 */
  fieldPermissions?: Record<string, FieldAccess>
  position?: { x: number; y: number } | null
}

export interface FlowDefinition {
  /** JSON 协议版本；旧定义缺省为 v1，与流程发布版本独立。 */
  schemaVersion?: number
  startNodeId: string
  /** 发起设置（发起中心、发起角色、标题模板） */
  settings?: FlowSettings
  /** 表单结构 */
  form?: FormSchema
  /** 流程级通知：对所有审批节点生效，以及流程结束 */
  notifications?: NotificationRule[]
  nodes: Record<string, FlowNodeDef>
}
