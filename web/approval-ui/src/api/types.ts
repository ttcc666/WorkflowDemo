// 与后端 JSON 对应的类型（camelCase，枚举为字符串）

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
}

export interface TodoItem {
  taskId: string
  instanceId: string
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

export interface ReturnTarget {
  nodeId: string
  nodeName: string
  isInitiator: boolean
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

export interface DemoWebhookState {
  failNext: number
  received: { path: string; idempotencyKey: string | null; body: string; receivedAt: string }[]
}

export interface DemoUser {
  id: string
  roles: string[]
}

// ───── 证书业务 ─────

export type CertificateStatus = 'Active' | 'Revoked'
export type CertOperation = 'Create' | 'Update' | 'Revoke'
export type RequestStatus = 'Reviewing' | 'Approved' | 'Rejected' | 'Withdrawn'

export interface CertificateData {
  certNo: string
  name: string
  holder: string
  certType: string
  issueDate: string
  expireDate: string
}

export interface Certificate extends CertificateData {
  id: string
  status: CertificateStatus
  pendingRequestId: string | null
}

export interface CertificateRequest {
  id: string
  certificateId: string | null
  operation: CertOperation
  snapshot: CertificateData
  reason: string | null
  status: RequestStatus
  applicantId: string
  createdAt: string
  completedAt: string | null
}

export interface FieldChange {
  field: string
  before: unknown
  after: unknown
}

export interface CertificateRequestDetail {
  request: CertificateRequest
  changes: FieldChange[]
  approval: ApprovalInstance | null
}
