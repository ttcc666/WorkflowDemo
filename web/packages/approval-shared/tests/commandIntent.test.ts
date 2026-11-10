import { expect, it, vi } from 'vitest'
import { ApiError } from '../src/api/http'
import { commandScopeAliases, createCommandIntent, resolveCommandNamespace } from '../src/commands/commandIntent'

function storage() {
  const entries = new Map<string, string>()
  return { entries, get length() { return entries.size }, key: (index: number) => [...entries.keys()][index] ?? null,
    getItem: (key: string) => entries.get(key) ?? null, setItem: (key: string, value: string) => { entries.set(key, value) }, removeItem: (key: string) => { entries.delete(key) } }
}
const body = { from: 'alice', to: 'bob', reason: 'private reason' }
it.each([['reject:task', 'return:task'], ['return:task', 'reject:task']])('blocks %s from being replaced by %s while its result is unresolved', async (before, after) => {
  const saved = storage()
  const old = createCommandIntent(() => 'actor:api:approval-detail', { storage: saved, generate: () => 'original' })
  await expect(old.run(before, { original: true }, async () => { throw new TypeError('lost') })).rejects.toThrow('lost')
  const current = createCommandIntent(() => 'actor:api:approval-detail', { storage: saved, scopeAliases: scope => commandScopeAliases('approval-detail', scope) })
  const snapshot = [...saved.entries]
  const send = vi.fn()
  await expect(current.run(after, { changed: true }, send)).rejects.toThrow('尚未确认')
  expect(current.hasPending(after)).toBe(true)
  expect(send).not.toHaveBeenCalled()
  expect([...saved.entries]).toEqual(snapshot)
})
async function unresolved(saved: ReturnType<typeof storage>, identity: string, key = 'original') {
  const old = createCommandIntent(() => identity, { storage: saved, generate: () => key, retainOnError: true })
  await expect(old.run('alice', body, async () => { throw new TypeError('lost response') })).rejects.toThrow('lost response')
}

