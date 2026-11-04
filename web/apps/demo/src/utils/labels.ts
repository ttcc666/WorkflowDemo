import type { TagType } from '@approval/ui'
import type { CertificateStatus, CertOperation, RequestStatus } from '@/api/types'

// 证书业务的显示文字

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
  Terminated: { text: '已终止', type: 'info' },
}

export const certificateFieldLabels: Record<string, string> = {
  CertNo: '注册证编号',
  Name: '注册证名称',
  Holder: '持有人',
  CertType: '证书类型',
  IssueDate: '发证日期',
  ExpireDate: '到期日期',
}

export const certTypeLabels: Record<string, string> = {
  Normal: '普通证书',
  Special: '特种证书',
}
