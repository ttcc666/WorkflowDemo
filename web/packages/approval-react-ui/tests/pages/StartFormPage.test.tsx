import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { StrictMode, useState } from 'react'
import { afterEach, expect, it, vi } from 'vitest'
import { createCommandIntent, formatDateTime } from '@approval/shared'
import { ApprovalProvider, createApprovalClient } from '../../src/client'
import StartFormPage from '../../src/pages/StartFormPage'

afterEach(() => { sessionStorage.clear(); vi.restoreAllMocks(); vi.useRealTimers() })
const updatedAt = '2026-11-10T03:04:05.1234567Z'
const result = { start: { instanceId: 'original-instance' }, draft: { draftId: 'original-draft', updatedAt } }
type Operation = keyof typeof result
type Post = { url: string; key: string | null; body: unknown }
function server() {
  const posts: Post[] = [], instances = new Map<string, unknown>(), drafts = new Map<string, unknown>()
  let losses = 1, response: unknown, hold = false, release!: (value: Response) => void
  const fetcher = async (url: string, init: RequestInit) => {
    if (init.method === 'POST' || init.method === 'PUT') {
      const post = { url, key: new Headers(init.headers).get('Idempotency-Key'), body: JSON.parse(String(init.body)) }
      posts.push(post)
      const operation: Operation = url.endsWith('/start') ? 'start' : 'draft'
      const ledger = operation === 'start' ? instances : drafts
      if (!ledger.has(post.key!)) ledger.set(post.key!, post.body)
      else expect(post.body).toEqual(ledger.get(post.key!))
      if (losses-- > 0) throw new TypeError('response lost after commit')
      if (hold) return new Promise<Response>(resolve => { release = resolve })
      return Response.json(response === undefined ? result[operation] : response)
    }
    if (url.endsWith('/me')) return Response.json({ userId: new Headers(init.headers).get('X-User-Id'), formsEnabled: true,
      capabilities: { 'approval.instance.start': true, 'approval.draft.write': true } })
    if (url.includes('/drafts/')) return Response.json({ id: url.split('/').at(-1), formDataJson: JSON.stringify({ subject: url.endsWith('/alice-draft') ? 'Alice saved' : 'Bob saved' }), comment: 'Bob note', updatedAt })
    return Response.json({ key: url.split('/').at(-2), name: '申请页面', form: { fields: [{ key: 'subject', label: '申请主题', type: 'Text', required: true, defaultValue: 'Original' }] }, settings: {} })
  }
  return { fetcher, posts, instances, drafts, lose: () => { losses = 1 }, invalid: (value: unknown) => { response = value }, hold: () => { hold = true }, complete: (operation: Operation) => act(async () => release(Response.json(result[operation]))) }
}
function show(api: ReturnType<typeof server>, entry = '/start/flow?source=workbench&tab=mine') {
  const client = createApprovalClient({ userKey: () => 'alice', headers: () => ({ 'X-User-Id': 'alice' }), fetcher: api.fetcher })
  const router = createMemoryRouter([
    { path: '/start/:key', Component: StartFormPage },
    { path: '/approvals/:id', element: <p>审批详情</p> },
    { path: '/other', element: <p>其他页面</p> },
  ], { initialEntries: [entry] })
  const view = render(<ApprovalProvider client={client}><RouterProvider router={router} /></ApprovalProvider>)
  return { ...view, client, router }
}
async function submit(operation: Operation) {
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original'))
  fireEvent.click(screen.getByRole('button', { name: operation === 'start' ? '提交申请' : '保存草稿' }))
  const retryButton = await screen.findByRole('button', { name: '按原内容重试上次操作' })
  await waitFor(() => expect(retryButton).toBeEnabled())
}
function retry(scope = 'start:flow') {
  const row = within(screen.getByText('操作：' + scope).closest('[data-slot="card"]') as HTMLElement)
  fireEvent.click(row.getByRole('button', { name: '按原内容重试上次操作' }))
}
function retained(scope: string, user = 'alice') {
  return createCommandIntent(() => `${user}:/api/approval:start-form`).hasPending(scope)
}
async function seedOtherFlow() {
  await expect(createCommandIntent(() => 'alice:/api/approval:start-form').run('draft:other', {}, async () => { throw new TypeError('lost') })).rejects.toThrow('lost')
}

