import { createApp, effectScope, nextTick, ref } from 'vue'
import { expect, it, vi } from 'vitest'
import { createApprovalClient } from '@ui/client'
import { useAsyncData } from '@ui/composables/useAsyncData'

vi.mock('element-plus', () => ({ ElMessage: { error: vi.fn() } }))

it('统计重新查询超限时清除上一次结果并展示错误', async () => {
  const scope = effectScope()
  const filter = ref('small')
  const client = createApprovalClient()
  const app = createApp({}).use(client)
  const state = scope.run(() => app.runWithContext(() => useAsyncData(() => filter.value === 'small'
    ? Promise.resolve({ total: 2 }) : Promise.reject(new Error('当前待办积压超过统计读取预算 2')),
    { watch: [filter], resetOnReload: true, notifyError: false })))!
  try {
    await vi.waitFor(() => expect(state.data.value).toEqual({ total: 2 }))
    filter.value = 'large'
    await vi.waitFor(() => expect(state.error.value).toBe('当前待办积压超过统计读取预算 2'))
    expect(state.data.value).toBeUndefined()
    expect(state.loading.value).toBe(false)
  } finally { scope.stop(); client.dispose() }
})

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
