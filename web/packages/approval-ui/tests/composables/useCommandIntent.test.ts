import { expect, it } from 'vitest'
import { ApiError } from '@ui/api/http'
import { createCommandIntent } from '@ui/composables/useCommandIntent'

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

it('CommitOutcomeUnknown 即使由 4xx 返回也保留原键', async () => {
  const { intent } = setup()
  await expect(intent.run('command', {}, async () => { throw new ApiError('unknown', 409, 'CommitOutcomeUnknown') })).rejects.toThrow('unknown')
  expect(intent.hasPending('command')).toBe(true)
  await intent.run('command', {}, async (_, key) => expect(key).toBe('key-1'))
})

it('批量改派部分提交后发生 4xx，重建意图管理器并提供原正文可恢复原键', async () => {
  const state = setup()
  const body = { from: 'alice', to: 'bob', reason: '交接' }
  const manager = createCommandIntent(() => 'bulk:admin', { ...state.options, retainOnError: true })
  const keys: string[] = []
  await expect(manager.run('alice', body, async (_, key) => { keys.push(key); throw new ApiError('permission revoked after one instance', 403) })).rejects.toThrow()
  const recreated = createCommandIntent(() => 'bulk:admin', { ...state.options, retainOnError: true })
  const remainingTasks = 0
  expect(remainingTasks > 0 || recreated.hasPending('alice')).toBe(true)
  await expect(recreated.run('alice', { ...body, to: 'carol' }, async () => { throw new Error('must not send changed body') })).rejects.toThrow('尚未确认')
  await recreated.run('alice', body, async (payload, key) => { expect(payload).toEqual(body); keys.push(key) })
  expect(keys).toEqual(['key-1', 'key-1'])
  expect(recreated.hasPending('alice')).toBe(false)
})

it('同页重复点击不会启动第二个发送；身份切换不会删除原用户的未知结果', async () => {
  const state = setup()
  let release!: () => void
  const send = state.intent.run('command', {}, () => new Promise<void>(resolve => { release = resolve }))
  await expect(state.intent.run('command', {}, async () => { throw new Error('duplicate') })).rejects.toThrow('正在提交')
  await import('vitest').then(({ vi }) => vi.waitFor(() => expect(release).toBeTypeOf('function')))
  state.switchUser('bob'); release()
  await expect(send).rejects.toThrow('身份已切换')
  expect(state.intent.hasPending('command')).toBe(false)
  state.switchUser('tenant-a:alice')
  await state.intent.run('command', {}, async (_, key) => expect(key).toBe('key-1'))
})
it('任务按钮消失后可用内存快照重试，编辑表单不会改变原请求', async () => {
  const state = setup(); const body = { taskId: 'original-task', secret: 'original-body' }
  const sent: { body: typeof body; key: string }[] = []
  await expect(state.intent.run('approve', body, async (payload, key) => {
    sent.push({ body: payload, key })
    if (sent.length === 1) throw new TypeError('response lost')
    return 'confirmed'
  })).rejects.toThrow('response lost')
  body.taskId = 'new-task'; body.secret = 'edited'
  expect(state.intent.canRetry()).toBe(true)
  state.switchUser('bob'); expect(state.intent.canRetry()).toBe(false)
  state.switchUser('tenant-a:alice')
  expect(await state.intent.retry()).toBe('confirmed')
  expect(sent).toEqual([
    { body: { taskId: 'original-task', secret: 'original-body' }, key: 'key-1' },
    { body: { taskId: 'original-task', secret: 'original-body' }, key: 'key-1' },
  ])
  expect(state.intent.canRetry()).toBe(false)
  expect([...state.entries.values()].join()).not.toContain('original-body')
})
