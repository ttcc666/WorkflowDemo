import { expect, it, vi } from 'vitest'
import { createApprovalApi, type ApprovalApiClient } from '@ui/api/approval'
import { createHttpClient } from '@ui/api/http'
import { resolveApprovalUIOptions } from '@ui/config'

const writes: [string, (api: ApprovalApiClient, key: string) => unknown][] = [
  ['copyDefinition', (a, k) => a.copyDefinition('flow', { targetKey: 'copy', name: 'Copy' }, k)],
  ['submitForm', (a, k) => a.submitForm('flow', {}, '', undefined, k)],
  ['saveDraft', (a, k) => a.saveDraft('flow', null, {}, '', k)],
  ['deleteDraft', (a, k) => a.deleteDraft('draft', k)],
  ['readCarbonCopy', (a, k) => a.readCarbonCopy('cc', k)],
  ['approve', (a, k) => a.approve('task', '', undefined, k)],
  ['reject', (a, k) => a.reject('task', '', undefined, k)],
  ['addSign', (a, k) => a.addSign('task', ['bob'], 'Before', '', k)],
  ['resubmit', (a, k) => a.resubmit('task', '', undefined, k)],
  ['transfer', (a, k) => a.transfer('task', 'bob', '', k)],
  ['withdraw', (a, k) => a.withdraw('instance', '', k)],
  ['saveDesignDraft', (a, k) => a.saveDesignDraft('flow', { name: 'Flow', definitionJson: '{}', expectedRevision: 0, sourceDefinitionId: null }, k)],
  ['publishDesignDraft', (a, k) => a.publishDesignDraft('flow', { expectedRevision: 0, changeNote: '' }, k)],
  ['setDefinitionEnabled', (a, k) => a.setDefinitionEnabled('flow', { expectedRevision: 0, changeNote: '', enabled: true }, k)],
  ['selectDefinitionVersion', (a, k) => a.selectDefinitionVersion('flow', { expectedRevision: 0, changeNote: '', definitionId: 'definition' }, k)],
  ['publish', (a, k) => a.publish('flow', 'Flow', '{}', k)],
  ['markRead', (a, k) => a.markRead('message', k)],
  ['markAllRead', (a, k) => a.markAllRead(k)],
  ['retryOutbox', (a, k) => a.retryOutbox('event', k)],
  ['terminate', (a, k) => a.terminate('instance', 'reason', k)],
  ['reassign', (a, k) => a.reassign('task', 'bob', 'reason', k)],
  ['reassignUser', (a, k) => a.reassignUser('alice', 'bob', 'reason', k)],
]
function setup() {
  const fetcher = vi.fn(async () => new Response('{}', { status: 200 }))
  const config = resolveApprovalUIOptions({ fetcher })
  return { api: createApprovalApi(createHttpClient(() => config), () => config), fetcher }
}
it.each(writes)('%s forwards exactly the supplied stable key', async (_name, send) => {
  const { api, fetcher } = setup()
  await send(api, 'stable-intent'); await send(api, 'stable-intent')
  expect(fetcher).toHaveBeenCalledTimes(2)
  for (const call of fetcher.mock.calls) expect((call as unknown as [string, RequestInit])[1].headers).toMatchObject({ 'Idempotency-Key': 'stable-intent' })
})
it.each(writes)('%s rejects absent or blank keys before sending', (_name, send) => {
  const { api, fetcher } = setup()
  for (const key of [undefined, '', '  ']) expect(() => send(api, key as string)).toThrow('OperationId')
  expect(fetcher).not.toHaveBeenCalled()
})
it('read-only simulation and validation need no key', async () => {
  const { api, fetcher } = setup()
  await api.validate('{}'); await api.simulate('{}', {})
  for (const call of fetcher.mock.calls) expect((call as unknown as [string, RequestInit])[1].headers).not.toHaveProperty('Idempotency-Key')
})
it('file upload keeps the existing raw file boundary without a command key', async () => {
  const { api, fetcher } = setup()
  const file = new File(['content'], 'file.txt')
  await api.uploadFile(file)
  const init = (fetcher.mock.calls[0] as unknown as [string, RequestInit])[1]
  expect(init.body).toBe(file)
  expect(init.headers).not.toHaveProperty('Idempotency-Key')
  expect(init.headers).toMatchObject({ 'X-File-Name': 'file.txt', 'Content-Type': 'application/octet-stream' })
})
