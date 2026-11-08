import { createHttpClient } from './api/http.js'
import { createApprovalApi } from './api/approval.js'
import { approvalIdentityKey, resolveTransportOptions, type ApprovalTransportOptions } from './transportOptions.js'
import { createStateCell } from './state.js'
import type { ApprovalMe, DirectoryUser } from './contracts/api.js'

export function createApprovalClientCore(options: ApprovalTransportOptions = {}) {
  let config = resolveTransportOptions(options)
  let generation = 0
  const identity = createStateCell(approvalIdentityKey(config))
  const version = createStateCell(0)
  const http = createHttpClient(() => config)
  const api = createApprovalApi(http, () => config)
  const me = createStateCell<ApprovalMe | null>(null)
  const userError = createStateCell<unknown>(null)
  const userLoading = createStateCell(false)
  const directoryUsers = new Map<string, DirectoryUser>()
  const roles = createStateCell<string[]>([])
  const directoryAvailable = createStateCell(true)
  let userController: AbortController | undefined
  let directoryController = new AbortController()
  let roleRequest: Promise<void> | null = null
  let rolesRequested = false
  const callbacks = new Set<() => void>()

  function invalidate(reloadRoles = false) {
    generation++
    http.abortPending()
    userController?.abort()
    directoryController.abort()
    directoryController = new AbortController()
    me.value = null
    userError.value = null
    userLoading.value = false
    directoryUsers.clear()
    roles.value = []
    directoryAvailable.value = true
    roleRequest = null
    for (const cleanup of callbacks) cleanup()
    version.value = generation
    if (reloadRoles && rolesRequested) void directory.loadRoles()
  }
  function updateOptions(next: ApprovalTransportOptions) {
    const nextConfig = resolveTransportOptions(next)
    const changed = nextConfig.apiBase !== config.apiBase || approvalIdentityKey(nextConfig) !== identity.value
    config = nextConfig
    if (changed) { invalidate(true); identity.value = approvalIdentityKey(config) }
  }
  async function reloadUser() {
    const expected = generation
    userController?.abort()
    const controller = userController = new AbortController()
    userLoading.value = true; userError.value = null
    try {
      const response = await api.me(controller.signal)
      if (expected === generation && controller === userController && !controller.signal.aborted) me.value = response
    } catch (error) {
      if (expected === generation && controller === userController) { me.value = null; if ((error as Error)?.name !== 'AbortError') userError.value = error }
    } finally {
      if (expected === generation && controller === userController) userLoading.value = false
    }
  }
  function handleDirectoryError(error: unknown, expected: number) {
    if (expected === generation && (error as { status?: number }).status === 501) directoryAvailable.value = false
    return [] as DirectoryUser[]
  }
  async function remember(load: Promise<DirectoryUser[]>, expected: number) {
    try {
      const users = await load
      if (expected !== generation) return []
      for (const user of users) directoryUsers.set(user.id, user)
      return users
    } catch (error) { return handleDirectoryError(error, expected) }
  }
  const directory = {
    roles, available: directoryAvailable,
    search: (keyword: string, take = 50) => remember(api.users(keyword, take, directoryController.signal), generation),
    async resolve(ids: string[]) {
      const expected = generation
      const missing = ids.filter(id => id && !directoryUsers.has(id))
      if (missing.length) await remember(api.usersByIds(missing, directoryController.signal), expected)
      return ids.map(id => (expected === generation ? directoryUsers.get(id) : undefined) ?? { id, name: id, roles: [], isActive: false })
    },
    loadRoles() {
      rolesRequested = true
      const expected = generation
      roleRequest ??= api.roles(directoryController.signal).then(response => { if (expected === generation) roles.value = response })
        .catch(error => { if (expected === generation) { handleDirectoryError(error, expected); roleRequest = null } })
      return roleRequest
    },
    displayName: (id: string) => directoryUsers.get(id)?.name ?? id,
  }
  return {
    api, http, identity, version, me, userError, userLoading, directory, reloadUser, updateOptions,
    config: () => config,
    get generation() { return generation },
    registerCleanup: (callback: () => void) => { callbacks.add(callback); return () => { callbacks.delete(callback) } },
    dispose: () => { invalidate(); callbacks.clear() },
  }
}
export type ApprovalClientCore = ReturnType<typeof createApprovalClientCore>