it('recovers a committed start after refresh with the original body and key and navigates once', async () => {
  const api = server(), first = show(api)
  await submit('start')
  const original = api.posts[0]!
  first.unmount()
  const view = show(api)
  await screen.findByText(/当前页面已无原请求正文/)
  expect(screen.queryByRole('button', { name: '按原内容重试上次操作' })).not.toBeInTheDocument()
  const mint = vi.spyOn(crypto, 'randomUUID')
  // Refresh keeps only the key/digest. Re-entering the same body restores the live retry.
  api.lose()
  await submit('start')
  retry()
  await waitFor(() => expect(view.router.state.location.pathname).toBe('/approvals/original-instance'))
  expect(await screen.findByText('审批详情')).toBeVisible()
  expect(api.posts).toEqual([original, original, original])
  expect(original.key).toBeTruthy()
  expect(api.instances.size).toBe(1)
  expect(mint).not.toHaveBeenCalled()
  expect(retained('start:flow')).toBe(false)
})

it('recovers a committed draft after refresh updates its saved snapshot and preserves query parameters', async () => {
  const api = server(), first = show(api)
  await submit('draft')
  const original = api.posts[0]!
  first.unmount()
  const view = show(api)
  api.lose()
  await submit('draft')
  retry('draft:flow')
  await waitFor(() => expect(new URLSearchParams(view.router.state.location.search).get('draft')).toBe('original-draft'))
  expect(new URLSearchParams(view.router.state.location.search).get('source')).toBe('workbench')
  expect(new URLSearchParams(view.router.state.location.search).get('tab')).toBe('mine')
  expect(screen.getByText('最后保存 ' + formatDateTime(updatedAt))).toBeVisible()
  expect(screen.queryByText('有未保存的修改')).not.toBeInTheDocument()
  expect(screen.queryByText('response lost after commit')).not.toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original')
  expect(api.posts).toEqual([original, original, original])
  expect(api.drafts.size).toBe(1)
  expect(retained('draft:flow')).toBe(false)
  fireEvent.change(screen.getByRole('textbox', { name: '申请主题' }), { target: { value: 'Edited' } })
  expect(screen.getByText('有未保存的修改')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: '保存草稿' }))
  await waitFor(() => expect(api.posts).toHaveLength(4))
  expect(api.posts[3]!.body).toEqual({ draftId: 'original-draft', formData: { subject: 'Edited' }, comment: '' })
})

it.each(['start', 'draft'] as const)('ignores an old %s recovery after changing the flow and retains other records', async operation => {
  await seedOtherFlow()
  const api = server(), view = show(api)
  await submit(operation)
  api.hold(); retry(`${operation}:flow`)
  await waitFor(() => expect(api.posts).toHaveLength(2))
  await act(async () => { await view.router.navigate('/start/new-flow?source=next') })
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original'))
  await api.complete(operation)
  expect(view.router.state.location.pathname).toBe('/start/new-flow')
  expect(view.router.state.location.search).toBe('?source=next')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(retained(`${operation}:flow`)).toBe(true)
  expect(retained('draft:other')).toBe(true)
})

it.each(['start', 'draft'] as const)('isolates a late %s recovery from the new identity and its draft', async operation => {
  const api = server(), view = show(api)
  await submit(operation)
  api.hold(); retry(`${operation}:flow`)
  await waitFor(() => expect(api.posts).toHaveLength(2))
  await act(async () => { view.client.updateOptions({ userKey: () => 'bob', headers: () => ({ 'X-User-Id': 'bob' }) }) })
  await act(async () => { await view.client.reloadUser() })
  await act(async () => { await view.router.navigate('/start/flow?draft=bob-draft&source=next') })
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Bob saved'))
  await api.complete(operation)
  expect(view.router.state.location.pathname).toBe('/start/flow')
  expect(new URLSearchParams(view.router.state.location.search).get('draft')).toBe('bob-draft')
  expect(screen.getByRole('textbox', { name: '申请说明' })).toHaveValue('Bob note')
  expect(screen.getByText('最后保存 ' + formatDateTime(updatedAt))).toBeVisible()
  expect(retained(`${operation}:flow`, 'alice')).toBe(true)
  expect(retained(`${operation}:flow`, 'bob')).toBe(false)
})

