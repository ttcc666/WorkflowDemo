import { approval } from '@/approval'
import { createCertificateApi } from '@demo-shared/certificates'
export const certificateApi = createCertificateApi(approval.http)
