import { computed, shallowRef } from 'vue'
import { getApprovalClientCore, registerClientCleanup, useApprovalClient, type ApprovalClient } from '@ui/client'

const directories = new WeakMap<ApprovalClient, ReturnType<typeof createDirectory>>()
function createDirectory(client: ApprovalClient) {
  const core = getApprovalClientCore(client)
  const directory = core.directory
  const roles = shallowRef(directory.roles.value)
  const available = shallowRef(directory.available.value)
  const offRoles = directory.roles.subscribe(() => { roles.value = directory.roles.value })
  const offAvailable = directory.available.subscribe(() => { available.value = directory.available.value })
  registerClientCleanup(client, () => { offRoles(); offAvailable(); roles.value = []; available.value = true; directories.delete(client) })
  return { ...directory, roles: computed(() => roles.value), available: computed(() => available.value) }
}
export function useDirectory() {
  const client = useApprovalClient()
  let state = directories.get(client)
  if (!state) directories.set(client, state = createDirectory(client))
  return state
}
