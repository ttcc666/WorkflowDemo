import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { commandScopeAliases, createCommandIntent, resolveCommandNamespace } from '@approval/shared/commands/commandIntent'
import { useApprovalClient, useApprovalIdentity } from '../client'

export function useCommandIntent(
  namespace: string,
  options: { retainOnError?: boolean; permission?: string; canExecute?: (scope: string) => boolean } = {},
) {
  const client = useApprovalClient()
  const identity = useApprovalIdentity()
  const active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const { retainOnError, permission } = options
  const canExecute = useRef(options.canExecute)
  canExecute.current = options.canExecute
  const intent = useMemo(
    () => {
      const resolved = resolveCommandNamespace(namespace)
      const owner = () => `${client.identity.value}:${client.config().apiBase}`
      return createCommandIntent(
        () =>
          `${owner()}:${resolved.namespace}`,
        { retainOnError, aliases: () => resolved.aliases.map(alias => `${owner()}:${alias}`), scopeAliases: scope => commandScopeAliases(resolved.namespace, scope), canExecute: scope => active.current && identity === `${client.identity.value}:${client.version.value}` && (!permission || client.me.value?.capabilities?.[permission] === true) && canExecute.current?.(scope) !== false },
      )
    },
    [client, namespace, identity, retainOnError, permission],
  )
  const pending = useSyncExternalStore(
    intent.pending.subscribe,
    intent.pending.getSnapshot,
    intent.pending.getSnapshot,
  )
  return { ...intent, pending, isCurrent: () => active.current && identity === `${client.identity.value}:${client.version.value}` }
}
