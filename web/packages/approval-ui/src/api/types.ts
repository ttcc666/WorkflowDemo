// 与后端 JSON 对应的类型（camelCase，枚举为字符串）

import type { FlowSettings, FormSchema } from '../features/forms/formModel'

export type { FlowSettings, FormField, FormFieldOption, FormFieldType, FormSchema, FormValues } from '../features/forms/formModel'

export type InstanceStatus = 'Running' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Terminated'
export type TaskStatus = 'Waiting' | 'Pending' | 'Approved' | 'Rejected' | 'Canceled' | 'Transferred' | 'Suspended'
export type TaskKind = 'Approval' | 'Resubmit' | 'AddSignBefore' | 'AddSignAfter' | 'AddSignParallel'
export type AddSignMode = 'Before' | 'After' | 'Parallel'
export type AssigneeKind = 'User' | 'Role'
export type ApprovalAction =
  | 'Submit'
  | 'Approve'
  | 'Reject'
  | 'Withdraw'
  | 'Transfer'
  | 'AutoApprove'
  | 'Cc'
  | 'Complete'
  | 'Action'
  | 'Remind'
  | 'TimeoutApprove'
  | 'TimeoutReject'
  | 'Return'
  | 'AddSign'
  | 'Resubmit'

export interface ApprovalTask {
  id: string
  instanceId: string
  nodeId: string
  nodeName: string
  assigneeKind: AssigneeKind
  assigneeValue: string
  kind: TaskKind
  parentTaskId: string | null
  round: number
  status: TaskStatus
  order: number
  handledBy: string | null
  comment: string | null
  createdAt: string
  completedAt: string | null
  dueAt: string | null
  remindCount: number
}

export interface ApprovalLog {
  id: string
  instanceId: string
  sequence: number
  nodeId: string | null
  nodeName: string | null
  action: ApprovalAction
  operatorId: string
  comment: string | null
  createdAt: string
}

export interface ApprovalInstance {
  id: string
  definitionId: string
  definitionKey: string
  definitionVersion: number
  businessType: string
  businessKey: string
  title: string | null
  initiatorId: string
  status: InstanceStatus
  /** 并行时为多个节点 ID，逗号分隔 */
  currentNodeId: string | null
  round: number
  formDataJson: string
  createdAt: string
  completedAt: string | null
  tasks: ApprovalTask[]
  logs: ApprovalLog[]
  /** 当前用户的查看级别：Full（发起人、管理员）/ Participant（处理人、候选人、抄送人） */
  access: ApprovalAccessLevel
}

export type ApprovalAccessLevel = 'Participant' | 'Full'

export interface TodoItem {
  taskId: string
  instanceId: string
  definitionKey: string
  definitionName: string
  businessType: string
  businessKey: string
  title: string | null
  initiatorId: string
  nodeName: string
  assigneeKind: AssigneeKind
  assigneeValue: string
  createdAt: string
  dueAt: string | null
  kind: TaskKind
}

/** 列表项（我已处理、我发起的），不含任务与轨迹 */
export interface InstanceListItem {
  id: string
  definitionKey: string
  definitionName: string
  definitionVersion: number
  businessType: string
  businessKey: string
  title: string | null
  initiatorId: string
  status: InstanceStatus
  currentNodeId: string | null
  createdAt: string
  completedAt: string | null
}

export interface ReturnTarget {
  nodeId: string
  nodeName: string
  isInitiator: boolean
}

// ───── 分页 ─────

/** 分页结果：total 为满足条件的总条数 */
export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** 列表查询参数（空值不传）：page 从 1 开始，pageSize 默认 20、最大 100；from / to 为发起时间区间（ISO，含 from 不含 to） */
export interface ListQuery {
  page?: number
  pageSize?: number
  keyword?: string
  status?: string
  definitionKey?: string
  from?: string
  to?: string
  initiatorId?: string
  /** 仅待办：只看已超时 */
  overdueOnly?: boolean
  /** 仅投递记录 */
  instanceId?: string
}

/** 流程标识与名称（列表的流程筛选） */
export interface FlowSummary {
  key: string
  name: string
}

export interface ApprovalDefinition {
  id: string
  key: string
  version: number
  name: string
  definitionJson: string
  isPublished: boolean
  createdAt: string
}

// ───── 设计器元数据 ─────

export type ActionParameterKind = 'Text' | 'TextArea' | 'Code' | 'Number' | 'Select' | 'Boolean'

export interface ActionParameterMeta {
  name: string
  label: string
  kind: ActionParameterKind
  required: boolean
  defaultValue: string | null
  options: string[] | null
  help: string | null
  /** 为 null 表示该参数不做模板替换（如脚本代码） */
  template: 'Raw' | 'Json' | 'Url' | null
}

export interface ActionMeta {
  type: string
  displayName: string
  description: string | null
  parameters: ActionParameterMeta[]
  supportsSync: boolean
  supportsAsync: boolean
}

export interface ChannelMeta {
  name: string
  displayName: string
}

export interface TemplateVariableMeta {
  name: string
  alias: string
  description: string
}

export interface ApprovalMeta {
  approverTypes: string[]
  recipientTypes: string[]
  actions: ActionMeta[]
  channels: ChannelMeta[]
  templateVariables: TemplateVariableMeta[]
  defaultTemplates: Record<string, string>
}

// ───── 站内信与投递记录 ─────

export interface ApprovalMessage {
  id: string
  userId: string
  title: string
  content: string
  event: string
  instanceId: string | null
  nodeId: string | null
  isRead: boolean
  createdAt: string
  readAt: string | null
}

export type OutboxStatus = 'Pending' | 'Succeeded' | 'Failed'

export interface OutboxMessage {
  id: string
  kind: 'Notification' | 'Action'
  target: string
  instanceId: string | null
  nodeId: string | null
  description: string | null
  payloadJson: string
  status: OutboxStatus
  attempts: number
  maxAttempts: number
  nextAttemptAt: string
  lastError: string | null
  createdAt: string
  completedAt: string | null
}

// ───── 当前用户与用户目录 ─────

export interface ApprovalMe {
  userId: string
  name: string
  roles: string[]
  isAdmin: boolean
  /** 可查看完整流程定义（流程定义列表、设计器）：管理员或后端 DefinitionReaders 中的角色 */
  canViewDefinitions: boolean
  /** 后端启用了表单审批（AddFormApprovals） */
  formsEnabled: boolean
  /** 表单审批的业务类型（该类型的申请被退回后可修改表单重新提交），未启用时为 null */
  formBusinessType: string | null
}

// ───── 表单审批 ─────

export interface StartableFlow {
  key: string
  name: string
  version: number
  category: string | null
  description: string | null
}

export interface StartForm {
  definitionId: string
  key: string
  name: string
  version: number
  settings: FlowSettings
  form: FormSchema
}

export interface DirectoryUser {
  id: string
  name: string
  roles: string[]
}
