import { shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ApiError, approvalUserKey, errorMessage } from '@approval/ui'
import { certificateApi } from '@/api/certificates'
import type { Certificate, CertificateData, CertificateRequest } from '@/api/types'
import { findApprovalInstanceId, useCertificates } from './useCertificates'
import { createSubmissionIdentity } from './submissionIdentity'

/** 注册证页面的选中项、重新申请、稳定提交标识与服务端字段错误。 */
export function useCertificateWorkbench() {
  const router = useRouter()
  const route = useRoute()
  const list = useCertificates()
  const selected = shallowRef<Certificate | null>(null)
  const source = shallowRef<CertificateRequest | null>(null)
  const formVisible = shallowRef(false)
  const revokeVisible = shallowRef(false)
  const historyVisible = shallowRef(false)
  const submitting = shallowRef(false)
  const serverErrors = shallowRef<Record<string, string[]>>()
  const submission = createSubmissionIdentity()

  function reset() {
    selected.value = null
    source.value = null
    formVisible.value = revokeVisible.value = historyVisible.value = submitting.value = false
    serverErrors.value = undefined
    submission.reset()
  }
  watch(approvalUserKey, reset)

  function newIntent(certificate: Certificate | null) {
    reset()
    selected.value = certificate
    if (route.query.reapply) {
      const query = { ...route.query }
      delete query.reapply
      void router.replace({ query })
    }
  }
  const openCreate = () => { newIntent(null); formVisible.value = true }
  const openEdit = (certificate: Certificate) => { newIntent(certificate); formVisible.value = true }
  const openRevoke = (certificate: Certificate) => { newIntent(certificate); revokeVisible.value = true }
  const openHistory = (certificate: Certificate) => { newIntent(certificate); historyVisible.value = true }

  watch([() => route.query.reapply, approvalUserKey], async ([id], _previous, cleanup) => {
    if (typeof id !== 'string') return
    reset()
    const controller = new AbortController()
    cleanup(() => controller.abort())
    try {
      const detail = await certificateApi.requestDetail(id, controller.signal)
      if (!detail.request.canReapply) throw new Error('该申请当前不能重新申请')
      const certificate = detail.request.certificateId ? await certificateApi.get(detail.request.certificateId, controller.signal) : null
      if (controller.signal.aborted) return
      source.value = detail.request
      selected.value = certificate
      if (detail.request.operation === 'Revoke') revokeVisible.value = true
      else formVisible.value = true
    } catch (e) { if (!controller.signal.aborted) ElMessage.error(errorMessage(e)) }
  }, { immediate: true })

  async function openRequest(requestId: string) {
    try {
      const instanceId = await findApprovalInstanceId(requestId)
      if (instanceId) await router.push({ name: 'approval-detail', params: { id: instanceId } })
      else ElMessage.warning('未找到对应的审批，或你无权查看')
    } catch (e) { ElMessage.error(errorMessage(e)) }
  }

  async function submit(action: () => Promise<string>, close: () => void) {
    if (submitting.value) return
    const user = approvalUserKey.value
    submitting.value = true
    serverErrors.value = undefined
    try {
      const id = await action()
      if (user !== approvalUserKey.value) return
      close()
      submission.reset()
      ElMessage.success('已提交审核')
      await openRequest(id)
    } catch (e) {
      if (user !== approvalUserKey.value) return
      if (e instanceof ApiError) serverErrors.value = e.errors
      ElMessage.error(errorMessage(e))
    } finally { if (user === approvalUserKey.value) submitting.value = false }
  }

  function onFormSubmit(data: CertificateData) {
    const target = selected.value
    const sourceId = source.value?.id
    const key = submission.forPayload({ operation: target ? 'Update' : 'Create', id: target?.id, data, sourceId })
    void submit(() => target ? list.submitUpdate(target.id, data, key, sourceId) : list.submitCreate(data, key, sourceId),
      () => { formVisible.value = false })
  }
  function onRevokeSubmit(reason: string) {
    const target = selected.value
    if (!target) return
    const sourceId = source.value?.id
    const key = submission.forPayload({ operation: 'Revoke', id: target.id, reason, sourceId })
    void submit(() => list.submitRevoke(target.id, reason, key, sourceId), () => { revokeVisible.value = false })
  }
  return { ...list, selected, source, formVisible, revokeVisible, historyVisible, submitting, serverErrors,
    openCreate, openEdit, openRevoke, openHistory, openRequest, onFormSubmit, onRevokeSubmit }
}
