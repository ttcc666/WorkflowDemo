import type { ApprovalInstance } from '@approval/shared'

// 演示与证书业务的类型

export interface DemoWebhookState {
  failNext: number
  received: { path: string; idempotencyKey: string | null; body: string; receivedAt: string }[]
}

export interface DemoUser {
  id: string
  name: string
  roles: string[]
}

export interface CertificateDiagnostics {
  pendingApplications: number
  failedDeliveries: number
  dueDeliveries: number
  overdueTasks: number
  unstaffedTasks: { taskId: string; instanceId: string; nodeName: string; problem: string }[]
  truncated: boolean
  database: { ready: boolean; problems: string[] }
}

// ───── 证书业务 ─────

export type CertificateStatus = 'Active' | 'Revoked'
export type CertOperation = 'Create' | 'Update' | 'Revoke'
export type RequestStatus = 'Reviewing' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Terminated'

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
  effectiveStatus: 'Valid' | 'NotYetValid' | 'Expired' | 'Revoked'
  canManage: boolean
}

export interface CertificateRequest {
  id: string
  certificateId: string | null
  operation: CertOperation
  snapshot: CertificateData
  beforeSnapshot: CertificateData | null
  sourceRequestId: string | null
  reason: string | null
  status: RequestStatus
  applicantId: string
  createdAt: string
  completedAt: string | null
  hasHistoricalBaseline: boolean
  canReapply: boolean
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
