import { shallowRef, watch, type Ref } from 'vue'
import type { ApprovalApiClient } from '@ui/api/approval'
import type { CarbonCopyRecipients } from '@ui/contracts/api'
import { errorMessage } from '@ui/api/http'

export function useCarbonCopyRecipients(api: Pick<ApprovalApiClient, 'carbonCopyRecipients'>, userKey: Readonly<Ref<string>>, id: () => string, visible: Ref<boolean>) {
  const page = shallowRef(1)
  const pageSize = shallowRef(20)
  const data = shallowRef<CarbonCopyRecipients | null>(null)
  const loading = shallowRef(false)
  const error = shallowRef('')
  watch(userKey, () => { visible.value = false; data.value = null; error.value = '' }, { flush: 'sync' })
  watch([id, visible], () => { page.value = 1 }, { flush: 'sync' })
  watch([visible, id, userKey, page, pageSize], async ([opened, copyId, actor, requestedPage, size], _, cleanup) => {
    const controller = new AbortController()
    cleanup(() => controller.abort())
    const current = () => !controller.signal.aborted && visible.value && id() === copyId && userKey.value === actor && page.value === requestedPage && pageSize.value === size
    data.value = null; error.value = ''; loading.value = false
    if (!opened || !copyId) return
    loading.value = true
    try {
      const result = await api.carbonCopyRecipients(copyId, requestedPage, size, controller.signal)
      if (current()) data.value = result
    } catch (failure) {
      if (current()) error.value = errorMessage(failure)
    } finally {
      if (current()) loading.value = false
    }
  }, { immediate: true })
  return { page, pageSize, data, loading, error }
}
