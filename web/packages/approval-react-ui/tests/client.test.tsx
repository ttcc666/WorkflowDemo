import { StrictMode } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { ApprovalProvider, createApprovalClient, useApprovalUser } from '../src/client'

it('keeps two owned Providers independent through StrictMode and identity changes with Forms disabled', async () => {
  const requested: string[] = []
  function Panel({ label }: { label: string }) {
    const user = useApprovalUser()
    return <output aria-label={label}>{user.userId}:{String(user.formsEnabled)}:{String(user.can('approval.definition.publish'))}</output>
  }
  function App({ owner }: { owner: string }) {
    const options = (id: string) => ({ userKey: () => id, headers: () => ({ 'X-User-Id': id }), fetcher: async (_url: string, init?: RequestInit) => {
      const supplied = new Headers(init?.headers).get('X-User-Id')!
      requested.push(supplied)
      return Response.json({ userId: supplied, formsEnabled: false, capabilities: {} })
    } })
    return <StrictMode>
      <ApprovalProvider options={options(owner)}><Panel label="first" /></ApprovalProvider>
      <ApprovalProvider options={options('bob')}><Panel label="second" /></ApprovalProvider>
    </StrictMode>
  }
  const view = render(<App owner="alice" />)
  await waitFor(() => expect(screen.getByLabelText('first')).toHaveTextContent('alice:false:false'))
  expect(screen.getByLabelText('second')).toHaveTextContent('bob:false:false')
  view.rerender(<App owner="carol" />)
  await waitFor(() => expect(screen.getByLabelText('first')).toHaveTextContent('carol:false:false'))
  expect(screen.getByLabelText('second')).toHaveTextContent('bob:false:false')
  expect(new Set(requested)).toEqual(new Set(['alice', 'bob', 'carol']))
})

function Permissions() {
  const user = useApprovalUser()
  return <output aria-label="permissions">{user.userId}:{String(user.can('approval.instance.start'))}</output>
}
it('owned provider reloads permissions when only API Base changes under StrictMode', async () => {
  const calls: string[] = []
  const fetcher = async (url: string) => { calls.push(url); return Response.json({ userId: 'alice', capabilities: { 'approval.instance.start': url.startsWith('/a/') } }) }
  const view = render(<StrictMode><ApprovalProvider options={{ apiBase: '/a', userKey: () => 'alice', fetcher }}><Permissions /></ApprovalProvider></StrictMode>)
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:true'))
  view.rerender(<StrictMode><ApprovalProvider options={{ apiBase: '/b', userKey: () => 'alice', fetcher }}><Permissions /></ApprovalProvider></StrictMode>)
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:false'))
  expect(calls.filter(url => url === '/b/me').length).toBeGreaterThan(0)
})
it('supplied provider reloads new realm permissions after imperative client updateOptions', async () => {
  const calls: string[] = []
  const client = createApprovalClient({ apiBase: '/a', userKey: () => 'alice', fetcher: async url => {
    calls.push(url); return Response.json({ userId: 'alice', capabilities: { 'approval.instance.start': url.startsWith('/a/') } })
  } })
  render(<ApprovalProvider client={client}><Permissions /></ApprovalProvider>)
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:true'))
  await act(async () => { client.updateOptions({ apiBase: '/b' }) })
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:false'))
  expect(calls).toEqual(['/a/me', '/b/me'])
})
it.each(['owned', 'supplied'] as const)('%s provider remounts host local state across API realms for the same user', async mode => {
  const fetcher = async (url: string) => Response.json({ userId: 'alice', capabilities: { 'approval.instance.start': url.startsWith('/a/') } })
  const client = createApprovalClient({ apiBase: '/a', userKey: () => 'alice', fetcher })
  function Content() { return <><Permissions /><input aria-label="host local draft" defaultValue="" /></> }
  function App({ base }: { base: string }) {
    return mode === 'owned'
      ? <ApprovalProvider options={{ apiBase: base, userKey: () => 'alice', fetcher }}><Content /></ApprovalProvider>
      : <ApprovalProvider client={client}><Content /></ApprovalProvider>
  }
  const view = render(<App base="/a" />)
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:true'))
  fireEvent.change(screen.getByLabelText('host local draft'), { target: { value: 'A private local state' } })
  expect(screen.getByLabelText('host local draft')).toHaveValue('A private local state')
  if (mode === 'owned') view.rerender(<App base="/b" />)
  else await act(async () => { client.updateOptions({ apiBase: '/b' }) })
  await waitFor(() => expect(screen.getByLabelText('permissions')).toHaveTextContent('alice:false'))
  expect(screen.getByLabelText('host local draft')).toHaveValue('')
})
