import { createApp, effectScope, shallowRef } from 'vue'
import { expect, it, vi } from 'vitest'
import { createApprovalClient } from '@ui/client'
import { useDefinitionSimulation } from '@ui/features/designer/useDefinitionSimulation'

function setup() {
  const user = shallowRef('alice')
  const raw = shallowRef('{"amount":1}')
  const definition = shallowRef('{"draft":true}')
  const visible = shallowRef(true)
  const fetcher = vi.fn<(url: string, init: RequestInit) => Promise<Response>>()
  const client = createApprovalClient({ userKey: () => user.value, fetcher })
  const app = createApp({}).use(client)
  const scope = effectScope()
  const state = scope.run(() => app.runWithContext(() => useDefinitionSimulation(() => ({
    definitionJson: definition.value, formDataJson: raw.value,
  }), () => visible.value)))!
  return { user, raw, definition, visible, fetcher, scope, state }
}
const result = { isValid: true, isComplete: false, steps: [], errors: [], warnings: [] }

it('模拟发送快照且不修改草稿，输入改变后旧结果立即失效', async () => {
  const s = setup()
  try {
    s.fetcher.mockResolvedValue(new Response(JSON.stringify(result)))
    await s.state.simulate()
    expect(s.state.result.value).toEqual(result)
    expect(s.definition.value).toBe('{"draft":true}')
    expect(JSON.parse(s.fetcher.mock.calls[0]![1].body as string)).toEqual({ definitionJson: '{"draft":true}', formData: { amount: 1 } })
    s.raw.value = '{"amount":2}'
    expect(s.state.result.value).toBeNull()
  } finally { s.scope.stop() }
})
it.each(['input', 'identity', 'closed'] as const)('丢弃 %s 改变后的迟到响应', async kind => {
  const s = setup(); let complete!: (response: Response) => void
  try {
    s.fetcher.mockImplementation(() => new Promise(resolve => { complete = resolve }))
    const pending = s.state.simulate()
    await vi.waitFor(() => expect(complete).toBeTypeOf('function'))
    if (kind === 'input') s.raw.value = '{}'
    if (kind === 'identity') s.user.value = 'bob'
    if (kind === 'closed') s.visible.value = false
    complete(new Response(JSON.stringify(result))); await pending
    expect(s.state.result.value).toBeNull()
  } finally { s.scope.stop() }
})
it('无效输入不发送；网络失败明确报错，重新执行可恢复', async () => {
  const s = setup()
  try {
    s.raw.value = '{'; await s.state.simulate()
    expect(s.state.error.value).toContain('不是有效 JSON'); expect(s.fetcher).not.toHaveBeenCalled()
    s.raw.value = '[]'; await s.state.simulate()
    expect(s.state.error.value).toContain('JSON 对象'); expect(s.fetcher).not.toHaveBeenCalled()
    s.raw.value = '{}'; s.fetcher.mockRejectedValueOnce(new TypeError('offline'))
    await s.state.simulate(); expect(s.state.error.value).toContain('offline'); expect(s.state.loading.value).toBe(false)
    s.fetcher.mockResolvedValueOnce(new Response(JSON.stringify(result)))
    await s.state.simulate(); expect(s.state.error.value).toBe(''); expect(s.state.result.value).toEqual(result)
  } finally { s.scope.stop() }
})
