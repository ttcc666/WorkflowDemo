import { computed, effectScope, shallowRef, watch } from 'vue'
import { registerClientCleanup, useApprovalClient, type ApprovalClient } from '../client'
import type { ApprovalMe } from '../contracts/api'

const users = new WeakMap<ApprovalClient, ReturnType<typeof createUserState>>()

function createUserState(client: ApprovalClient) {
  const me = shallowRef<ApprovalMe | null>(null)
  const scope = effectScope(true)
  let controller: AbortController | null = null

  async function load() {
    controller?.abort()
    const current = (controller = new AbortController())
    try {
      const result = await client.api.me(current.signal)
      if (current === controller) me.value = result
    } catch {
      if (current === controller) me.value = null
    }
  }

  registerClientCleanup(client, () => {
    scope.stop()
    controller?.abort()
    controller = null
    me.value = null
    users.delete(client)
  })
  scope.run(() => watch(client.userKey, () => {
    me.value = null
    void load()
  }, { immediate: true, flush: 'sync' }))

  return {
    me: computed(() => me.value),
    userId: computed(() => me.value?.userId ?? ''),
    roles: computed(() => me.value?.roles ?? []),
    capabilities: computed(() => me.value?.capabilities ?? {}),
    can: (permission: string) => me.value?.capabilities?.[permission] === true,
    isAdmin: computed(() => me.value?.isAdmin ?? false),
    canViewDefinitions: computed(() => me.value?.canViewDefinitions ?? false),
    formsEnabled: computed(() => me.value?.formsEnabled ?? false),
    formBusinessType: computed(() => me.value?.formBusinessType ?? null),
    reload: load,
  }
}

/** 同一客户端共享当前用户；身份切换会取消旧请求，应用释放后停止监听。 */
export function useApprovalUser() {
  const client = useApprovalClient()
  let state = users.get(client)
  if (!state) users.set(client, state = createUserState(client))
  return state
}