it.each(['start', 'draft'] as const)('does not navigate or update state when %s recovery completes after leaving', async operation => {
  const api = server(), view = show(api)
  await submit(operation)
  api.hold(); retry(`${operation}:flow`)
  await waitFor(() => expect(api.posts).toHaveLength(2))
  await act(async () => { await view.router.navigate('/other') })
  await api.complete(operation)
  expect(view.router.state.location.pathname).toBe('/other')
  expect(view.router.state.location.search).toBe('')
  expect(screen.getByText('其他页面')).toBeVisible()
  expect(retained(`${operation}:flow`)).toBe(true)
})

it('drops the previous identity draft query and keeps the new form clean during a late recovery', async () => {
  const api = server(), view = show(api, '/start/flow?draft=alice-draft&source=workbench')
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Alice saved'))
  fireEvent.click(screen.getByRole('button', { name: '保存草稿' }))
  const retryButton = await screen.findByRole('button', { name: '按原内容重试上次操作' })
  await waitFor(() => expect(retryButton).toBeEnabled())
  api.hold(); retry('draft:flow')
  await waitFor(() => expect(api.posts).toHaveLength(2))
  await act(async () => {
    view.client.updateOptions({ userKey: () => 'bob', headers: () => ({ 'X-User-Id': 'bob' }) })
    await view.client.reloadUser()
  })
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original'))
  expect(view.router.state.location.search).toBe('?source=workbench')
  await api.complete('draft')
  expect(view.router.state.location.search).toBe('?source=workbench')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original')
  expect(retained('draft:flow')).toBe(true)
})

it('isolates draft recovery when the owned provider remounts the page on identity change', async () => {
  const api = server()
  const router = createMemoryRouter([{ path: '/start/:key', Component: StartFormPage }], {
    initialEntries: ['/start/flow?draft=alice-draft&source=workbench'],
  })
  let switchUser!: (actor: string) => void
  function App() {
    const [actor, setActor] = useState('alice')
    switchUser = setActor
    return <ApprovalProvider options={{ userKey: () => actor, headers: () => ({ 'X-User-Id': actor }), fetcher: api.fetcher }}>
      <RouterProvider router={router} />
    </ApprovalProvider>
  }
  render(<StrictMode><App /></StrictMode>)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Alice saved'))
  fireEvent.click(screen.getByRole('button', { name: '保存草稿' }))
  const retryButton = await screen.findByRole('button', { name: '按原内容重试上次操作' })
  await waitFor(() => expect(retryButton).toBeEnabled())
  api.hold(); retry('draft:flow')
  await waitFor(() => expect(api.posts).toHaveLength(2))
  await act(async () => { switchUser('bob') })
  await waitFor(() => expect(router.state.location.search).toBe('?source=workbench'))
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original'))
  await api.complete('draft')
  expect(router.state.location.search).toBe('?source=workbench')
  expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('Original')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(retained('draft:flow')).toBe(true)
})

