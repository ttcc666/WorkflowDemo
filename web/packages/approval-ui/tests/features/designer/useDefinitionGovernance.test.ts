import { describe, expect, it } from 'vitest'
import { createDefinitionEditor } from '@ui/features/designer/useDefinitionGovernance'
import { createCommandIntent } from '@ui/composables/useCommandIntent'
import { ApiError } from '@ui/api/http'
import type { DefinitionGovernance } from '@ui/contracts/api'

const draft: DefinitionGovernance = { key: 'doc', name: 'Doc', category: null, draftJson: '{"nodes":{}}', revision: 1, lastVersion: 0,
  isEnabled: false, activeDefinitionId: null, draftSourceDefinitionId: null, hasDraftChanges: true, updatedAt: null, updatedBy: null, capabilities: {} }
function intent() {
  const entries = new Map<string, string>(); let sequence = 0
  return createCommandIntent(() => 'tenant:user', { generate: () => `key-${++sequence}`,
    storage: { getItem: k => entries.get(k) ?? null, setItem: (k, v) => { entries.set(k, v) }, removeItem: k => { entries.delete(k) } } })
}
describe('设计草稿与发布治理', () => {
  it('保存响应丢失后使用相同键与原编辑令牌重试，成功后才更新本地版本', async () => {
    const requests: Array<{ id: string; revision: number }> = []
    const editor = createDefinitionEditor({ definitionGovernance: async () => draft,
      saveDesignDraft: async (_, body, id) => { requests.push({ id, revision: body.expectedRevision }); if (requests.length === 1) throw new TypeError('offline'); return draft },
      publishDesignDraft: async () => { throw new Error('unexpected publish') } }, () => 'doc', intent())
    await expect(editor.save('Doc', draft.draftJson!, null)).rejects.toThrow('offline')
    expect(editor.state.value).toBeNull()
    await editor.save('Doc', draft.draftJson!, null)
    expect(requests).toEqual([{ id: 'key-1', revision: 0 }, { id: 'key-1', revision: 0 }])
    expect(editor.state.value?.revision).toBe(1)
  })
  it('发布只使用已保存草稿令牌，成功后更新发布版本并保留停用状态', async () => {
    let saved = 0
    const requests: unknown[] = []
    const editor = createDefinitionEditor({ definitionGovernance: async () => draft, saveDesignDraft: async () => { saved++; return draft },
      publishDesignDraft: async (key, body, id) => { requests.push({ key, body, id }); return { id: 'published', key, version: 2 } } }, () => 'doc', intent())
    await editor.load(); await editor.publish('updated review')
    expect(saved).toBe(0)
    expect(requests).toEqual([{ key: 'doc', body: { expectedRevision: 1, changeNote: 'updated review' }, id: 'key-1' }])
    expect(editor.state.value).toMatchObject({ revision: 2, lastVersion: 2, activeDefinitionId: 'published', isEnabled: false, hasDraftChanges: false })
  })
  it('编辑冲突保留当前草稿，缺少已保存草稿时不调用发布', async () => {
    let calls = 0
    const editor = createDefinitionEditor({ definitionGovernance: async () => draft,
      saveDesignDraft: async () => { throw new ApiError('stale revision', 409, 'Conflict') },
      publishDesignDraft: async () => { calls++; return { id: 'bad', key: 'doc', version: 1 } } }, () => 'doc', intent())
    await expect(editor.publish('')).rejects.toThrow('请先保存')
    expect(calls).toBe(0)
    await editor.load(); await expect(editor.save('new', '{}', null)).rejects.toThrow('stale')
    expect(editor.state.value?.name).toBe('Doc'); expect(editor.state.value?.revision).toBe(1)
  })
})
