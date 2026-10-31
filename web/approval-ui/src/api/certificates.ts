import { http } from './http'
import type { Certificate, CertificateData, CertificateRequest, CertificateRequestDetail } from './types'

export const certificateApi = {
  list: (signal?: AbortSignal) => http.get<Certificate[]>('/api/certificates', signal),
  requests: (certificateId: string, signal?: AbortSignal) =>
    http.get<CertificateRequest[]>(`/api/certificates/${certificateId}/requests`, signal),
  requestDetail: (requestId: string, signal?: AbortSignal) =>
    http.get<CertificateRequestDetail>(`/api/certificate-requests/${requestId}`, signal),

  submitCreate: (data: CertificateData) => http.post<{ requestId: string }>('/api/certificates', data),
  submitUpdate: (id: string, data: CertificateData) => http.put<{ requestId: string }>(`/api/certificates/${id}`, data),
  submitRevoke: (id: string, reason: string) => http.post<{ requestId: string }>(`/api/certificates/${id}/revoke`, { reason }),
}
