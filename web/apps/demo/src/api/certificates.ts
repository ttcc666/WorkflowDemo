import { http } from '@approval/ui'
import type { ListQuery, PagedResult } from '@approval/ui'
import type { Certificate, CertificateData, CertificateRequest, CertificateRequestDetail } from './types'

export const certificateApi = {
  list: (query: ListQuery, signal?: AbortSignal) =>
    http.get<PagedResult<Certificate>>(`/api/certificates?${new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 20), keyword: query.keyword ?? '' })}`, signal),
  get: (id: string, signal?: AbortSignal) => http.get<Certificate>(`/api/certificates/${id}`, signal),
  requests: (certificateId: string, query: ListQuery, signal?: AbortSignal) =>
    http.get<PagedResult<CertificateRequest>>(`/api/certificates/${certificateId}/requests?page=${query.page ?? 1}&pageSize=${query.pageSize ?? 20}`, signal),
  requestDetail: (requestId: string, signal?: AbortSignal) =>
    http.get<CertificateRequestDetail>(`/api/certificate-requests/${requestId}`, signal),

  submitCreate: (data: CertificateData, key: string, sourceRequestId?: string) =>
    http.post<{ requestId: string }>('/api/certificates', { ...data, sourceRequestId }, { 'Idempotency-Key': key }),
  submitUpdate: (id: string, data: CertificateData, key: string, sourceRequestId?: string) =>
    http.put<{ requestId: string }>(`/api/certificates/${id}`, { ...data, sourceRequestId }, { 'Idempotency-Key': key }),
  submitRevoke: (id: string, reason: string, key: string, sourceRequestId?: string) =>
    http.post<{ requestId: string }>(`/api/certificates/${id}/revoke`, { reason, sourceRequestId }, { 'Idempotency-Key': key }),
}
