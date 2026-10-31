import { approvalApi } from '@/api/approval'
import { certificateApi } from '@/api/certificates'
import type { CertificateData } from '@/api/types'
import { useAsyncData } from '@/composables/useAsyncData'

export const CERTIFICATE_BUSINESS_TYPE = 'Certificate'

/** 证书列表与提交操作。所有写操作只生成申请单并发起审批，返回申请单 ID。 */
export function useCertificates() {
  const list = useAsyncData((signal) => certificateApi.list(signal), { initial: [] })

  async function submit(action: () => Promise<{ requestId: string }>) {
    const { requestId } = await action()
    await list.reload()
    return requestId
  }

  return {
    certificates: list.data,
    loading: list.loading,
    reload: list.reload,
    submitCreate: (data: CertificateData) => submit(() => certificateApi.submitCreate(data)),
    submitUpdate: (id: string, data: CertificateData) => submit(() => certificateApi.submitUpdate(id, data)),
    submitRevoke: (id: string, reason: string) => submit(() => certificateApi.submitRevoke(id, reason)),
  }
}

/** 申请单对应的审批实例 ID（BusinessKey = 申请单 ID）；驳回后重提会有多条，取最新。 */
export async function findApprovalInstanceId(requestId: string) {
  const instances = await approvalApi.byBusiness(CERTIFICATE_BUSINESS_TYPE, requestId)
  return instances[0]?.id
}
