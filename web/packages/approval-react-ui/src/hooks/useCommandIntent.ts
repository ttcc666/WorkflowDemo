import { useMemo, useSyncExternalStore } from 'react'
import { createCommandIntent } from '@approval/shared/commands/commandIntent'
import { useApprovalClient, useApprovalIdentity } from '../client'

export function useCommandIntent(
  namespace: string,
  options: { retainOnError?: boolean } = {},
) {
  const client = useApprovalClient()
  const identity = useApprovalIdentity()
  const intent = useMemo(
    () =>
      createCommandIntent(
        () =>
          `${client.identity.value}:${client.config().apiBase}:${namespace}`,
        options,
      ),
    [client, namespace, identity, options.retainOnError],
  )
  const pending = useSyncExternalStore(
    intent.pending.subscribe,
    intent.pending.getSnapshot,
    intent.pending.getSnapshot,
  )
  return { ...intent, pending }
}