it.each([
  ['missing instance ID', 'start', {}], ['empty instance ID', 'start', { instanceId: '' }],
  ['whitespace instance ID', 'start', { instanceId: '  ' }], ['wrong instance ID type', 'start', { instanceId: 12 }],
  ['missing draft ID', 'draft', { updatedAt }], ['wrong draft ID type', 'draft', { draftId: 12, updatedAt }],
  ['empty draft ID', 'draft', { draftId: '', updatedAt }], ['invalid draft time', 'draft', { draftId: 'bad', updatedAt: 'not-a-date' }],
  ['missing draft time', 'draft', { draftId: 'bad' }],
] as const)('rejects %s without changing the page or clearing unresolved records', async (_label, operation, response) => {
  await seedOtherFlow()
  const api = server(), view = show(api)
  await submit(operation)
  api.invalid(response); retry(`${operation}:flow`)
  await screen.findByText(/恢复结果缺少有效/)
  expect(view.router.state.location.pathname).toBe('/start/flow')
  expect(view.router.state.location.search).toBe('?source=workbench&tab=mine')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(retained(`${operation}:flow`)).toBe(true)
  expect(retained('draft:other')).toBe(true)
  expect(api.posts).toHaveLength(2)
  expect(api.posts[1]).toEqual(api.posts[0])
})

it('keeps edits made after a lost draft response dirty when the original draft is recovered', async () => {
  const api = server(); show(api)
  await submit('draft')
  fireEvent.change(screen.getByRole('textbox', { name: '申请主题' }), { target: { value: 'New unsaved edit' } })
  retry('draft:flow')
  await screen.findByText('有未保存的修改')
  expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('New unsaved edit')
  expect(api.posts[1]).toEqual(api.posts[0])
})

