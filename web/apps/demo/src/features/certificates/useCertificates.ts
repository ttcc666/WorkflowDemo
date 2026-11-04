import { usePagedList } from '@approval/ui'
import { approval } from '@/approval'
import { certificateApi } from '@/api/certificates'
import type { CertificateData } from '@/api/types'

const { api: approvalApi, userKey: approvalUserKey } = approval

export const CERTIFICATE_BUSINESS_TYPE = 'Certificate'

/** 证书列表与提交操作。所有写操作只生成申请单并发起审批，返回申请单 ID。 */
export function useCertificates() {
  const list = usePagedList((query, signal) => certificateApi.list(query, signal), { routePrefix: 'cert_', resetOn: [approvalUserKey] })

  async function submit(action: () => Promise<{ requestId: string }>) {
    const { requestId } = await action()
    await list.reload()
    return requestId
  }

  return {
    ...list,
    certificates: list.items,
    loading: list.loading,
    reload: list.reload,
    submitCreate: (data: CertificateData, key: string, source?: string) => submit(() => certificateApi.submitCreate(data, key, source)),
    submitUpdate: (id: string, data: CertificateData, key: string, source?: string) => submit(() => certificateApi.submitUpdate(id, data, key, source)),
    submitRevoke: (id: string, reason: string, key: string, source?: string) => submit(() => certificateApi.submitRevoke(id, reason, key, source)),
  }
}

/** 申请单对应的审批实例 ID（BusinessKey = 申请单 ID）；驳回后重提会有多条，取最新。只返回当前用户有权查看的审批。 */
export async function findApprovalInstanceId(requestId: string) {
  const instances = await approvalApi.byBusiness(CERTIFICATE_BUSINESS_TYPE, requestId)
  return instances[0]?.id
}
