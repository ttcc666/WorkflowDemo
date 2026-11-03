import { expect, it } from 'vitest'
import { ApiError } from '../api/http'
import { createCommandIntent } from './useCommandIntent'

function setup() {
  const entries = new Map<string, string>(); let user = 'tenant-a:alice'; let sequence = 0
  const storage = { getItem: (key: string) => entries.get(key) ?? null, setItem: (key: string, value: string) => { entries.set(key, value) }, removeItem: (key: string) => { entries.delete(key) } }
  const options = { storage, generate: () => `key-${++sequence}` }
  return { entries, options, intent: createCommandIntent(() => user, options), switchUser: (value: string) => { user = value }, recreate: () => createCommandIntent(() => user, options) }
}
it('网络失败和结果待确认重试保持原键，成功后新申请换键', async () => {
  const { intent } = setup(); const keys: string[] = []
  await expect(intent.run('start', { amount: 1 }, async (_, key) => { keys.push(key); throw new TypeError('network') })).rejects.toThrow('network')
  await intent.run('start', { amount: 1 }, async (_, key) => { keys.push(key); return 'original' })
  await intent.run('start', { amount: 1 }, async (_, key) => { keys.push(key) })
  expect(keys).toEqual(['key-1', 'key-1', 'key-2'])
})
it('重新创建页面仍能恢复原键，存储不包含表单正文', async () => {
  const state = setup()
  await expect(state.intent.run('start', { secret: 'private-body' }, async () => { throw new ApiError('unknown', 503, 'CommitOutcomeUnknown') })).rejects.toBeInstanceOf(ApiError)
  expect([...state.entries.values()].join()).not.toContain('private-body')
  await state.recreate().run('start', { secret: 'private-body' }, async (_, key) => { expect(key).toBe('key-1') })
})
it('待确认时阻止改内容自动换键；明确重开后生成新标识', async () => {
  const { intent } = setup()
  await expect(intent.run('start', { amount: 1 }, async () => { throw new Error('offline') })).rejects.toThrow()
  let called = false
  await expect(intent.run('start', { amount: 2 }, async () => { called = true })).rejects.toThrow('尚未确认')
  expect(called).toBe(false)
  intent.reset('start'); await intent.run('start', { amount: 2 }, async (_, key) => { expect(key).toBe('key-2') })
})
it('校验失败没有提交结果，修正内容可生成新键', async () => {
  const { intent } = setup()
  await expect(intent.run('start', { amount: null }, async () => { throw new ApiError('validation', 400, 'Validation') })).rejects.toBeInstanceOf(ApiError)
  await intent.run('start', { amount: 1 }, async (_, key) => { expect(key).toBe('key-2') })
})
it('租户和用户隔离；对象属性顺序不改变同一意图', async () => {
  const state = setup()
  await expect(state.intent.run('start', { a: 1, b: 2 }, async () => { throw new Error('offline') })).rejects.toThrow()
  state.switchUser('tenant-b:alice')
  await state.intent.run('start', { a: 1, b: 2 }, async (_, key) => { expect(key).toBe('key-2') })
  state.switchUser('tenant-a:alice')
  await state.intent.run('start', { b: 2, a: 1 }, async (_, key) => { expect(key).toBe('key-1') })
})
it('请求使用提交瞬间的内容快照', async () => {
  const { intent } = setup(); const value = { amount: 1 }
  const pending = intent.run('start', value, async body => { expect(body.amount).toBe(1) }); value.amount = 2; await pending
})
it('浏览器存储不可用时，同页重试仍使用原键', async () => {
  let sequence = 0
  const storage = { getItem: () => { throw new Error('disabled') }, setItem: () => { throw new Error('quota') }, removeItem: () => { throw new Error('disabled') } }
  const intent = createCommandIntent(() => 'storage-unavailable:alice', { storage, generate: () => `fallback-${++sequence}` })
  await expect(intent.run('start', {}, async () => { throw new TypeError('offline') })).rejects.toThrow('offline')
  await intent.run('start', {}, async (_, key) => { expect(key).toBe('fallback-1') })
  await intent.run('start', {}, async (_, key) => { expect(key).toBe('fallback-2') })
})
it('清理存储失败时，新提交不会误用已完成的键', async () => {
  const entries = new Map<string, string>(); let sequence = 0
  const storage = { getItem: (key: string) => entries.get(key) ?? null, setItem: (key: string, value: string) => { entries.set(key, value) }, removeItem: () => { throw new Error('disabled') } }
  const options = { storage, generate: () => `remove-${++sequence}` }
  await createCommandIntent(() => 'remove-failure:alice', options).run('start', {}, async (_, key) => { expect(key).toBe('remove-1') })
  await createCommandIntent(() => 'remove-failure:alice', options).run('start', {}, async (_, key) => { expect(key).toBe('remove-2') })
})
it('重放保留期结束仍保留原键，核对归档后才允许开始新操作', async () => {
  const { intent } = setup()
  await expect(intent.run('start', { amount: 1 }, async () => { throw new ApiError('expired', 410, 'ReplayExpired') })).rejects.toThrow('expired')
  let sent = false
  await expect(intent.run('start', { amount: 2 }, async () => { sent = true })).rejects.toThrow('尚未确认')
  expect(sent).toBe(false)
  await expect(intent.run('start', { amount: 1 }, async (_, key) => { expect(key).toBe('key-1'); throw new ApiError('expired', 410, 'ReplayExpired') })).rejects.toThrow('expired')
  intent.reset('start')
  await intent.run('start', { amount: 2 }, async (_, key) => { expect(key).toBe('key-2') })
})
