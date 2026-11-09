import { expect, it } from 'vitest'
import { createSSRApp, h, nextTick } from 'vue'
import { renderToString } from 'vue/server-renderer'
import { createApprovalClient } from '@ui/client'
import { useFlowDesigner } from '@ui/features/designer/useFlowDesigner'
import { newFlowDefinition } from '@approval/shared'

it('Vue acknowledges only the persisted designer snapshot while preserving later edits and undo redo', async () => {
  const client = createApprovalClient({ fetcher: async () => Response.json({}) })
  let designer!: ReturnType<typeof useFlowDesigner>
  const app = createSSRApp({ setup() { designer = useFlowDesigner(); return () => h('div') } }).use(client)
  try {
    await renderToString(app)
    designer.load(newFlowDefinition())
    designer.updateNode('start', { name: '提交时的名称' }); await nextTick()
    const submitted = designer.buildDefinition()
    designer.updateForm({ fields: [{ key: 'later', label: '保存期间的字段', type: 'Text' }] }); await nextTick()
    designer.markSaved(submitted)
    expect(designer.dirty.value).toBe(true)
    expect(designer.buildDefinition().form?.fields[0]?.key).toBe('later')
    designer.undo()
    expect(designer.buildDefinition()).toEqual(submitted)
    expect(designer.dirty.value).toBe(false)
    designer.redo()
    expect(designer.dirty.value).toBe(true)
  } finally { client.dispose() }
})
