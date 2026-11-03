import { afterEach, expect, it, vi } from 'vitest'
import { configureApprovalUI } from '../config'
import { http } from './http'

afterEach(() => configureApprovalUI())

it('uses the host fetcher and preserves submission identity and request cancellation', async () => {
  const fetcher = vi.fn(async (_url: string, _init: RequestInit) => new Response('{"id":"r1"}', { status: 202 }))
  configureApprovalUI({ fetcher, headers: () => ({ Authorization: 'Bearer current' }) })
  expect(await http.post('/api/certificates', { name: '证书' }, { 'Idempotency-Key': 'key' })).toEqual({ id: 'r1' })
  expect(fetcher).toHaveBeenCalledWith('/api/certificates', expect.objectContaining({
    method: 'POST', body: '{"name":"证书"}',
    headers: { Authorization: 'Bearer current', 'Content-Type': 'application/json', 'Idempotency-Key': 'key' },
  }))
  const controller = new AbortController()
  await http.get('/api/approval/me', controller.signal)
  const forwarded = fetcher.mock.calls.at(-1)![1].signal
  expect(forwarded?.aborted).toBe(false)
  controller.abort()
  expect(forwarded?.aborted).toBe(true)
})

it('keeps host ProblemDetails and invokes the unauthorized callback', async () => {
  const onUnauthorized = vi.fn()
  configureApprovalUI({ onUnauthorized, fetcher: async () => new Response('{"title":"登录失效","code":"SessionRevoked"}', { status: 401 }) })
  await expect(http.get('/api/approval/me')).rejects.toMatchObject({ status: 401, code: 'SessionRevoked', message: '登录失效' })
  expect(onUnauthorized).toHaveBeenCalledOnce()
  configureApprovalUI({ fetcher: async () => new Response('{"title":"必填","errors":{"certNo":["必填"]}}', { status: 400 }) })
  await expect(http.post('/api/certificates')).rejects.toMatchObject({ errors: { certNo: ['必填'] } })
})
