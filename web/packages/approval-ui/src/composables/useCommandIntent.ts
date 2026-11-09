import { shallowRef } from 'vue'
import { getApprovalClientCore, useApprovalClient } from '@ui/client'
import { commandScopeAliases, createCommandIntent as createCoreIntent, resolveCommandNamespace } from '@approval/shared/commands/commandIntent'

export function createCommandIntent(...args: Parameters<typeof createCoreIntent>) {
  const core = createCoreIntent(...args)
  const pending = shallowRef(core.pending.value)
  core.pending.subscribe(() => { pending.value = core.pending.value })
  return { ...core, pending }
}
export type CommandIntent = ReturnType<typeof createCommandIntent>
export function useCommandIntent(namespace: string, options: { retainOnError?: boolean; permission?: string } = {}) {
  const client = useApprovalClient()
  const core = getApprovalClientCore(client)
  const resolved = resolveCommandNamespace(namespace)
  const owner = () => `${client.userKey.value}:${client.config().apiBase}`
  return createCommandIntent(() => `${owner()}:${resolved.namespace}`, { ...options, aliases: () => resolved.aliases.map(alias => `${owner()}:${alias}`), scopeAliases: scope => commandScopeAliases(resolved.namespace, scope), canExecute: options.permission ? () => core.me.value?.capabilities?.[options.permission!] === true : undefined })
}
