import { computed, effectScope, shallowRef, watch } from 'vue'
import { getApprovalClientCore, registerClientCleanup, useApprovalClient, type ApprovalClient } from '@ui/client'

const users = new WeakMap<ApprovalClient, ReturnType<typeof createUserState>>()
function createUserState(client: ApprovalClient) {
  const core = getApprovalClientCore(client)
  const me = shallowRef(core.me.value)
  const scope = effectScope(true)
  const unsubscribe = core.me.subscribe(() => { me.value = core.me.value })
  scope.run(() => watch(client.userKey, () => { me.value = null; void core.reloadUser() }, { immediate: true, flush: 'sync' }))
  registerClientCleanup(client, () => { scope.stop(); unsubscribe(); me.value = null; users.delete(client) })
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
    reload: core.reloadUser,
  }
}
export function useApprovalUser() {
  const client = useApprovalClient()
  let state = users.get(client)
  if (!state) users.set(client, state = createUserState(client))
  return state
}