it.each([
  ['user-reassign', 'bulk-reassign'], ['cc-read', 'carbon-copy-read'], ['form-start', 'start-form'],
  ['message-read', 'message'], ['definition-import', 'import-definition'], ['definition-copy', 'copy-definition'],
])('normalizes %s and preserves its legacy identity under %s', (legacy, canonical) => {
  expect(resolveCommandNamespace(legacy)).toEqual({ namespace: canonical, aliases: [legacy] })
  expect(resolveCommandNamespace(canonical)).toEqual(resolveCommandNamespace(legacy))
})
it('discovers a legacy handover before selecting its sender without deleting or persisting its body', async () => {
  const saved = storage()
  await unresolved(saved, 'admin:api:user-reassign')
  const before = [...saved.entries]
  const current = createCommandIntent(() => 'admin:api:bulk-reassign', { storage: saved, aliases: () => ['admin:api:user-reassign'] })
  expect(current.pendingScopes()).toEqual(['alice'])
  expect(current.hasPending('alice')).toBe(true)
  expect(current.canRetry('alice')).toBe(false)
  expect([...saved.entries]).toEqual(before)
  expect(current.hasConflictingPending('alice')).toBe(false)
  expect(JSON.stringify([...saved.entries])).not.toContain('private reason')
  await expect(current.retry('alice')).rejects.toThrow('没有原请求内容')
  const send = vi.fn()
  await expect(current.run('alice', { ...body, to: 'carol' }, send)).rejects.toThrow('尚未确认')
  expect(send).not.toHaveBeenCalled()
  expect([...saved.entries]).toEqual(before)
})
it('replays an unchanged legacy payload with its original key and clears both identities only on success', async () => {
  const saved = storage()
  await unresolved(saved, 'admin:api:user-reassign')
  const generate = vi.fn(() => 'forbidden-new-key')
  const current = createCommandIntent(() => 'admin:api:bulk-reassign', { storage: saved, generate, aliases: () => ['admin:api:user-reassign'] })
  const send = vi.fn().mockRejectedValueOnce(new ApiError('denied replay', 403)).mockResolvedValueOnce({ count: 2 })
  await expect(current.run('alice', body, send)).rejects.toMatchObject({ status: 403 })
  expect(saved.entries.size).toBe(2)
  expect(await current.retry()).toEqual({ count: 2 })
  expect(send.mock.calls).toEqual([[body, 'original'], [body, 'original']])
  expect(generate).not.toHaveBeenCalled()
  expect(saved.entries.size).toBe(0)
  expect(current.pendingScopes()).toEqual([])
})
it('blocks two distinct legacy and canonical intents until explicit review without losing either key', async () => {
  const saved = storage()
  await unresolved(saved, 'admin:api:user-reassign', 'old-key')
  await unresolved(saved, 'admin:api:bulk-reassign', 'new-key')
  const before = [...saved.entries]
  const current = createCommandIntent(() => 'admin:api:bulk-reassign', { storage: saved, aliases: () => ['admin:api:user-reassign'] })
  const send = vi.fn()
  await expect(current.run('alice', body, send)).rejects.toThrow('多笔未确认')
  expect(current.hasConflictingPending('alice')).toBe(true)
  expect(send).not.toHaveBeenCalled()
  expect(current.canRetry('alice')).toBe(false)
  expect([...saved.entries]).toEqual(before)
  current.reset('alice')
  expect(current.hasPending('alice')).toBe(false)
  expect(saved.entries.size).toBe(0)
})
it('rechecks permission after asynchronous fingerprinting and never sends or allocates a key after revocation', async () => {
  const saved = storage()
  let allowed = true
  let release!: () => void
  let started!: () => void
  const entered = new Promise<void>(resolve => { started = resolve })
  const gate = new Promise<void>(resolve => { release = resolve })
  const digest = crypto.subtle.digest.bind(crypto.subtle)
  const spy = vi.spyOn(crypto.subtle, 'digest').mockImplementation(async (algorithm, data) => {
    const value = await digest(algorithm, data); started(); await gate; return value
  })
  try {
    const generate = vi.fn(() => 'new-key')
    const current = createCommandIntent(() => 'admin', { storage: saved, generate, canExecute: () => allowed })
    const send = vi.fn()
    const result = current.run('alice', body, send)
    await entered; allowed = false; release()
    await expect(result).rejects.toMatchObject({ status: 403 })
    expect(send).not.toHaveBeenCalled()
    expect(generate).not.toHaveBeenCalled()
    expect(saved.entries.size).toBe(0)
  } finally { spy.mockRestore() }
})
it('keeps legacy discovery isolated by actor API namespace and sender including encoded IDs', async () => {
  const saved = storage()
  await unresolved(saved, 'admin:api:user-reassign')
  for (const identity of ['other:api:bulk-reassign', 'admin:other:bulk-reassign', 'admin:api:other-command']) {
    const current = createCommandIntent(() => identity, { storage: saved })
    expect(current.pendingScopes()).toEqual([])
    expect(current.hasPending('alice')).toBe(false)
  }
  const current = createCommandIntent(() => 'admin:api:bulk-reassign', { storage: saved, aliases: () => ['admin:api:user-reassign'] })
  const special = 'user:/中文%'
  await expect(current.run(special, body, async () => { throw new TypeError('network') })).rejects.toThrow('network')
  expect(current.pendingScopes()).toEqual(['alice', special])
  current.reset(special)
  expect(current.pendingScopes()).toEqual(['alice'])
})
it('denies a revoked permission on both new commands and uncertain replays without deleting the original', async () => {
  const saved = storage()
  let allowed = false
  const current = createCommandIntent(() => 'admin', { storage: saved, canExecute: () => allowed, generate: () => 'stable' })
  const send = vi.fn().mockRejectedValueOnce(new TypeError('unknown')).mockResolvedValueOnce('confirmed')
  await expect(current.run('alice', body, send)).rejects.toMatchObject({ status: 403 })
  expect(send).not.toHaveBeenCalled()
  expect(saved.entries.size).toBe(0)
  allowed = true
  await expect(current.run('alice', body, send)).rejects.toThrow('unknown')
  const before = [...saved.entries]
  allowed = false
  await expect(current.retry()).rejects.toMatchObject({ status: 403 })
  expect(send).toHaveBeenCalledTimes(1)
  expect([...saved.entries]).toEqual(before)
  allowed = true
  expect(await current.retry()).toBe('confirmed')
  expect(send.mock.calls).toEqual([[body, 'stable'], [body, 'stable']])
})
