import { expect, it, vi } from 'vitest'
import { createHttpClient } from '../src/api/http'

it.each([400, 401, 403, 404, 409, 422, 429, 500])('preserves HTTP %s ProblemDetails and field errors with an unauthorized callback only for 401', async status => {
  const onUnauthorized = vi.fn()
  const http = createHttpClient(() => ({ onUnauthorized, fetcher: async () => Response.json({ title: '业务失败', code: 'test-code', errors: { amount: ['字段错误'] } }, { status }) }))
  await expect(http.get('/me')).rejects.toMatchObject({ status, code: 'test-code', message: '业务失败', errors: { amount: ['字段错误'] } })
  expect(onUnauthorized).toHaveBeenCalledTimes(status === 401 ? 1 : 0)
})
it('does not send a request when the caller signal is already aborted even if the custom fetcher ignores cancellation', async () => {
  const fetcher = vi.fn(async () => Response.json({ private: true }))
  const http = createHttpClient(() => ({ fetcher }))
  const controller = new AbortController(); controller.abort()
  await expect(http.get('/private', controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
  expect(fetcher).not.toHaveBeenCalled()
})
it('rejects a caller cancellation during response parsing even when the custom fetcher completes successfully', async () => {
  let release!: (value: string) => void
  let started!: () => void
  const reading = new Promise<void>(resolve => { started = resolve })
  const body = new Promise<string>(resolve => { release = resolve })
  const response = Response.json({ private: true })
  vi.spyOn(response, 'text').mockImplementation(() => { started(); return body })
  const http = createHttpClient(() => ({ fetcher: async () => response }))
  const controller = new AbortController()
  const result = http.get('/private', controller.signal)
  await reading
  controller.abort(); release('{"private":true}')
  await expect(result).rejects.toMatchObject({ name: 'AbortError' })
})
