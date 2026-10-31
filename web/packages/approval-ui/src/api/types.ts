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
  | 'Terminate'
  | 'Reassign'
  | 'FormEdit'

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
  /** 结构化附加信息（JSON）：修改表单（FormEdit）时为 {@link FormFieldChange} 数组，已按查看者去掉隐藏字段 */
  details: string | null
  createdAt: string
}

/** 审批中修改表单的一个字段：修改前后的值 */
export interface FormFieldChange {
  field: string
  label: string
  before: unknown
  after: unknown
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
  /** 当前用户看不到的字段（节点字段权限设为隐藏）：已从 formDataJson 与修改轨迹中去掉，展示时应隐藏这些字段 */
  hiddenFields: string[]
}

export type ApprovalAccessLevel = 'Participant' | 'Full'

/** 当前用户在流程中可处理的任务；editableFields 为该任务所在节点可在同意时修改的表单字段（仅表单审批） */
export interface MyTaskResult {
  task: ApprovalTask | null
  editableFields: string[]
}

/** 流程定义校验结果：errors 阻止发布，warnings 为提示（如隐藏字段可能通过标题间接暴露） */
export interface ValidationResult {
  errors: string[]
  warnings: string[]
}

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
  /** 待办均为 Pending；离职交接预览中还有 Waiting（尚未轮到）、Suspended（等待加签） */
  status: TaskStatus
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

// ───── 管理：改派与统计 ─────

export interface ReassignSkip {
  taskId: string
  instanceId: string
  title: string | null
  nodeName: string
  reason: string
}

/** 离职交接（批量改派）结果 */
export interface ReassignResult {
  count: number
  skipped: ReassignSkip[]
}

/** 状态分布与耗时（分钟，只统计已结束的流程） */
export interface FlowStatistics {
  definitionKey: string
  definitionName: string
  total: number
  running: number
  approved: number
  rejected: number
  withdrawn: number
  terminated: number
  averageMinutes: number | null
  p90Minutes: number | null
}

export interface NodeStatistics {
  definitionKey: string
  definitionName: string
  nodeId: string
  nodeName: string
  handled: number
  averageMinutes: number
  overdue: number
}

export interface BacklogItem {
  assigneeKind: AssigneeKind
  assigneeValue: string
  count: number
  overdue: number
  oldestCreatedAt: string
}

export interface ApprovalStatistics {
  from: string
  to: string
  summary: FlowStatistics
  /** 通过率（%）= 通过 /（通过 + 驳回 + 终止），没有已结束流程时为 null */
  approvalRate: number | null
  overdueTasks: number
  /** 流程数超过上限时只统计了最早的部分 */
  truncated: boolean
  flows: FlowStatistics[]
  nodes: NodeStatistics[]
  /** 当前待办积压前 10 的处理人 */
  backlog: BacklogItem[]
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

/** 审批人规则参数的类型：设计器据此生成参数控件 */
export type ApproverValueKind = 'None' | 'User' | 'Role' | 'FormUserField' | 'Level' | 'Text'

/** 审批人 / 通知接收人类型描述 */
export interface ApproverTypeMeta {
  type: string
  displayName: string
  valueKind: ApproverValueKind
  help: string | null
  /** Text 类型的可选值 */
  options: string[] | null
  /** 返回有序列表（如逐级审批），审批节点须为依次审批 */
  ordered: boolean
}

export interface ApprovalMeta {
  approverTypes: ApproverTypeMeta[]
  recipientTypes: ApproverTypeMeta[]
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

/** 草稿列表项 */
export interface DraftSummary {
  id: string
  definitionKey: string
  definitionName: string
  comment: string | null
  createdAt: string
  updatedAt: string
}

export interface DraftDetail extends DraftSummary {
  /** 按最新表单宽松规范化后的数据 */
  formDataJson: string
  /** 流程已更新，草稿已按最新表单调整 */
  adjusted: boolean
}

export interface DirectoryUser {
  id: string
  name: string
  roles: string[]
}
