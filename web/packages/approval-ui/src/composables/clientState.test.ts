import { afterEach, expect, it, vi } from 'vitest'
import { createApp, effectScope, nextTick, shallowRef } from 'vue'
import { createApprovalClient, type ApprovalClient } from '../client'
import { useApprovalUser } from './useApprovalUser'
import { useDirectory } from './useDirectory'

const clients: ApprovalClient[] = []

it('权限使用服务端能力并在撤权和租户切换时立即清空', async () => {
  const tenant = shallowRef('a')
  let allowed = true
  const client = createApprovalClient({ tenantId: () => tenant.value, userKey: () => 'admin',
    fetcher: async () => Response.json({ userId: 'admin', name: 'Admin', roles: ['Admin'], isAdmin: true,
      capabilities: { 'approval.definition.publish': allowed && tenant.value === 'a' } }) })
  clients.push(client)
  const user = createApp({}).use(client).runWithContext(useApprovalUser)
  expect(user.can('approval.definition.publish')).toBe(false)
  await vi.waitFor(() => expect(user.can('approval.definition.publish')).toBe(true))
  allowed = false; await user.reload()
  expect(user.isAdmin.value).toBe(true)
  expect(user.can('approval.definition.publish')).toBe(false)
  allowed = true; await user.reload()
  expect(user.can('approval.definition.publish')).toBe(true)
  tenant.value = 'b'
  expect(user.can('approval.definition.publish')).toBe(false)
  await vi.waitFor(() => expect(user.me.value?.userId).toBe('admin'))
  expect(user.can('approval.definition.publish')).toBe(false)
})

it('isolates identical users when only the tenant changes and rejects late submission responses', async () => {
  const tenant = shallowRef('a')
  const fetcher = vi.fn(async (url: string, _init: RequestInit) => {
    if (url.endsWith('/me')) return Response.json({ userId: 'shared', name: tenant.value, roles: [] })
    return Response.json([{ id: 'shared', name: tenant.value, roles: [] }])
  })
  const client = createApprovalClient({ tenantId: () => tenant.value, userKey: () => 'shared', fetcher })
  clients.push(client)
  const app = createApp({}).use(client)
  const directory = app.runWithContext(useDirectory)
  const user = app.runWithContext(useApprovalUser)
  await vi.waitFor(() => expect(user.me.value?.name).toBe('a'))
  await directory.resolve(['shared'])
  expect(directory.displayName('shared')).toBe('a')
  let complete!: (response: Response) => void
  fetcher.mockImplementationOnce(() => new Promise<Response>(resolve => { complete = resolve }))
  const pending = expect(client.http.post('/submit', { amount: 1 })).rejects.toMatchObject({ name: 'AbortError' })
  const signal = fetcher.mock.calls.at(-1)![1].signal
  const oldKey = client.userKey.value
  tenant.value = 'b'
  expect(client.userKey.value).not.toBe(oldKey)
  expect(signal?.aborted).toBe(true)
  expect(directory.displayName('shared')).toBe('shared')
  expect((await directory.resolve(['shared']))[0]?.name).toBe('b')
  complete(Response.json({ instanceId: 'old-tenant-instance' }))
  await pending
  await vi.waitFor(() => expect(user.me.value?.name).toBe('b'))
})
afterEach(() => clients.splice(0).forEach(client => client.dispose()))

function host(name: string) {
  const identity = shallowRef(name)
  const fetcher = vi.fn(async (url: string, _init: RequestInit) => {
    if (url.endsWith('/me')) return Response.json({ userId: identity.value, name: identity.value, roles: [identity.value + '-role'], isAdmin: false, canViewDefinitions: true, formsEnabled: true, formBusinessType: 'Form' })
    if (url.endsWith('/roles')) return Response.json([identity.value + '-role'])
    return Response.json([{ id: 'shared', name: identity.value + '-name', roles: [] }])
  })
  const client = createApprovalClient({ apiBase: '/' + name, userKey: () => identity.value, fetcher })
  clients.push(client)
  return { identity, fetcher, client, app: createApp({}).use(client) }
}

