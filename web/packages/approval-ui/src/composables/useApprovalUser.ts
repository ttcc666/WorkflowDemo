import { computed, shallowRef, watch } from 'vue'
import { approvalApi } from '../api/approval'
import type { ApprovalMe } from '../api/types'
import { approvalUserKey } from '../config'

// 全应用共享一份当前用户信息（GET /me），用户标识变化时重新加载
const me = shallowRef<ApprovalMe | null>(null)
let started = false
let controller: AbortController | null = null

async function load() {
  controller?.abort()
  const current = (controller = new AbortController())
  try {
    const result = await approvalApi.me(current.signal)
    if (current === controller) me.value = result
  } catch {
    if (current === controller) me.value = null
  }
}

/** 当前用户（来自后端 /me）：用户 ID、名称、角色、是否管理员、是否启用表单审批。 */
export function useApprovalUser() {
  if (!started) {
    started = true
    watch(approvalUserKey, load, { immediate: true })
  }

  return {
    me: computed(() => me.value),
    userId: computed(() => me.value?.userId ?? ''),
    roles: computed(() => me.value?.roles ?? []),
    isAdmin: computed(() => me.value?.isAdmin ?? false),
    formsEnabled: computed(() => me.value?.formsEnabled ?? false),
    reload: load,
  }
}
