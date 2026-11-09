import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { createCommandIntent } from '@approval/shared'
import { ApprovalProvider, createApprovalClient } from '../../src/client'
import { useCommandIntent } from '../../src/hooks/useCommandIntent'
import { CommandRecovery } from '../../src/components/CommandRecovery'

afterEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })
const owner = 'alice:/api/approval:recovery-p2'
async function seed(scope: string) {
  await expect(createCommandIntent(() => owner).run(scope, { secret: 'private body' }, async () => { throw new TypeError('lost') })).rejects.toThrow('lost')
}
function show(scope?: string | null) {
  let intent!: ReturnType<typeof useCommandIntent>
  const recovered = vi.fn(), reset = vi.fn(), recovering = vi.fn()
  const client = createApprovalClient({ userKey: () => 'alice', fetcher: async () => Response.json({ userId: 'alice' }) })
  function View({ selected }: { selected?: string | null }) { intent = useCommandIntent('recovery-p2'); return <CommandRecovery intent={intent} scope={selected} recovered={recovered} onReset={reset} onRecoveringChange={recovering} /> }
  const view = render(<ApprovalProvider client={client}><View selected={scope} /></ApprovalProvider>)
  return { ...view, client, recovered, reset, recovering, get intent() { return intent }, select: (selected?: string | null) => view.rerender(<ApprovalProvider client={client}><View selected={selected} /></ApprovalProvider>) }
}
function row(scope: string) { return within(screen.getByText('操作：' + scope).closest('[data-slot="card"]') as HTMLElement) }
it('discovers every refreshed scope without retrying and reviews only the selected record', async () => {
  await seed('draft:a'); await seed('start:a')
  const mint = vi.spyOn(crypto, 'randomUUID')
  const view = show()
  expect(screen.getAllByText('上次提交结果尚未确认')).toHaveLength(2)
  expect(screen.queryByRole('button', { name: '按原内容重试上次操作' })).not.toBeInTheDocument()
  expect(row('draft:a').getByText(/当前页面已无原请求正文/)).toBeVisible()
  expect(mint).not.toHaveBeenCalled()
  fireEvent.click(row('draft:a').getByRole('button', { name: '核对结果后开始新操作' }))
  expect(view.intent.pendingScopes()).toEqual(['draft:a', 'start:a'])
  fireEvent.click(within(screen.getByRole('alertdialog', { name: '开始新操作' })).getByRole('button', { name: '确认' }))
  expect(screen.queryByText('操作：draft:a')).not.toBeInTheDocument()
  expect(row('start:a').getByText(/禁止自动重试/)).toBeVisible()
  expect(view.intent.pendingScopes()).toEqual(['start:a'])
  expect(view.reset).toHaveBeenCalledWith('draft:a')
  expect(mint).not.toHaveBeenCalled()
})
it('reacts to later peer writes and clears and preserves explicit scope and explicit null', async () => {
  await seed('draft:a'); await seed('start:a')
  const view = show(null)
  expect(screen.queryByText('上次提交结果尚未确认')).not.toBeInTheDocument()
  view.select('start:a')
  expect(screen.getAllByText('上次提交结果尚未确认')).toHaveLength(1)
  expect(screen.queryByText('操作：draft:a')).not.toBeInTheDocument()
  view.select()
  await act(async () => { await seed('submit:b') })
  expect(screen.getAllByText('上次提交结果尚未确认')).toHaveLength(3)
  act(() => createCommandIntent(() => owner).reset('start:a'))
  expect(screen.queryByText('操作：start:a')).not.toBeInTheDocument()
  expect(view.intent.pendingScopes()).toEqual(['draft:a', 'submit:b'])
})
it('retries only the live original body and key and leaves another refreshed scope untouched', async () => {
  await seed('draft:a')
  const view = show(), send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce({ result: 'confirmed' })
  await act(async () => { await expect(view.intent.run('submit:b', { secret: 'original' }, send)).rejects.toThrow('lost') })
  const originalKey = send.mock.calls[0]![1]
  const retry = row('submit:b').getByRole('button', { name: '按原内容重试上次操作' })
  fireEvent.click(retry); fireEvent.click(retry)
  await waitFor(() => expect(view.recovered).toHaveBeenCalledWith({ result: 'confirmed' }, 'submit:b'))
  expect(send.mock.calls).toEqual([[{ secret: 'original' }, originalKey], [{ secret: 'original' }, originalKey]])
  expect(screen.queryByText('操作：submit:b')).not.toBeInTheDocument()
  expect(view.intent.hasPending('draft:a')).toBe(true)
  expect([...Object.values(sessionStorage)].join()).not.toContain('original')
})
it('keeps concurrent recovery rows independent until both original operations settle', async () => {
  const view = show()
  const releases = new Map<string, () => void>()
  for (const scope of ['draft:a', 'start:b']) {
    const send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockImplementationOnce(() => new Promise<void>(resolve => { releases.set(scope, resolve) }))
    await act(async () => { await expect(view.intent.run(scope, { scope }, send)).rejects.toThrow('lost') })
    fireEvent.click(row(scope).getByRole('button', { name: '按原内容重试上次操作' }))
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2))
    expect(send.mock.calls[1]).toEqual(send.mock.calls[0])
  }
  await act(async () => { releases.get('draft:a')!() })
  expect(screen.queryByText('操作：draft:a')).not.toBeInTheDocument()
  expect(row('start:b').getByText('恢复状态：正在按原内容确认结果')).toBeVisible()
  expect(view.recovered.mock.calls).toEqual([[undefined, 'draft:a']])
  expect(view.recovering).toHaveBeenLastCalledWith(true)
  await act(async () => { releases.get('start:b')!() })
  expect(view.recovering).toHaveBeenLastCalledWith(false)
  expect(view.intent.pendingScopes()).toEqual([])
  expect(view.recovered.mock.calls).toEqual([[undefined, 'draft:a'], [undefined, 'start:b']])
})
it('isolates the new identity recovery status while an old identity retry is still pending', async () => {
  const view = show()
  let releaseOld!: () => void
  const oldSend = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockImplementationOnce(() => new Promise<void>(resolve => { releaseOld = resolve }))
  await act(async () => { await expect(view.intent.run('draft:alice', {}, oldSend)).rejects.toThrow('lost') })
  fireEvent.click(row('draft:alice').getByRole('button', { name: '按原内容重试上次操作' }))
  await waitFor(() => expect(oldSend).toHaveBeenCalledTimes(2))
  await act(async () => { view.client.updateOptions({ userKey: () => 'bob' }) })
  expect(screen.queryByText('操作：draft:alice')).not.toBeInTheDocument()
  const newSend = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce('bob confirmed')
  await act(async () => { await expect(view.intent.run('start:bob', {}, newSend)).rejects.toThrow('lost') })
  fireEvent.click(row('start:bob').getByRole('button', { name: '按原内容重试上次操作' }))
  await waitFor(() => expect(view.recovered).toHaveBeenCalledWith('bob confirmed', 'start:bob'))
  expect(view.recovering).toHaveBeenLastCalledWith(false)
  const notifications = view.recovering.mock.calls.length
  await act(async () => { releaseOld() })
  expect(view.recovered.mock.calls).toEqual([['bob confirmed', 'start:bob']])
  expect(view.recovering).toHaveBeenCalledTimes(notifications)
})
