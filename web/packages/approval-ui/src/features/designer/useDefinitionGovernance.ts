import { shallowRef } from 'vue'
import type { ApprovalApiClient } from '@ui/api/approval'
import { ApiError } from '@ui/api/http'
import { useApprovalClient } from '@ui/client'
import { createCommandIntent, useCommandIntent } from '@ui/composables/useCommandIntent'
import type { DefinitionGovernance } from '@ui/contracts/api'

type DraftApi = Pick<ApprovalApiClient, 'definitionGovernance' | 'saveDesignDraft' | 'publishDesignDraft'>
export function createDefinitionEditor(api: DraftApi, key: () => string, intent: ReturnType<typeof createCommandIntent>) {
  const state = shallowRef<DefinitionGovernance | null>(null)
  async function load(signal?: AbortSignal) {
    const owner = key()
    try { const result = await api.definitionGovernance(owner, signal); if (key() === owner) state.value = result }
    catch (error) { if (error instanceof ApiError && error.status === 404) { if (key() === owner) state.value = null } else throw error }
    return state.value
  }
  async function save(name: string, definitionJson: string, sourceDefinitionId: string | null) {
    const owner = key()
    const body = { name, definitionJson, sourceDefinitionId, expectedRevision: state.value?.revision ?? 0 }
    const result = await intent.run(`draft:${owner}`, body, (payload, id) => api.saveDesignDraft(owner, payload, id))
    if (key() === owner) state.value = result
    return result
  }
  async function publish(changeNote: string) {
    const owner = key(); const current = state.value
    if (!current?.draftJson) throw new Error('请先保存设计草稿')
    const result = await intent.run(owner, { expectedRevision: current.revision, changeNote }, (body, id) => api.publishDesignDraft(owner, body, id))
    if (key() === owner) state.value = { ...current, revision: current.revision + 1, lastVersion: result.version,
      activeDefinitionId: result.id, draftSourceDefinitionId: result.id, hasDraftChanges: false }
    return result
  }
  return { state, intent, load, save, publish }
}
export function useDefinitionGovernance(key: () => string) {
  return createDefinitionEditor(useApprovalClient().api, key, useCommandIntent('definition-publish'))
}
