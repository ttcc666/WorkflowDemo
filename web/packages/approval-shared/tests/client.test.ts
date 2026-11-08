import { expect, it, vi } from 'vitest'
import { createApprovalClientCore } from '../src/client'
import { ApiError, createHttpClient } from '../src/api/http'
import { createCommandIntent } from '../src/commands/commandIntent'

function storage() { const values = new Map<string, string>(); return { values, getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value) }, removeItem: (key: string) => { values.delete(key) } } }
it('rejects a response parsed after identity changes and does not call the old unauthorized handler', async () => {
  let identity = 'alice'
  let release!: (body: string) => void
  const body = new Promise<string>(resolve => { release = resolve })
  const onUnauthorized = vi.fn()
  const http = createHttpClient(() => ({ userKey: () => identity, onUnauthorized, fetcher: async () => ({ ok: false, status: 401, json: async () => JSON.parse(await body) }) as Response }))
  const request = http.get('/me').catch(error => error)
  await Promise.resolve(); identity = 'bob'; release('{"title":"old session"}')
  expect(await request).toMatchObject({ name: 'AbortError' })
  expect(onUnauthorized).not.toHaveBeenCalled()
})
it('invalidates directory and current user snapshots across A B A with independent client caches', async () => {
  let identity = 'alice'
  let release!: (body: Response) => void
  const late = new Promise<Response>(resolve => { release = resolve })
  const client = createApprovalClientCore({ userKey: () => identity, fetcher: url => url.includes('keyword=late') ? late : Promise.resolve(new Response(JSON.stringify([{ id: 'u1', name: identity, roles: [], isActive: true }]))) })
  const other = createApprovalClientCore({ userKey: () => 'bob', fetcher: async () => new Response(JSON.stringify([{ id: 'u1', name: 'other', roles: [], isActive: true }])) })
  expect((await client.directory.search('now'))[0]?.name).toBe('alice')
  expect((await other.directory.search('now'))[0]?.name).toBe('other')
  const request = client.directory.search('late')
  identity = 'bob'; client.updateOptions({ userKey: () => identity })
  identity = 'alice'; client.updateOptions({ userKey: () => identity })
  release(new Response(JSON.stringify([{ id: 'late', name: 'secret', roles: [], isActive: true }])))
  expect(await request).toEqual([])
  expect(client.generation).toBe(2)
  expect(client.me.value).toBeNull()
  expect(client.directory.displayName('late')).toBe('late')
  expect(other.directory.displayName('u1')).toBe('other')
  client.dispose(); other.dispose()
})
it('retains frozen payload and original key after an unknown commit and a later forbidden replay', async () => {
  const saved = storage()
  const intent = createCommandIntent(() => 'alice:api:approve', { storage: saved, generate: () => 'stable-key' })
  const payload = { amount: 10, rows: ['first', 'second'] }
  const send = vi.fn().mockRejectedValueOnce(new ApiError('unknown', 503, 'CommitOutcomeUnknown')).mockRejectedValueOnce(new ApiError('denied', 403)).mockResolvedValueOnce({ id: 'original' })
  await expect(intent.run('task', payload, send)).rejects.toMatchObject({ code: 'CommitOutcomeUnknown' })
  payload.amount = 20
  await expect(intent.retry()).rejects.toMatchObject({ status: 403 })
  expect(intent.hasPending('task')).toBe(true)
  expect(saved.values.values().next().value).not.toContain('amount')
  expect(await intent.retry()).toEqual({ id: 'original' })
  expect(send.mock.calls.map(call => call)).toEqual(Array.from({ length: 3 }, () => [{ amount: 10, rows: ['first', 'second'] }, 'stable-key']))
  expect(intent.hasPending('task')).toBe(false)
})
it('rejects changed payload for an unresolved intent and protects an in-flight duplicate', async () => {
  const intent = createCommandIntent(() => 'owner', { storage: storage(), generate: () => 'one' })
  let release!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const send = vi.fn(async () => { await gate; throw new TypeError('disconnected') })
  const first = intent.run('scope', { rows: [1, 2] }, send).catch(error => error)
  await expect(intent.run('scope', { rows: [1, 2] }, send)).rejects.toThrow('正在提交')
  release(); expect(await first).toMatchObject({ message: 'disconnected' })
  await expect(intent.run('scope', { rows: [2, 1] }, send)).rejects.toThrow('尚未确认')
  expect(send).toHaveBeenCalledOnce()
})
it('keeps a failed storage deletion cleared within the current page', async () => {
  const memory = storage()
  const broken = { ...memory, removeItem: () => { throw new Error('storage disabled') } }
  const intent = createCommandIntent(() => 'owner', { storage: broken, generate: () => 'stable' })
  await intent.run('task', { x: 1 }, async () => undefined)
  expect(intent.hasPending('task')).toBe(false)
  expect(memory.values.size).toBe(1)
  expect(intent.pending.value).toBeNull()
})
