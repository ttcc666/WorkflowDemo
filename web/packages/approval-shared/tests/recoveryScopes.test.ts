import { expect, it, vi } from 'vitest'
import { createCommandIntent, commandScopeAliases } from '../src/commands/commandIntent'

function storage() {
  const values = new Map<string, string>()
  return { values, get length() { return values.size }, key: (index: number) => [...values.keys()][index] ?? null,
    getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value) }, removeItem: (key: string) => { values.delete(key) } }
}
it('recreates all scopes without bodies or new keys and resets only the reviewed scope', async () => {
  const saved = storage(), generate = vi.fn(() => 'original')
  const old = createCommandIntent(() => 'alice:api:forms', { storage: saved, generate })
  for (const scope of ['draft:a', 'start:a', 'submit:b']) await expect(old.run(scope, { secret: scope }, async () => { throw new TypeError('unknown') })).rejects.toThrow('unknown')
  const next = createCommandIntent(() => 'alice:api:forms', { storage: saved, generate })
  expect(next.pending.value).toBeNull()
  expect(next.pendingScopes()).toEqual(['draft:a', 'start:a', 'submit:b'])
  for (const scope of next.pendingScopes()) { expect(next.canRetry(scope)).toBe(false); await expect(next.retry(scope)).rejects.toThrow('没有原请求内容') }
  expect(generate).toHaveBeenCalledTimes(3)
  expect([...saved.values.values()].join()).not.toContain('secret')
  next.reset('draft:a')
  expect(next.pendingScopes()).toEqual(['start:a', 'submit:b'])
  expect(old.hasPending('start:a')).toBe(true)
  expect(generate).toHaveBeenCalledTimes(3)
})
it('notifies another manager on the same storage and preserves an unrelated active scope', async () => {
  const saved = storage()
  const first = createCommandIntent(() => 'alice', { storage: saved })
  const second = createCommandIntent(() => 'alice', { storage: saved })
  const changed = vi.fn(); const stop = second.revision.subscribe(changed)
  await expect(first.run('draft', {}, async () => { throw new Error('unknown') })).rejects.toThrow()
  await expect(first.run('start', {}, async () => { throw new Error('unknown') })).rejects.toThrow()
  expect(second.pendingScopes()).toEqual(['draft', 'start'])
  const before = changed.mock.calls.length
  first.reset('draft')
  expect(first.pending.value).toBe('start')
  expect(changed.mock.calls.length).toBeGreaterThan(before)
  expect(second.pendingScopes()).toEqual(['start'])
  stop()
})
it('keeps independent in-memory retries and keys when storage is unavailable', async () => {
  const broken = { getItem: () => { throw new Error('disabled') }, setItem: () => { throw new Error('quota') }, removeItem: () => { throw new Error('disabled') } }
  let sequence = 0
  const options = { storage: broken, generate: () => 'key-' + ++sequence }
  const first = createCommandIntent(() => 'alice', options)
  const send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce('confirmed')
  await expect(first.run('draft', { value: 'original' }, send)).rejects.toThrow('lost')
  await expect(first.run('start', {}, async () => { throw new TypeError('lost') })).rejects.toThrow()
  const next = createCommandIntent(() => 'alice', options)
  expect(next.pendingScopes()).toEqual(['draft', 'start'])
  expect(next.canRetry('draft')).toBe(false)
  expect(await first.retry('draft')).toBe('confirmed')
  expect(send.mock.calls).toEqual([[{ value: 'original' }, 'key-1'], [{ value: 'original' }, 'key-1']])
  expect(first.pending.value).toBe('start')
  expect(next.pendingScopes()).toEqual(['start'])
  next.reset('start'); expect(first.pendingScopes()).toEqual([])
})
it('keeps users and legacy namespace and scope aliases isolated during discovery', async () => {
  const saved = storage()
  const old = createCommandIntent(() => 'alice:api:user-reassign', { storage: saved })
  await expect(old.run('sender', {}, async () => { throw new Error('lost') })).rejects.toThrow()
  const current = createCommandIntent(() => 'alice:api:bulk-reassign', { storage: saved, aliases: () => ['alice:api:user-reassign'] })
  expect(current.pendingScopes()).toEqual(['sender'])
  expect(createCommandIntent(() => 'bob:api:bulk-reassign', { storage: saved }).pendingScopes()).toEqual([])
  const task = createCommandIntent(() => 'alice:api:approval-detail', { storage: saved, scopeAliases: scope => commandScopeAliases('approval-detail', scope) })
  await expect(task.run('reject:task', {}, async () => { throw new Error('lost') })).rejects.toThrow()
  expect(task.hasPending('return:task')).toBe(true)
  current.reset('sender')
  expect(task.pendingScopes()).toEqual(['reject:task'])
})
