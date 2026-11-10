import { getCurrentScope, onScopeDispose, shallowRef } from 'vue'
import { getApprovalClientCore, useApprovalClient } from '@ui/client'
import { commandScopeAliases, createCommandIntent as createCoreIntent, resolveCommandNamespace } from '@approval/shared/commands/commandIntent'

export function createCommandIntent(...args: Parameters<typeof createCoreIntent>) {
  const core = createCoreIntent(...args)
  const pending = shallowRef(core.pending.value)
  const revision = shallowRef(core.revision.value)
  const stopPending = core.pending.subscribe(() => { pending.value = core.pending.value })
  const stopRevision = core.revision.subscribe(() => { revision.value = core.revision.value })
  if (getCurrentScope()) onScopeDispose(() => { stopPending(); stopRevision() })
  return { ...core, pending, pendingScopes: () => { void revision.value; return core.pendingScopes() } }
}
export type CommandIntent = ReturnType<typeof createCommandIntent>
export function useCommandIntent(namespace: string, options: { retainOnError?: boolean; permission?: string; canExecute?: (scope: string) => boolean } = {}) {
  const client = useApprovalClient()
  const core = getApprovalClientCore(client)
  const resolved = resolveCommandNamespace(namespace)
  const owner = () => `${client.userKey.value}:${client.config().apiBase}`
  return createCommandIntent(() => `${owner()}:${resolved.namespace}`, { ...options, aliases: () => resolved.aliases.map(alias => `${owner()}:${alias}`), scopeAliases: scope => commandScopeAliases(resolved.namespace, scope), canExecute: scope => (!options.permission || core.me.value?.capabilities?.[options.permission] === true) && options.canExecute?.(scope) !== false })
}
