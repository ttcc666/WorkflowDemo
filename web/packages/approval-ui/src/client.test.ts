import { afterEach, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { approvalApi } from './api/approval'
import { http } from './api/http'
import { createApprovalClient, defaultApprovalClient, useApprovalClient, type ApprovalClient } from './client'
import { configureApprovalUI, type ApprovalUIOptions } from './config'

const clients: ApprovalClient[] = []
const client = (options: ApprovalUIOptions) => {
  const value = createApprovalClient(options)
  clients.push(value)
  return value
}

afterEach(() => {
  clients.splice(0).forEach(value => value.dispose())
  defaultApprovalClient.dispose()
  configureApprovalUI()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

it('keeps transport configuration dynamic headers and panels independent across clients', async () => {
  let token = 'left-1'
  const leftPanel = { name: 'LeftPanel' }
  const rightPanel = { name: 'RightPanel' }
  const panels = { Certificate: leftPanel }
  const leftFetch = vi.fn(async () => Response.json({ userId: 'left' }))
  const rightFetch = vi.fn(async () => Response.json({ userId: 'right' }))
  const left = client({ apiBase: '/left///', headers: () => ({ Authorization: token }), fetcher: leftFetch, businessPanels: panels })
  const right = client({ apiBase: '/right', headers: () => ({ Authorization: 'right' }), fetcher: rightFetch, businessPanels: { Certificate: rightPanel } })

  expect((await left.api.me()).userId).toBe('left')
  expect((await right.api.me()).userId).toBe('right')
  token = 'left-2'
  await left.api.me()
  panels.Certificate = rightPanel
  expect(leftFetch).toHaveBeenLastCalledWith('/left/me', expect.objectContaining({ headers: { Authorization: 'left-2' } }))
  expect(rightFetch).toHaveBeenCalledExactlyOnceWith('/right/me', expect.objectContaining({ headers: { Authorization: 'right' } }))
  expect(left.config().businessPanels.Certificate).toBe(leftPanel)
  expect(right.config().businessPanels.Certificate).toBe(rightPanel)
})

it('preserves legacy global configuration without reconfiguring isolated clients', async () => {
  const isolatedFetch = vi.fn(async () => Response.json({ userId: 'isolated' }))
  const legacyFetch = vi.fn(async (_url: string) => Response.json({ userId: 'legacy' }))
  const isolated = client({ apiBase: '/isolated', fetcher: isolatedFetch })
  configureApprovalUI({ apiBase: '/legacy-one', fetcher: legacyFetch })
  await approvalApi.me()
  configureApprovalUI({ apiBase: '/legacy-two', fetcher: legacyFetch })
  await approvalApi.me()
  await http.get('/business')
  await isolated.api.me()
  expect(legacyFetch.mock.calls.map(call => call[0])).toEqual(['/legacy-one/me', '/legacy-two/me', '/business'])
  expect(isolatedFetch).toHaveBeenCalledExactlyOnceWith('/isolated/me', expect.any(Object))
  expect(useApprovalClient()).toBe(defaultApprovalClient)
})

it('keeps authorization and validation errors on the owning client', async () => {
  const unauthorizedLeft = vi.fn()
  const unauthorizedRight = vi.fn()
  const left = client({ onUnauthorized: unauthorizedLeft, fetcher: async () => Response.json({ title: 'expired', code: 'SessionRevoked' }, { status: 401 }) })
  const right = client({ onUnauthorized: unauthorizedRight, fetcher: async () => Response.json({ title: 'invalid', errors: { certNo: ['required'] } }, { status: 400 }) })
  await expect(left.api.me()).rejects.toMatchObject({ message: 'expired', status: 401, code: 'SessionRevoked' })
  await expect(right.http.post('/business')).rejects.toMatchObject({ message: 'invalid', status: 400, errors: { certNo: ['required'] } })
  expect(unauthorizedLeft).toHaveBeenCalledOnce()
  expect(unauthorizedRight).not.toHaveBeenCalled()
})

it('preserves submission identity raw uploads binary downloads and empty responses', async () => {
  const fetcher = vi.fn(async (url: string) => {
    if (url === '/api/files') return Response.json({ id: 'file-1' })
    if (url === '/binary') return new Response(new Uint8Array([4, 5, 6]), { headers: { 'Content-Type': 'application/pdf' } })
    return new Response(null, { status: 204 })
  })
  const bound = client({ apiBase: '/api', fetcher, headers: () => ({ Authorization: 'current' }) })
  expect(await bound.http.post('/business', { amount: 12 }, { 'Idempotency-Key': 'submission-1' })).toBeUndefined()
  expect(fetcher).toHaveBeenLastCalledWith('/business', expect.objectContaining({
    method: 'POST', body: '{"amount":12}', headers: { Authorization: 'current', 'Content-Type': 'application/json', 'Idempotency-Key': 'submission-1' },
  }))
  const file = new File([new Uint8Array([1, 2, 3])], 'sample file.pdf')
  expect(await bound.api.uploadFile(file)).toEqual({ id: 'file-1' })
  expect(fetcher).toHaveBeenLastCalledWith('/api/files', expect.objectContaining({
    body: file, headers: { Authorization: 'current', 'X-File-Name': 'sample%20file.pdf', 'Content-Type': 'application/octet-stream' },
  }))
  const blob = await bound.http.blob('/binary')
  expect(blob.type).toBe('application/pdf')
  expect([...new Uint8Array(await blob.arrayBuffer())]).toEqual([4, 5, 6])
})

it('passes cancellation through the host transport without converting AbortError', async () => {
  const controller = new AbortController()
  const fetcher = vi.fn((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init.signal?.addEventListener('abort', () => reject(new DOMException('cancelled', 'AbortError')), { once: true })
  }))
  const bound = client({ fetcher })
  const pending = expect(bound.http.get('/pending', controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetcher).toHaveBeenCalledWith('/pending', expect.objectContaining({ signal: controller.signal }))
  controller.abort()
  await pending
})

it('downloads files through the bound client and releases the browser object URL', async () => {
  vi.useFakeTimers()
  const anchor = { href: '', download: '', click: vi.fn() }
  vi.stubGlobal('document', { createElement: vi.fn(() => anchor) })
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:download')
  const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
  const fetcher = vi.fn(async () => new Response('contents'))
  const bound = client({ apiBase: '/own', fetcher })
  await bound.api.downloadFile('file-1', 'certificate.pdf')
  expect(fetcher).toHaveBeenCalledWith('/own/files/file-1', expect.any(Object))
  expect(anchor.href).toBe('blob:download')
  expect(anchor.download).toBe('certificate.pdf')
  expect(anchor.click).toHaveBeenCalledOnce()
  await vi.advanceTimersByTimeAsync(1000)
  expect(revoke).toHaveBeenCalledExactlyOnceWith('blob:download')
})

it('installs a separate client into each Vue app', async () => {
  const left = client({ fetcher: async () => Response.json({ userId: 'left' }) })
  const right = client({ fetcher: async () => Response.json({ userId: 'right' }) })
  const leftApp = createApp({}).use(left)
  const rightApp = createApp({}).use(right)
  configureApprovalUI({ fetcher: async () => { throw new Error('legacy must not be used') } })
  expect(leftApp.runWithContext(useApprovalClient)).toBe(left)
  expect(rightApp.runWithContext(useApprovalClient)).toBe(right)
  expect((await leftApp.runWithContext(() => useApprovalClient().api.me())).userId).toBe('left')
  expect((await rightApp.runWithContext(() => useApprovalClient().api.me())).userId).toBe('right')
})

it('releases a shared client only after its last installed app unmounts', () => {
  const bound = client({})
  const left = createApp({})
  const right = createApp({})
  const unmounts: (() => void)[] = []
  vi.spyOn(left, 'onUnmount').mockImplementation(callback => { unmounts.push(callback) })
  vi.spyOn(right, 'onUnmount').mockImplementation(callback => { unmounts.push(callback) })
  const dispose = vi.spyOn(bound, 'dispose')
  left.use(bound)
  right.use(bound)
  unmounts[0]!()
  expect(dispose).not.toHaveBeenCalled()
  unmounts[1]!()
  expect(dispose).toHaveBeenCalledOnce()
})
