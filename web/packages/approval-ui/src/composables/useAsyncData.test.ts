import { createApp, effectScope, nextTick, ref } from 'vue'
import { expect, it, vi } from 'vitest'
import { createApprovalClient } from '../client'
import { useAsyncData } from './useAsyncData'

vi.mock('element-plus', () => ({ ElMessage: { error: vi.fn() } }))

it('身份变化后清空旧数据，拒绝响应不会恢复前一个用户的内容', async () => {
  const scope = effectScope()
  const user = ref('alice')
  const client = createApprovalClient({ userKey: () => user.value })
  const app = createApp({}).use(client)
  let reject: (error: Error) => void = () => { throw new Error('请求未开始') }
  const state = scope.run(() => app.runWithContext(() => useAsyncData(() => user.value === 'alice' ? Promise.resolve('私有资料')
    : new Promise<string>((_resolve, fail) => { reject = fail }), { watch: [user], resetOnReload: true, notifyError: false })))!
  try {
    await vi.waitFor(() => expect(state.data.value).toBe('私有资料'))
    user.value = 'bob'
    await nextTick()
    expect(state.data.value).toBeUndefined()
    reject(new Error('无权查看'))
    await vi.waitFor(() => expect(state.error.value).toBe('无权查看'))
    expect(state.data.value).toBeUndefined()
  } finally { scope.stop(); client.dispose() }
})