function tenantApi() {
  const requests: { base: string; url: string; method: string; key: string | null; body?: Record<string, unknown> }[] = []
  const writes = new Map<string, Record<string, unknown>>()
  let hold = false, release!: (response: Response) => void
  const fetcher = async (url: string, init: RequestInit) => {
    const base = url.startsWith('/tenant-a/') ? 'A' : 'B'
    const body = init.body ? JSON.parse(String(init.body)) as Record<string, unknown> : undefined
    const key = new Headers(init.headers).get('Idempotency-Key')
    requests.push({ base, url, method: init.method!, key, body })
    if (init.method === 'PUT' || init.method === 'POST') {
      const stored = writes.get(base + ':' + key)
      if (stored) expect(body).toEqual(stored); else writes.set(base + ':' + key, body!)
      const value = url.endsWith('/start') ? { instanceId: base + '-instance' } : { draftId: base + '-draft', updatedAt }
      if (base === 'A' && hold) return new Promise<Response>(resolve => { release = resolve })
      return Response.json(value)
    }
    if (url.endsWith('/me')) return Response.json({ userId: new Headers(init.headers).get('X-User-Id'), formsEnabled: true, roles: [base + '-role'],
      capabilities: { 'approval.instance.start': true, 'approval.draft.write': true, 'approval.definition.publish': base === 'A' } })
    if (url.includes('/drafts/')) return base === 'A' ? Response.json({ id: 'A-draft', formDataJson: '{"subject":"A saved"}', comment: 'A private note', updatedAt }) : Response.json({ title: 'wrong tenant draft' }, { status: 404 })
    return Response.json({ key: 'flow', name: base + ' form', form: { fields: [{ key: 'subject', label: '申请主题', type: 'Text', required: true, defaultValue: base + ' default' }] }, settings: {} })
  }
  return { fetcher, requests, writes, hold: () => { hold = true }, release: (operation: Operation) => act(async () => release(Response.json(operation === 'start' ? { instanceId: 'A-instance' } : { draftId: 'A-draft', updatedAt }))) }
}
function tenantPage(api: ReturnType<typeof tenantApi>) {
  const client = createApprovalClient({ apiBase: '/tenant-a/api/approval', userKey: () => 'alice', headers: () => ({ 'X-User-Id': 'alice' }), fetcher: api.fetcher })
  const router = createMemoryRouter([{ path: '/start/:key', Component: StartFormPage }, { path: '/approvals/:id', element: <p>审批详情</p> }], {
    initialEntries: ['/start/flow?draft=A-draft&source=workbench&tab=mine'],
  })
  render(<ApprovalProvider client={client}><RouterProvider router={router} /></ApprovalProvider>)
  return { client, router }
}
function tenantIntent(base: 'A' | 'B', actor = 'alice') {
  return createCommandIntent(() => `${actor}:/tenant-${base.toLowerCase()}/api/approval:start-form`)
}
async function changeTenant(client: ReturnType<typeof createApprovalClient>, actor = 'alice') {
  await act(async () => { client.updateOptions({ apiBase: '/tenant-b/api/approval', userKey: () => actor, headers: () => ({ 'X-User-Id': actor }) }) })
}
it('same user API Base switch clears the old draft and query while loading only the new tenant form', async () => {
  const api = tenantApi(), view = tenantPage(api)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('A saved'))
  await changeTenant(view.client)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('B default'))
  expect(view.client.identity.value).toBe('alice')
  expect(view.router.state.location.search).toBe('?source=workbench&tab=mine')
  expect(screen.getByRole('textbox', { name: '申请说明' })).toHaveValue('')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(api.requests.filter(request => request.base === 'B' && request.url.includes('/drafts/'))).toEqual([])
  expect(api.requests.filter(request => request.base === 'B' && request.url.endsWith('/me'))).toHaveLength(1)
})
it.each(['start', 'draft'] as const)('late tenant A %s response cannot change tenant B or clear the A operation', async operation => {
  const api = tenantApi(), view = tenantPage(api)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('A saved'))
  api.hold()
  fireEvent.click(screen.getByRole('button', { name: operation === 'start' ? '提交申请' : '保存草稿' }))
  await waitFor(() => expect(api.writes.size).toBe(1))
  const originalKey = api.requests.find(request => request.method === 'PUT' || request.method === 'POST')!.key
  await changeTenant(view.client)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('B default'))
  await api.release(operation)
  expect(view.router.state.location.pathname).toBe('/start/flow')
  expect(view.router.state.location.search).toBe('?source=workbench&tab=mine')
  expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('B default')
  expect(screen.queryByText(/最后保存/)).not.toBeInTheDocument()
  expect(tenantIntent('A').hasPending(`${operation}:flow`)).toBe(true)
  expect(tenantIntent('B').hasPending(`${operation}:flow`)).toBe(false)
  expect(api.writes.size).toBe(1)
  fireEvent.click(screen.getByRole('button', { name: '保存草稿' }))
  await screen.findByText('草稿已保存')
  const newWrite = api.requests.filter(request => request.base === 'B' && request.method === 'PUT')
  expect(newWrite).toHaveLength(1)
  expect(newWrite[0]!.key).not.toBe(originalKey)
  expect(newWrite[0]!.body).toEqual({ draftId: null, formData: { subject: 'B default' }, comment: '' })
  expect(tenantIntent('A').hasPending(`${operation}:flow`)).toBe(true)
  expect([...Object.values(sessionStorage)].join()).not.toContain('A private note')
})
it('API Base and user switch together reloads new permissions without requesting the old draft', async () => {
  const api = tenantApi(), view = tenantPage(api)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('A saved'))
  await changeTenant(view.client, 'bob')
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('B default'))
  expect(view.client.me.value?.userId).toBe('bob')
  expect(view.client.me.value?.roles).toEqual(['B-role'])
  expect(view.client.me.value?.capabilities?.['approval.definition.publish']).toBe(false)
  expect(api.requests.filter(request => request.base === 'B' && request.url.includes('/drafts/'))).toEqual([])
  expect(view.router.state.location.search).toBe('?source=workbench&tab=mine')
  expect(api.writes.size).toBe(0)
})
it('automatic draft saving after an API Base switch writes only the new tenant snapshot with no old draft ID', async () => {
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
  const api = tenantApi(), view = tenantPage(api)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('A saved'))
  await changeTenant(view.client)
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('B default'))
  fireEvent.change(screen.getByRole('textbox', { name: '申请主题' }), { target: { value: 'B autosaved' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(30000) })
  await waitFor(() => expect(api.writes.size).toBe(1))
  expect(api.requests.filter(request => request.method === 'PUT').map(request => [request.base, request.body])).toEqual([
    ['B', { draftId: null, formData: { subject: 'B autosaved' }, comment: '' }],
  ])
  expect(new URLSearchParams(view.router.state.location.search).get('draft')).toBe('B-draft')
})
