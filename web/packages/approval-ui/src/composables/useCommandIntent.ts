import { shallowRef } from 'vue'
import { useApprovalClient } from '@ui/client'
import { createCommandIntent as createCoreIntent } from '@approval/shared/commands/commandIntent'

export function createCommandIntent(...args: Parameters<typeof createCoreIntent>) {
  const core = createCoreIntent(...args)
  const pending = shallowRef(core.pending.value)
  core.pending.subscribe(() => { pending.value = core.pending.value })
  return { ...core, pending }
}
export type CommandIntent = ReturnType<typeof createCommandIntent>
export function useCommandIntent(namespace: string, options: { retainOnError?: boolean } = {}) {
  const client = useApprovalClient()
  return createCommandIntent(() => `${client.userKey.value}:${client.config().apiBase}:${namespace}`, options)
}
