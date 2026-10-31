import type { ApprovalInstance } from '@approval/ui'

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
