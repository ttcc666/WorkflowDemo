import { effectScope, shallowRef, watch } from 'vue'
import { registerClientCleanup, useApprovalClient, type ApprovalClient } from '../client'
import type { DirectoryUser } from '../contracts/api'

const directories = new WeakMap<ApprovalClient, ReturnType<typeof createDirectory>>()

function createDirectory(client: ApprovalClient) {
  const names = new Map<string, DirectoryUser>()
  const roles = shallowRef<string[]>([])
  const available = shallowRef(true)
  const scope = effectScope(true)
  let rolesPromise: Promise<void> | null = null
  let rolesRequested = false
  let generation = 0
  let identity = client.userKey.value
  let controller = new AbortController()

  function reset(reloadRoles = false) {
    identity = client.userKey.value
    generation++
    controller.abort()
    controller = new AbortController()
    names.clear()
    roles.value = []
    available.value = true
    rolesPromise = null
    if (reloadRoles && rolesRequested) void loadRoles()
  }

  function ensureIdentity() {
    if (identity !== client.userKey.value) reset(true)
  }

  function remember(users: DirectoryUser[], expected: number) {
    if (generation !== expected) return []
    for (const user of users) names.set(user.id, user)
    return users
  }

  function handleError(error: unknown, expected: number) {
    if (generation === expected && (error as { status?: number }).status === 501) available.value = false
    return [] as DirectoryUser[]
  }

  async function search(keyword: string, take = 50) {
    ensureIdentity()
    const expected = generation
    return client.api.users(keyword, take, controller.signal).then(
      users => remember(users, expected), error => handleError(error, expected))
  }

  async function resolve(ids: string[]): Promise<DirectoryUser[]> {
    ensureIdentity()
    const expected = generation
    const missing = ids.filter(id => id && !names.has(id))
    if (missing.length > 0) await client.api.usersByIds(missing, controller.signal).then(
      users => remember(users, expected), error => handleError(error, expected))
    return ids.map(id => (expected === generation ? names.get(id) : undefined) ?? { id, name: id, roles: [], isActive: false })
  }

  function loadRoles() {
    ensureIdentity()
    rolesRequested = true
    const expected = generation
    rolesPromise ??= client.api.roles(controller.signal)
      .then(list => { if (generation === expected) roles.value = list })
      .catch(error => {
        if (generation === expected) {
          handleError(error, expected)
          rolesPromise = null
        }
      })
    return rolesPromise
  }

  scope.run(() => watch(client.userKey, ensureIdentity, { flush: 'sync' }))
  registerClientCleanup(client, () => {
    scope.stop()
    reset()
    controller.abort()
    directories.delete(client)
  })

  return { search, resolve, loadRoles, roles, available, displayName: (id: string) => names.get(id)?.name ?? id }
}

/** 按客户端和身份隔离目录缓存，旧身份的迟到响应不能写回新缓存。 */
export function useDirectory() {
  const client = useApprovalClient()
  let state = directories.get(client)
  if (!state) directories.set(client, state = createDirectory(client))
  return state
}