it('isolates current users directory names and role caches for the same user IDs', async () => {
  const left = host('left')
  const right = host('right')
  const leftUser = left.app.runWithContext(useApprovalUser)
  const rightUser = right.app.runWithContext(useApprovalUser)
  const leftDirectory = left.app.runWithContext(useDirectory)
  const rightDirectory = right.app.runWithContext(useDirectory)
  await vi.waitFor(() => {
    expect(leftUser.userId.value).toBe('left')
    expect(rightUser.userId.value).toBe('right')
  })
  expect((await leftDirectory.resolve(['shared']))[0]?.name).toBe('left-name')
  expect((await rightDirectory.resolve(['shared']))[0]?.name).toBe('right-name')
  await Promise.all([leftDirectory.loadRoles(), rightDirectory.loadRoles()])
  expect(leftDirectory.roles.value).toEqual(['left-role'])
  expect(rightDirectory.roles.value).toEqual(['right-role'])
  await left.app.runWithContext(useDirectory).resolve(['shared'])
  expect(left.fetcher.mock.calls.filter(call => call[0].includes('/directory/users'))).toHaveLength(1)
})

it('rejects late directory responses from a previous identity even if the host ignores abort', async () => {
  const left = host('left')
  const right = host('right')
  const directory = left.app.runWithContext(useDirectory)
  const other = right.app.runWithContext(useDirectory)
  await other.resolve(['shared'])
  let complete!: (response: Response) => void
  left.fetcher.mockImplementationOnce(() => new Promise<Response>(resolve => { complete = resolve }))
  const pending = directory.resolve(['shared'])
  const signal = left.fetcher.mock.calls[0]![1].signal
  left.identity.value = 'next'
  expect(signal?.aborted).toBe(true)
  expect((await directory.resolve(['shared']))[0]?.name).toBe('next-name')
  complete(Response.json([{ id: 'shared', name: 'old-name', roles: [] }]))
  await pending
  expect(directory.displayName('shared')).toBe('next-name')
  expect(other.displayName('shared')).toBe('right-name')
})

it('rejects late user and role responses after an identity switch', async () => {
  const current = host('before')
  const user = current.app.runWithContext(useApprovalUser)
  const directory = current.app.runWithContext(useDirectory)
  await vi.waitFor(() => expect(user.userId.value).toBe('before'))
  let completeUser!: (response: Response) => void
  let completeRoles!: (response: Response) => void
  current.fetcher.mockImplementationOnce(() => new Promise<Response>(resolve => { completeUser = resolve }))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { completeRoles = resolve }))
  const pendingUser = user.reload()
  const pendingRoles = directory.loadRoles()
  const signals = current.fetcher.mock.calls.slice(-2).map(call => call[1].signal)
  current.identity.value = 'after'
  expect(signals.map(signal => signal?.aborted)).toEqual([true, true])
  await vi.waitFor(() => {
    expect(user.userId.value).toBe('after')
    expect(directory.roles.value).toEqual(['after-role'])
  })
  completeUser(Response.json({ userId: 'stale' }))
  completeRoles(Response.json(['stale-role']))
  await Promise.all([pendingUser, pendingRoles])
  expect(user.userId.value).toBe('after')
  expect(directory.roles.value).toEqual(['after-role'])
})

it('keeps shared user watchers alive after the first consumer stops and disposes only their client', async () => {
  const left = host('left')
  const right = host('right')
  const first = effectScope()
  const second = effectScope()
  const firstUser = first.run(() => left.app.runWithContext(useApprovalUser))!
  const remainingUser = second.run(() => left.app.runWithContext(useApprovalUser))!
  const otherUser = right.app.runWithContext(useApprovalUser)
  const directory = left.app.runWithContext(useDirectory)
  await directory.loadRoles()
  first.stop()
  left.identity.value = 'updated'
  await vi.waitFor(() => {
    expect(remainingUser.userId.value).toBe('updated')
    expect(directory.roles.value).toEqual(['updated-role'])
    expect(otherUser.userId.value).toBe('right')
  })
  expect(firstUser.userId.value).toBe('updated')
  left.client.dispose()
  const calls = left.fetcher.mock.calls.length
  left.identity.value = 'disposed'
  right.identity.value = 'other-updated'
  await nextTick()
  await vi.waitFor(() => expect(otherUser.userId.value).toBe('other-updated'))
  expect(remainingUser.me.value).toBeNull()
  expect(left.fetcher).toHaveBeenCalledTimes(calls)
  second.stop()
})

it('keeps an unavailable directory local to its client', async () => {
  const left = host('left')
  const right = host('right')
  left.fetcher.mockResolvedValueOnce(Response.json({ title: 'not configured' }, { status: 501 }))
  const unavailable = left.app.runWithContext(useDirectory)
  const available = right.app.runWithContext(useDirectory)
  expect(await unavailable.search('person')).toEqual([])
  expect(unavailable.available.value).toBe(false)
  expect((await available.resolve(['shared']))[0]?.name).toBe('right-name')
  expect(available.available.value).toBe(true)
})
